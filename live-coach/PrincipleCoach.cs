using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    public enum DrivingDiscipline { Race, Rally, Drift }

    public sealed class PrincipleLesson
    {
        public string Id, Title, Goal, Explanation, Drill, LookFor, Limitations, SourceTitle, SourceUrl;
        public DrivingDiscipline Discipline;
        public override string ToString() { return Title; }
        internal PrincipleLesson Copy() { return (PrincipleLesson)MemberwiseClone(); }
    }

    public sealed class CoachPrincipleView
    {
        public string Status = "주행 관찰 대기", LessonId = "", Title = "입력과 차의 반응을 연결해 보세요.";
        public string Observation = "", PossibleMechanism = "", NextDrill = "", Limits = "", EventId = "";
        public bool NewFeedback, HasObservation;
        internal CoachPrincipleView Copy() { return (CoachPrincipleView)MemberwiseClone(); }
    }

    // Input thresholds are lesson triggers, never vehicle-specific grip limits.
    // No rule establishes an understeer/oversteer diagnosis, a failed manoeuvre,
    // or a causal performance verdict from an input pattern.
    public sealed class PrincipleCoach
    {
        const double MaxGap = .5, Cooldown = 18, RuleCooldown = 45;
        const int MaxFrames = 600, MaxEvents = 50;
        readonly List<Sample> recent = new List<Sample>();
        readonly List<EventRecord> history = new List<EventRecord>();
        readonly Dictionary<string, RuleState> rules = new Dictionary<string, RuleState>();
        Sample last;
        Pending pending;
        CoachPrincipleView active;
        string context = "", status = "주행 관찰 대기";
        DrivingDiscipline mode;
        double lastDelivered = double.NegativeInfinity, activeUntil, settledSince = double.NaN;
        int eventSequence;

        sealed class Sample
        {
            public double Time, Speed, Throttle, Brake, Steering, Clutch, Handbrake;
            public double Yaw, LateralG, LongitudinalG, Right, Side, Forward;
            public double Distance;
            public int Lap;
            public bool ValidityKnown, ValidLap;
            public bool CaptureClock;
        }
        sealed class RuleState
        {
            public double Since = double.NaN, LastDelivery = double.NegativeInfinity;
            public bool Fired;
        }
        sealed class Pending
        {
            public string Rule, Lesson, Observation, Limits;
            public double DetectedAt;
        }
        sealed class EventRecord
        {
            public string Context;
            public DrivingDiscipline Discipline;
            public double DetectedAt, DeliveredAt;
            public CoachPrincipleView View;
        }

        static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        static bool Unit(double x) { return Finite(x) && x >= 0 && x <= 1; }
        static object Number(double x) { return Finite(x) ? (object)x : null; }
        static string F(double x) { return x.ToString("F1", CultureInfo.InvariantCulture); }
        static Sample Read(TelemetryFrame f)
        {
            return new Sample { Time=f.Time, Speed=f.SpeedKph, Throttle=f.Throttle, Brake=f.Brake,
                Steering=f.Steering, Clutch=Unit(f.Clutch)?f.Clutch:double.NaN,
                Handbrake=Unit(f.Handbrake)?f.Handbrake:double.NaN,
                Yaw=Finite(f.YawRate)&&Math.Abs(f.YawRate)<=10?f.YawRate:double.NaN,
                LateralG=Finite(f.LateralG)&&Math.Abs(f.LateralG)<=10?f.LateralG:double.NaN,
                LongitudinalG=Finite(f.LongitudinalG)&&Math.Abs(f.LongitudinalG)<=10?f.LongitudinalG:double.NaN,
                Right=Finite(f.LocalVelocityRight)&&Math.Abs(f.LocalVelocityRight)<150?f.LocalVelocityRight:double.NaN,
                Side=Finite(f.LocalVelocitySideAbs)&&f.LocalVelocitySideAbs>=0&&f.LocalVelocitySideAbs<150?f.LocalVelocitySideAbs:
                    Finite(f.LocalVelocityRight)&&Math.Abs(f.LocalVelocityRight)<150?Math.Abs(f.LocalVelocityRight):double.NaN,
                Forward=Finite(f.LocalVelocityForward)&&Math.Abs(f.LocalVelocityForward)<200?f.LocalVelocityForward:double.NaN,
                Distance=f.LapDistance, Lap=f.Lap, ValidityKnown=f.ValidLapKnown, ValidLap=f.IsValidLap,CaptureClock=f.TimeIsCaptureClock };
        }
        void ClearLive()
        {
            recent.Clear(); rules.Clear(); last=null; pending=null; active=null;
            activeUntil=0; settledSince=double.NaN; lastDelivered=double.NegativeInfinity;
        }
        public void Reset()
        {
            ClearLive(); history.Clear(); context=""; eventSequence=0; status="새 주행 관찰을 시작합니다.";
        }
        public CoachPrincipleView Disconnect(string message)
        {
            ClearLive(); context=""; status=string.IsNullOrWhiteSpace(message)?"주행 데이터 연결 대기":message;
            return View(null, false);
        }

        public CoachPrincipleView Update(TelemetryFrame frame, DrivingDiscipline discipline)
        {
            if(!Enum.IsDefined(typeof(DrivingDiscipline),discipline))return Disconnect("주행 종목을 선택하세요.");
            if(frame==null || !Finite(frame.Time) || frame.Time<0 || !Finite(frame.SpeedKph) || frame.SpeedKph<0 || frame.SpeedKph>550 ||
                !Unit(frame.Brake) || !Unit(frame.Throttle) || !Finite(frame.Steering) || Math.Abs(frame.Steering)>1.01)
                return Disconnect("입력·속도 신호 부족 · 원인 판단을 보류합니다.");
            if(frame.IsReplay || frame.InPit || !frame.OnTrack)
                return Disconnect(frame.IsReplay?"리플레이 제외":frame.InPit?"피트에서는 주행 관찰을 쉽니다.":"차량 주행 대기");
            if(discipline==DrivingDiscipline.Race && frame.ValidLapKnown && !frame.IsValidLap)
                return Disconnect("무효 랩 · 이번 구간의 코칭을 보류합니다.");
            string key=(frame.GameVersion??"")+"|"+(frame.Car??"")+"|"+(frame.Track??"")+"|"+discipline+"|"+(frame.TimeIsCaptureClock?"capture-clock":"simulation-clock");
            if(context!=key) { ClearLive(); context=key; }
            mode=discipline;
            Sample f=Read(frame);
            if(last!=null && (f.Time<last.Time-.001 || f.Time-last.Time>MaxGap))ClearLive();
            if(last!=null && f.Time<=last.Time+.000001)return View(f,false);
            recent.Add(f);
            while(recent.Count>MaxFrames || (recent.Count>0 && f.Time-recent[0].Time>30))recent.RemoveAt(0);
            status="입력 관찰 중 · 한 번에 한 동작만 실험하세요.";
            if(last!=null)Detect(f);
            bool settled=f.Speed<8 || (f.Brake<.08 && Math.Abs(f.Steering)<.12 &&
                (!Unit(f.Handbrake) || f.Handbrake<.1) && (!Unit(f.Clutch) || f.Clutch<.2) &&
                (!Finite(f.Yaw) || Math.Abs(f.Yaw)<.18) && (!Finite(f.LateralG) || Math.Abs(f.LateralG)<.3) &&
                (!Finite(f.Side) || f.Side<1.5));
            if(settled) { if(!Finite(settledSince))settledSince=f.Time; } else settledSince=double.NaN;
            bool delivered=false;
            if(pending!=null)
            {
                if(f.Time-pending.DetectedAt>12)pending=null;
                else if(Finite(settledSince) && f.Time-settledSince>=.6 && f.Time-lastDelivered>=Cooldown)
                {
                    PrincipleLesson lesson=GetLesson(pending.Lesson);
                    active=new CoachPrincipleView {Status="관찰 → 가능한 원리 → 다음 한 가지 실험",LessonId=lesson.Id,Title=lesson.Title,
                        Observation=pending.Observation,PossibleMechanism=lesson.Explanation,NextDrill=lesson.Drill,
                        Limits=pending.Limits+" "+lesson.Limitations+(f.CaptureClock?" 시간은 데이터 수신 시계 기준입니다.":""),HasObservation=true,EventId="principle-"+(++eventSequence)};
                    activeUntil=f.Time+10;lastDelivered=f.Time;rules[pending.Rule].LastDelivery=f.Time;
                    history.Add(new EventRecord {Context=context,Discipline=mode,DetectedAt=pending.DetectedAt,DeliveredAt=f.Time,View=active.Copy()});
                    if(history.Count>MaxEvents)history.RemoveAt(0);
                    pending=null;delivered=true;
                }
            }
            last=f;
            return View(f,delivered);
        }

        void Observe(string rule,string lesson,bool condition,double seconds,Sample f,string observation,string limits)
        {
            RuleState state;
            if(!rules.TryGetValue(rule,out state)) {state=new RuleState();rules.Add(rule,state);}
            if(!condition) {state.Since=double.NaN;state.Fired=false;return;}
            if(!Finite(state.Since))state.Since=f.Time;
            // A continuing manoeuvre supplies fresh evidence. Keep its queued
            // observation current without emitting again; once the condition
            // ends, the normal expiry still discards stale evidence.
            if(pending!=null && pending.Rule==rule && f.Time-state.Since+1e-8>=seconds)
                pending.DetectedAt=f.Time;
            if(state.Fired || f.Time-state.Since+1e-8<seconds)return;
            state.Fired=true;
            if(pending!=null || f.Time-lastDelivered<Cooldown || f.Time-state.LastDelivery<RuleCooldown)return;
            pending=new Pending {Rule=rule,Lesson=lesson,Observation=observation,Limits=limits,DetectedAt=f.Time};
        }
        Sample[] Window(Sample f,double seconds)
        {return recent.Where(s=>s.Time>=f.Time-seconds-.000001).ToArray();}
        static bool Sideways(Sample f)
        {
            double planarSpeed=Math.Sqrt(f.Side*f.Side+f.Forward*f.Forward);
            return Finite(f.Side) && Finite(f.Forward) && Finite(f.Yaw) && f.Forward>4 &&
                planarSpeed<=f.Speed/3.6*1.25+2 && planarSpeed>=f.Speed/3.6*.5 &&
                f.Side>2 && f.Side/Math.Max(4,Math.Abs(f.Forward))>.12 && Math.Abs(f.Yaw)>.15;
        }
        static int Reversals(Sample[] samples,Func<Sample,double> channel,double threshold)
        {
            int sign=0,count=0;
            foreach(Sample p in samples) {double v=channel(p);if(!Finite(v)||Math.Abs(v)<threshold)continue;int next=Math.Sign(v);if(sign!=0&&next!=sign)count++;sign=next;}
            return count;
        }
        void Detect(Sample f)
        {
            Sample[] shortWindow=Window(f,.35), wide=Window(f,1.4);
            Sample a=shortWindow[0];
            if(mode==DrivingDiscipline.Race)
            {
                Observe("shared-grip","race-grip",f.Speed>50&&f.Brake>=.55&&Math.Abs(f.Steering)>=.2,.4,f,
                    "제동 55% 이상과 조향 입력 20% 이상이 0.4초 이상 겹쳤습니다.",
                    "입력 비율은 타이어 힘의 비율이 아닙니다. 언더스티어·락업 여부는 확인하지 않았습니다.");
                bool release=f.Speed>40&&a.Brake>=.5&&f.Brake<.08&&a.Brake-f.Brake>.45&&f.Time-a.Time>=.15&&shortWindow.All(p=>Math.Abs(p.Steering)>.18);
                Observe("release","race-release",release,0,f,"조향을 유지한 채 약 "+F(f.Time-a.Time)+"초 사이에 브레이크 입력이 크게 줄었습니다.","해제의 빠르기만 관찰했으며, 차가 불안정해졌다는 뜻은 아닙니다.");
                bool alternating=wide.Length>=8&&f.Time-wide[0].Time>=1&&f.Speed>40&&Reversals(wide,p=>p.Steering,.18)>=3;
                Observe("hands","race-hands",alternating,.1,f,"약 1.4초 안에 큰 조향 방향 전환이 세 번 이상 기록됐습니다.","시케인·커브·접촉 회피일 수 있습니다. 조향 각도나 슬립각은 알 수 없습니다.");
                bool rotation=Sideways(f)&&f.Speed>40&&Finite(a.Yaw)&&Math.Abs(f.Yaw)>.7&&Math.Abs(f.Yaw)-Math.Abs(a.Yaw)>.3;
                Observe("rotation","race-recovery",rotation,.1,f,"차체의 옆방향 이동과 회전 속도 증가가 함께 관측됐습니다.","원하는 선회인지 미끄러짐인지 경로 정보만으로 구분하지 못합니다. 오버스티어 확정 진단이 아닙니다.");
            }
            else
            {
                string prefix=mode==DrivingDiscipline.Rally?"rally":"drift";
                Observe("handbrake",prefix+"-handbrake",Unit(f.Handbrake)&&f.Handbrake>.5&&f.Speed>15,.7,f,
                    "핸드브레이크 입력이 50% 이상으로 0.7초 이상 유지됐습니다.","입력은 확인했지만 후륜 잠김이나 구동계 분리 여부는 확인하지 못했습니다.");
                Sample pressed=shortWindow.FirstOrDefault(p=>Unit(p.Clutch)&&p.Clutch>.8);
                bool clutch=Unit(f.Clutch)&&f.Clutch<.2&&pressed!=null&&f.Time-pressed.Time>=.08&&f.Throttle>.4&&f.Speed>15;
                Observe("clutch",prefix+"-clutch",clutch,0,f,"가속 입력 중 클러치 입력이 눌림에서 해제로 바뀌었습니다.","자동 클러치·변속 보조일 수 있습니다. 회전수·기어·휠 속도가 없어 클러치 킥의 성공을 판정하지 않습니다.");
                if(mode==DrivingDiscipline.Rally)
                {
                    Observe("rally-turn-brake","rally-load",f.Speed>20&&f.Brake>.2&&Math.Abs(f.Steering)>.2,.4,f,
                        "제동과 조향 입력이 0.4초 이상 동시에 이어졌습니다.","그래벌인지 타막인지 알 수 없습니다. 노면과 구동 방식에 따라 같은 입력의 결과가 달라집니다.");
                    bool swing=f.Speed>20&&wide.All(p=>Finite(p.Yaw))&&Reversals(wide,p=>p.Steering,.2)>=1&&Reversals(wide,p=>p.Yaw,.18)>=1;
                    Observe("rally-swing","rally-pendulum",swing,.2,f,"짧은 구간에 조향 방향과 차체 회전 방향이 각각 바뀌었습니다.","펜듈럼 의도나 성공을 판정하지 않습니다. 보통의 S자 코너도 같은 신호를 만들 수 있습니다.");
                }
                else
                {
                    bool lifting=f.Speed>15&&Sideways(f)&&a.Throttle-f.Throttle>.45&&f.Time-a.Time>=.15&&shortWindow.All(Sideways);
                    Observe("drift-lift","drift-balance",lifting,0,f,"옆방향 이동이 이어지는 동안 가속 입력이 짧은 시간에 크게 줄었습니다.","드리프트가 끊겼다고 판정하지 않습니다. 슬라이드 의도·노면·차량 설정은 추가 확인이 필요합니다.");
                    bool transition=wide.Length>=8&&wide.All(p=>Finite(p.Side)&&Finite(p.Forward)&&Finite(p.Yaw))&&wide.Count(Sideways)>=6&&Reversals(wide,p=>p.Yaw,.2)>=1;
                    Observe("drift-switch","drift-transition",transition,.25,f,"옆방향 이동 구간에서 차체 회전 방향이 바뀌었습니다.","8자나 연결 드리프트의 완성 여부는 경로와 목표 없이 판단하지 않습니다.");
                    Observe("drift-sustain","drift-donut",f.Speed>=15&&f.Speed<=100&&Sideways(f)&&f.Throttle>.15,1.0,f,
                        "옆방향 이동과 차체 회전, 가속 입력이 1초 이상 함께 이어졌습니다.","제어된 드리프트·원돌이인지 확인하려면 목표 원과 실제 궤적이 필요합니다.");
                }
            }
        }

        CoachPrincipleView View(Sample f,bool fresh)
        {
            if(f!=null&&active!=null&&f.Time<=activeUntil) {CoachPrincipleView v=active.Copy();v.NewFeedback=fresh;return v;}
            CoachPrincipleView waiting=new CoachPrincipleView {Status=status};
            if(f!=null)
            {
                var missing=new List<string>();
                if(!Finite(f.Yaw))missing.Add("차체 회전 속도");
                if(!Finite(f.Side)||!Finite(f.Forward))missing.Add("차체 기준 이동 속도");
                if(mode!=DrivingDiscipline.Race&&!Unit(f.Handbrake))missing.Add("핸드브레이크");
                if(mode!=DrivingDiscipline.Race&&!Unit(f.Clutch))missing.Add("클러치");
                waiting.Limits=missing.Count>0?string.Join(" · ",missing)+" 미제공: 관련 동작 평가는 보류합니다.":"입력과 운동 상태를 관찰합니다. 타이어별 힘·목표 경로·노면은 측정하지 않습니다.";
                waiting.NextDrill="원리 배우기에서 한 과제를 고른 뒤 입력 하나만 바꿔 보세요.";
            }
            return waiting;
        }

        public string ExportJson()
        {
            object data=new {schemaVersion=1,kind="principle-observations",exportedAtUtc=DateTime.UtcNow.ToString("o"),
                context=context,discipline=mode.ToString(),status=status,
                interpretation="Measured input patterns and possible principles; no causal, success, grip-limit or fastest-line verdict.",
                recentFrames=recent.Select(s=>new {time=Number(s.Time),speedKph=Number(s.Speed),brake=Number(s.Brake),throttle=Number(s.Throttle),steering=Number(s.Steering),
                    clutch=Number(s.Clutch),handbrake=Number(s.Handbrake),yawRateRadPerSec=Number(s.Yaw),lateralG=Number(s.LateralG),longitudinalG=Number(s.LongitudinalG),
                    localVelocityRightMps=Number(s.Right),localVelocitySideAbsMps=Number(s.Side),localVelocityForwardMps=Number(s.Forward),timeSource=s.CaptureClock?"capture":"simulation",lap=s.Lap,distanceM=Number(s.Distance),validityKnown=s.ValidityKnown,validLap=s.ValidLap}).ToArray(),
                observations=history.Select(e=>new {context=e.Context,discipline=e.Discipline.ToString(),detectedAt=e.DetectedAt,deliveredAt=e.DeliveredAt,
                    id=e.View.EventId,lesson=e.View.LessonId,observation=e.View.Observation,possibleMechanism=e.View.PossibleMechanism,nextDrill=e.View.NextDrill,limits=e.View.Limits}).ToArray()};
            return new JavaScriptSerializer {MaxJsonLength=4*1024*1024}.Serialize(data);
        }

        public static PrincipleLesson[] GetLessons(DrivingDiscipline discipline)
        { return Catalog.Where(l=>l.Discipline==discipline).Select(l=>l.Copy()).ToArray(); }
        public static PrincipleLesson GetLesson(string id)
        { PrincipleLesson lesson=Catalog.FirstOrDefault(l=>l.Id==id);return lesson==null?null:lesson.Copy(); }
        static PrincipleLesson Lesson(DrivingDiscipline d,string id,string title,string goal,string explanation,string drill,string lookFor,string limitations,string sourceTitle,string sourceUrl)
        {return new PrincipleLesson {Discipline=d,Id=id,Title=title,Goal=goal,Explanation=explanation,Drill=drill,LookFor=lookFor,Limitations=limitations,SourceTitle=sourceTitle,SourceUrl=sourceUrl};}
        static readonly PrincipleLesson[] Catalog = new PrincipleLesson[]
        {
            Lesson(DrivingDiscipline.Race,"race-grip","01 · 감속과 회전은 타이어의 일을 나눠 쓴다","브레이크와 조향을 왜 함께 조절하는지 이해하기",
                "타이어가 낼 수 있는 힘에는 한계가 있습니다. 강하게 감속하면서 더 많이 돌려면 한쪽 요구를 줄여야 할 수 있습니다. 페달 50%와 핸들 50%를 합쳐 100%라는 뜻은 아닙니다.",
                "같은 진입 속도와 제동 시작점을 유지하고, 이번에는 조향을 늘릴 때 브레이크를 조금 더 점진적으로 풀어 보세요.",
                "같은 목표를 향할 때 조향을 다시 보태야 하는지, 출구까지 입력이 이어지는지 비교하세요.",
                "ABS·타이어·노면에 따라 달라집니다. 제동과 조향이 겹쳤다는 사실만으로 그립 초과를 판정하지 않습니다.","Driver61 · Understanding Grip","https://driver61.com/uni/grip/"),
            Lesson(DrivingDiscipline.Race,"race-release","02 · 브레이크를 놓는 과정도 조작이다","감속에서 회전으로 넘어가는 연결을 느끼기",
                "제동 해제는 감속 요구와 앞뒤 축의 하중 분배를 함께 바꿉니다. 급격한 해제는 반응을 갑자기 바꿀 수 있지만, 오래 끌면 항상 잘 도는 것도 아닙니다.",
                "게임의 넓고 느린 코너에서 진입 속도를 고정하고, 브레이크를 0까지 놓는 시간만 조금 늘려 비교하세요.",
                "해제 직후 차가 더 돌거나 덜 도는지, 조향을 되돌려 고치는 횟수가 줄었는지 보세요.",
                "필요한 해제 시점은 코너·차량마다 다릅니다. 모든 코너에서 정점까지 제동하는 공식은 없습니다.","Driver61 · Trail Braking","https://driver61.com/uni/trail-braking/"),
            Lesson(DrivingDiscipline.Race,"race-hands","03 · 손을 느리게, 필요한 수정은 제때","반복 조향을 줄이고 차의 반응을 기다리기",
                "조향을 빨리 바꾸면 차가 요구받는 횡방향 힘도 빠르게 바뀝니다. 반복 수정은 원인일 수도, 이미 생긴 움직임에 대한 대응일 수도 있습니다. 미끄러질 때 필요한 빠른 대응까지 늦추라는 뜻은 아닙니다.",
                "같은 느린 코너에서 진입 속도를 유지하고, 최초 조향을 넣는 속도만 부드럽게 바꿔 보세요.",
                "정점을 향해 한 번에 방향을 잡는지, 불필요하게 풀었다 다시 꺾는 횟수가 줄었는지 보세요.",
                "조향 비율은 휠 회전각이 아닙니다. 시케인·요철·회피 조작에는 반복 수정이 필요할 수 있습니다.","Driver61 · Weight Transfer","https://driver61.com/uni/weight-transfer/"),
            Lesson(DrivingDiscipline.Race,"race-exit","04 · 핸들을 풀 공간을 만들고 가속한다","출구에서 조향과 가속을 연결하기",
                "코너 탈출 때 조향을 풀면 타이어에 요구하는 회전력이 줄어들 수 있습니다. 그 여유를 가속에 쓰는 과정은 차의 구동 방식과 접지에 따라 달라집니다.",
                "같은 출구에서 가속 시작 위치는 유지하고, 가속을 100%까지 올리는 속도만 완만하게 바꿔 보세요.",
                "가속하다 다시 놓는 일이 줄었는지, 같은 직선 지점에서 속도가 어떻게 달라지는지 확인하세요.",
                "조기 가속이 항상 빠르지는 않습니다. 경로·TC 개입·휠 슬립 없이 최적 페달값을 정할 수 없습니다.","Driver61 · Corner Exit","https://driver61.com/uni/corner-exit/"),
            Lesson(DrivingDiscipline.Race,"race-recovery","05 · 뒤가 흐르면 회전과 복원을 함께 본다","오버스티어 회복의 순서를 이해하기",
                "뒷부분이 옆으로 나오는 과도한 회전에는 진행하려는 방향으로 앞바퀴를 맞추는 카운터스티어가 필요할 수 있습니다. 회전이 잦아들면 조향도 되돌려야 반대쪽으로 튀는 일을 줄일 수 있습니다.",
                "게임의 넓은 연습장에서 작은 슬라이드를 만들고, 같은 가속 입력에서 카운터스티어를 되돌리는 타이밍만 비교하세요.",
                "첫 슬라이드보다 회복 뒤 반대 방향으로 튀는 반응이 생기는지 보세요.",
                "파워·리프트·제동으로 생긴 오버스티어는 대응이 다릅니다. 모든 상황에 급가속이나 급리프트를 처방하지 않습니다.","Driver61 · Oversteer","https://driver61.com/uni/oversteer/"),
            Lesson(DrivingDiscipline.Rally,"rally-load","01 · 그래벌에서는 감속과 회전 준비를 나눈다","노면과 하중 변화에 차가 어떻게 반응하는지 보기",
                "느슨한 노면에서는 타이어가 미끄러지며 내는 힘의 특성이 타막과 다릅니다. 제동·리프트는 앞뒤 하중을 바꾸므로 회전 준비에 쓰일 수 있지만, 더 밟는다고 무조건 더 잘 도는 것은 아닙니다.",
                "게임의 넓은 그래벌 코너에서 진입 속도와 조향을 고정하고, 가벼운 제동을 놓는 시점만 바꿔 보세요.",
                "해제 전후 차의 회전이 시작·멈추는 시점과 출구 방향을 비교하세요.",
                "노면 신호가 없으면 그래벌 주행이라고 단정하지 않습니다. FWD·RWD·AWD의 구동 반응을 같은 공식으로 다루지 않습니다.","Team O'Neil · Weight Transfer","https://teamoneil.com/blog/weight-transfer/"),
            Lesson(DrivingDiscipline.Rally,"rally-pendulum","02 · 펜듈럼은 반대 조향보다 하중의 타이밍","짧은 반대 방향 움직임을 본래 회전으로 연결하기",
                "펜듈럼은 먼저 반대쪽에 생긴 차체 움직임을 원하는 코너 방향으로 넘겨 회전을 돕는 기법입니다. 큰 핸들 각도만 복사하면 이동할 공간과 타이밍이 맞지 않을 수 있습니다.",
                "게임의 넓은 연습장에서 속도와 첫 조향 크기를 고정하고, 반대 조향에서 본래 방향으로 전환하는 시점만 비교하세요.",
                "조향 전환보다 차의 회전이 먼저인지 나중인지, 목표 출구로 가기 위한 수정이 줄었는지 보세요.",
                "모든 코너에 필요한 기법은 아닙니다. 기본 감속·작은 슬라이드 회복을 익힌 뒤 시도하세요.","Team O'Neil · Pendulum Turn","https://teamoneil.com/blog/how-to-do-a-pendulum-turn/"),
            Lesson(DrivingDiscipline.Rally,"rally-handbrake","03 · 핸드브레이크는 회전 시작을 돕는 도구","후륜 제동을 짧게 쓰고 다시 굴리기",
                "핸드브레이크는 보통 후륜에 제동을 걸어 회전과 감속에 영향을 줍니다. 오래 잡으면 진행에 필요한 속도도 잃을 수 있습니다. 회전 후에는 해제와 구동 연결이 필요합니다.",
                "게임의 넓은 헤어핀에서 진입 속도를 고정하고, 핸드브레이크 유지 시간만 짧게 줄여 비교하세요.",
                "목표 방향까지 돌았는지와 해제 후 출구로 움직일 속도가 남았는지를 함께 보세요.",
                "AWD 센터 구동 분리·클러치 처리 방식은 차마다 다릅니다. 차량 안내를 확인하고 일률적으로 클러치를 밟지 마세요.","Team O'Neil · Weight Transfer","https://teamoneil.com/blog/weight-transfer/"),
            Lesson(DrivingDiscipline.Rally,"rally-clutch","04 · 클러치와 구동 방식부터 확인한다","연결을 끊는 조작과 회전을 만드는 조작 구분하기",
                "클러치를 밟으면 엔진과 구동계의 연결을 끊습니다. 핸드브레이크 중 시동 꺼짐을 피하려 쓰는 경우와, 구동 토크를 갑자기 연결하는 클러치 킥은 목적이 다릅니다.",
                "게임의 선택 차량에서 자동 클러치와 핸드브레이크 구동 분리 설정을 확인한 뒤, 저속 재출발의 클러치 해제 속도만 비교하세요.",
                "차가 다시 가속을 시작하는 시점과 급격한 튀어나감을 보세요.",
                "WRC 차량과 H패턴 후륜차의 조작 절차는 같지 않습니다. 회전수·기어·구동계 정보 없이 클러치 킥으로 확정하지 않습니다.","Texas Drift Academy · Clutch Kick","https://texasdriftacademy.com/how-a-clutch-kick-works-in-drifting/"),
            Lesson(DrivingDiscipline.Drift,"drift-donut","01 · 원돌이: 원의 크기와 회전을 따로 본다","한 방향의 작은 슬라이드를 일정하게 이어 보기",
                "원돌이는 뒤가 미끄러지는 회전을 가속과 카운터스티어로 조절하며 일정한 궤적을 만드는 연습입니다. 핸들을 끝까지 고정하고 계속 전개하는 것만으로 유지되지는 않습니다.",
                "게임의 넓은 평지에 목표 원을 정하고, 같은 기어에서 작은 가속 변화 하나만 시험하세요. 원이 커지는지 작아지는지 보세요.",
                "스핀·직진으로 끝나는 시점과 그 직전 가속 변화, 원의 크기를 기록하세요.",
                "차체가 돌아간다는 사실만으로 원돌이 성공은 아닙니다. AE86도 타이어·디퍼렌셜·조향 설정에 따라 필요한 입력이 달라집니다.","Keep It Reet · Beginner Drift Guide","https://www.keepitreet.com/drift-guide"),
            Lesson(DrivingDiscipline.Drift,"drift-balance","02 · 유지: 가속과 카운터스티어의 균형","각도를 유지하면서 앞으로 나아가기",
                "후륜 구동 드리프트에서 가속은 뒷바퀴의 구동과 미끄러짐을 바꿉니다. 갑작스러운 가속 해제는 휠 슬립과 하중 상태를 함께 바꾸므로 회전·그립 회복 반응이 달라질 수 있습니다.",
                "같은 원과 기어에서 가속을 줄이는 양은 유지하고, 줄이는 시간만 조금 늘려 보세요.",
                "차가 바로 서는지, 더 도는지, 회복 뒤 반대쪽으로 튀는지 관찰하세요.",
                "가속을 더 하면 항상 각도가 커지는 공식은 아닙니다. 속도·기어·구동 토크·타이어 상태를 함께 봐야 합니다.","Texas Drift Academy · Clutch Kick","https://texasdriftacademy.com/how-a-clutch-kick-works-in-drifting/"),
            Lesson(DrivingDiscipline.Drift,"drift-transition","03 · 8자에서 가상 토우게로","왼쪽 회전을 마치며 오른쪽 회전을 준비하기",
                "전환은 기존 회전을 줄이고 반대 회전으로 넘어가는 과정입니다. 조향과 가속의 전환이 늦거나 크게 겹치면 의도한 경로를 벗어날 수 있습니다.",
                "게임의 평지 8자에서 진입 속도를 유지하고, 가속을 다시 올리는 시점만 바꿔 보세요. 익숙해지면 짧은 가상 토우게의 두 코너에 적용하세요.",
                "원과 원 사이에서 끊기는지, 반대 회전을 너무 크게 만드는지, 다음 코너의 공간이 남는지 보세요.",
                "토우게의 경사·폭·다음 코너가 목표 경로를 바꿉니다. 센서만으로 두 코너를 올바르게 연결했는지 판정하지 않습니다.","Keep It Reet · Beginner Drift Guide","https://www.keepitreet.com/drift-guide"),
            Lesson(DrivingDiscipline.Drift,"drift-handbrake","04 · 핸드브레이크는 유지의 정답이 아니다","회전·속도를 조절한 뒤 구동을 다시 연결하기",
                "핸드브레이크는 후륜 제동으로 드리프트 진입이나 속도·자세 조절을 돕습니다. 가속을 대신하는 장치는 아니며, 길게 잡으면 속도를 많이 잃을 수 있습니다.",
                "게임의 넓은 한 코너에서 진입 조건을 고정하고, 핸드브레이크를 놓는 시점만 앞당겨 비교하세요.",
                "해제 후 조향과 가속으로 슬라이드를 이어갈 속도와 공간이 남았는지 보세요.",
                "수동 후륜차는 클러치 처리와 자동 보조 설정을 확인하세요. 입력 신호가 없으면 사용 여부도 평가하지 않습니다.","Keep It Reet · Beginner Drift Guide","https://www.keepitreet.com/drift-guide"),
            Lesson(DrivingDiscipline.Drift,"drift-clutch","05 · 클러치 킥은 시작 도구, 유지 기술은 따로","짧은 구동 토크 변화의 역할 이해하기",
                "클러치 킥은 구동을 잠시 끊었다 다시 연결해 뒷바퀴의 미끄러짐을 유도하는 기법입니다. 이후에는 가속과 카운터스티어로 회전과 경로를 유지해야 합니다.",
                "게임의 넓은 연습장에서 작은 슬라이드를 잡을 수 있게 된 뒤, 같은 진입 속도에서 클러치 해제 시점 하나만 비교하세요.",
                "슬라이드가 시작되는 순간보다 그 뒤 회전을 잡고 유지하는 동작에 집중하세요.",
                "자동 클러치·변속 입력을 킥으로 오인할 수 있습니다. 엔진 회전수와 휠 속도 없이 성공이나 필요한 토크를 추정하지 않습니다.","Texas Drift Academy · Clutch Kick","https://texasdriftacademy.com/how-a-clutch-kick-works-in-drifting/")
        };
    }
}
