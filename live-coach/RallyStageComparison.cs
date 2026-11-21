using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IntsLiveCoach
{
    // Explicit stage/direction and vehicle labels are required for EACH run. The
    // standard EA WRC packet has no such IDs. ACR can supply labels, but has no
    // stage clock or distance. Use its observed input proportions separately.
    internal sealed class RallyStageComparison
    {
        sealed class Gate
        {
            public double Clock, X, Y, Z;
            public double DistanceSquared(Gate other)
            {
                double x=X-other.X, y=Y-other.Y, z=Z-other.Z;
                return x*x+y*y+z*z;
            }
            public bool CloseTo(Gate other) { return DistanceSquared(other)<=25*25; }
        }
        sealed class Run
        {
            public readonly Gate[] Gates = new Gate[5];
            public int LastGate;
            public double ObservedSeconds, BrakeSeconds, OverlapSeconds;
            public double OverlapPercent { get { return 100*OverlapSeconds/ObservedSeconds; } }
        }

        readonly Dictionary<Tuple<string,string,string,string,string,double>,Run> references =
            new Dictionary<Tuple<string,string,string,string,string,double>,Run>();
        Tuple<string,string,string,string,string,double> key;
        string pendingStage, pendingCar, stage, car, unavailable, outcome;
        bool timed, previousBraking, previousOverlap, finished;
        Run run, baseline;
        double length, lastTime, lastClock, lastDistance, lastX, lastY, lastZ;
        static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        internal bool AttemptOpen
        { get { return pendingStage!=null || run!=null && !finished && String.IsNullOrEmpty(unavailable); } }
        internal bool WaitingForStart { get { return pendingStage!=null; } }
        internal bool CancelPending()
        {
            if (pendingStage==null) return false;
            pendingStage=pendingCar=null;
            return true;
        }

        internal bool Arm(string stageName, string carName)
        {
            if (String.IsNullOrWhiteSpace(stageName) || String.IsNullOrWhiteSpace(carName) ||
                stageName.Trim().Length>120 || carName.Trim().Length>120) return false;
            pendingStage=stageName.Trim(); pendingCar=carName.Trim();
            return true;
        }
        internal void Reset()
        {
            references.Clear(); pendingStage=pendingCar=null; run=baseline=null; key=null;
        }
        internal void Begin(TelemetryFrame f, bool byDistance)
        {
            run=baseline=null; key=null; unavailable=outcome=""; finished=false;
            if (pendingStage==null) return;
            // A driver can arm the next pass while parked near the end of the
            // previous one. Never consume this confirmation on a late join.
            if (byDistance && (f.LapDistance>5 || !Finite(f.StageElapsedSeconds) || f.StageElapsedSeconds>2.5)) return;
            stage=pendingStage; car=pendingCar; pendingStage=pendingCar=null;
            timed=byDistance; length=timed?f.TrackLength:-1;
            key=Tuple.Create(stage,car,f.GameVersion??"",f.Track??"",f.Car??"",length);
            references.TryGetValue(key,out baseline);
            run=new Run(); lastTime=Double.NaN;
            if (timed)
            {
                if (f.TimeIsCaptureClock || !Position(f.X,f.Y,f.Z)) { unavailable="출발 위치나 게임 스테이지 시계 미확인"; return; }
                // The native timer starts at zero; the first position is the
                // observed near-start position, not a reconstructed start line.
                run.Gates[0]=new Gate { Clock=0,X=f.X,Y=f.Y,Z=f.Z };
                if (baseline!=null && !run.Gates[0].CloseTo(baseline.Gates[0])) unavailable="기준과 출발 위치가 다름";
            }
        }
        internal void Observe(TelemetryFrame f)
        {
            if (run==null || finished || !String.IsNullOrEmpty(unavailable)) return;
            double dt=f.Time-lastTime;
            if (Finite(lastTime) && dt>0)
            {
                if (timed)
                {
                    if (f.TrackLength!=length || !Finite(f.StageElapsedSeconds) || f.StageElapsedSeconds<lastClock ||
                        f.StageElapsedSeconds-lastClock>dt+.25)
                    { unavailable="코스 길이 또는 스테이지 시계가 바뀜"; return; }
                    CrossGates(f);
                }
                else if (dt<=.5)
                {
                    // Hold the preceding measured inputs only over a short
                    // continuous observation; longer gaps are excluded.
                    run.ObservedSeconds+=dt;
                    if (previousBraking) run.BrakeSeconds+=dt;
                    if (previousOverlap) run.OverlapSeconds+=dt;
                }
            }
            previousBraking=f.Brake>=.15; previousOverlap=previousBraking && Math.Abs(f.Steering)>=.2;
            lastTime=f.Time; lastDistance=f.LapDistance; lastClock=f.StageElapsedSeconds;
            lastX=f.X; lastY=f.Y; lastZ=f.Z;
        }
        void CrossGates(TelemetryFrame f)
        {
            while (run.LastGate<4)
            {
                int index=run.LastGate+1;
                double target=length*index/4;
                if (lastDistance>=target || f.LapDistance<target) break;
                if (!Position(lastX,lastY,lastZ) || !Position(f.X,f.Y,f.Z))
                { unavailable="경계 통과 위치 미수신"; return; }
                double t=(target-lastDistance)/(f.LapDistance-lastDistance);
                var gate=new Gate { Clock=lastClock+t*(f.StageElapsedSeconds-lastClock),
                    X=lastX+t*(f.X-lastX),Y=lastY+t*(f.Y-lastY),Z=lastZ+t*(f.Z-lastZ) };
                if (gate.Clock<=run.Gates[index-1].Clock)
                { unavailable="구간 경계의 이동 또는 시간 미확인"; return; }
                if (baseline!=null && !gate.CloseTo(baseline.Gates[index]))
                { unavailable="같은 거리의 실제 위치가 다름 · 스테이지 방향 확인"; return; }
                run.Gates[index]=gate; run.LastGate=index;
                if(index==4 && !run.Gates.Any(g=>g.DistanceSquared(run.Gates[0])>4))
                    unavailable="코스 거리마다 실제 이동 위치를 확인할 수 없음";
            }
        }
        static bool Position(double x,double y,double z) { return Finite(x) && Finite(y) && Finite(z); }
        static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        static string Number(double value) { return value.ToString("0.0",Culture); }
        static string TimeDelta(double delta)
        {
            return Math.Abs(delta)<.05 ? "같은 시간" : Number(Math.Abs(delta))+"초 "+(delta<0?"단축":"증가");
        }
        static double Split(Run r,int i) { return r.Gates[i].Clock-r.Gates[i-1].Clock; }
        string SplitLabel(int i) { return i+"구간 "+TimeDelta(Split(run,i)-Split(baseline,i)); }
        string OverlapDelta()
        {
            double delta=run.OverlapPercent-baseline.OverlapPercent;
            return Math.Abs(delta)<.05 ? "기준과 같은 비중" :
                "기준보다 "+Number(Math.Abs(delta))+"%p "+(delta>0?"증가":"감소");
        }
        internal string Progress
        {
            get
            {
                if (run==null) return pendingStage==null?"":"반복 비교 · 다음 출발 대기";
                if (!String.IsNullOrEmpty(unavailable)) return "비교 보류 · "+unavailable;
                if (finished) return baseline==null?"첫 기준 저장":"반복 비교 완료";
                if (baseline==null) return "내 첫 기준 기록 중";
                if (timed) return run.LastGate==0?"내 기준 · 첫 구간 주행":SplitLabel(run.LastGate);
                return run.ObservedSeconds<1 ? "반복 관측 시작" : "제동·조향 겹침 "+Number(run.OverlapPercent)+"%";
            }
        }
        internal string Finish(string reason)
        {
            if (run==null) return "";
            finished=true;
            bool reached=timed ? reason=="distance-end" && run.LastGate==4 :
                reason=="manual" && run.ObservedSeconds>=1;
            if (!reached && String.IsNullOrEmpty(unavailable))
                unavailable=timed?"출발부터 끝 거리까지 연속 관측 필요":"주행 신호 수신 중 직접 종료 필요";
            if (!String.IsNullOrEmpty(unavailable)) outcome="비교 보류 · "+unavailable;
            else if (baseline==null)
            {
                references[key]=run;
                outcome=timed?"내 기준 저장 · 거리 4구간":"내 기준 저장 · 관측한 제동·조향 비중";
            }
            else if (timed) outcome="내 기준 대비 전체 "+TimeDelta(run.Gates[4].Clock-baseline.Gates[4].Clock);
            else outcome="제동·조향 겹침 "+Number(run.OverlapPercent)+"% · "+OverlapDelta();
            return outcome;
        }
        internal string NextDrill()
        {
            if (!finished || run==null || !String.IsNullOrEmpty(unavailable)) return "";
            if (baseline==null)
                return "같은 방향·차량을 입력하고 다음 시도를 시작하세요. 진입 제동이나 가속 시점 하나만 바꿔 보세요.";
            if (!timed)
                return "다음에는 같은 시작·종료 표지를 쓰고, 코너에 들어가며 브레이크를 푸는 속도만 바꿔 보세요.";
            var boundaries=Enumerable.Range(1,4);
            int focus=boundaries.OrderByDescending(i=>Split(run,i)-Split(baseline,i)).First();
            bool slower=Split(run,focus)-Split(baseline,focus)>=.05;
            if (!slower) focus=boundaries.OrderBy(i=>Split(run,i)-Split(baseline,i)).First();
            return (focus-1)*25+"–"+focus*25+"% 구간 · "+(slower?
                "다음 시도에는 같은 표지에서 감속한 뒤 제동을 푸는 시점 하나만 바꿔 보세요.":
                "다음 시도에도 같은 제동 표지와 출구 가속으로 시간을 재현해 보세요.");
        }
        internal string Review()
        {
            if(run==null) return "";
            var s=new StringBuilder();
            s.AppendLine("반복 주행 · "+stage+" / "+car);
            s.AppendLine(finished?outcome:Progress);
            if (!String.IsNullOrEmpty(unavailable)) return s.ToString().TrimEnd();
            if (timed)
            {
                for (int i=1; i<=run.LastGate; ++i)
                    s.AppendLine(((i-1)*25)+"–"+(i*25)+"% · "+Number(Split(run,i))+"초"+
                        (baseline==null?"":" · 기준 "+Number(Split(baseline,i))+"초 · "+TimeDelta(Split(run,i)-Split(baseline,i))));
                s.Append("게임의 스테이지 시계로 거리 4등분 지점의 통과를 보간했습니다. 코스·방향·차량은 운전자가 매 시도 지정하며 노면과 셋업에 따라서도 시간이 달라집니다.");
            }
            else if (run.ObservedSeconds>=1)
            {
                s.AppendLine("관측한 입력 중 제동 "+Number(100*run.BrakeSeconds/run.ObservedSeconds)+"% · 제동과 조향 겹침 "+Number(run.OverlapPercent)+"%"+
                    (baseline==null?"":" · 기준 "+Number(baseline.OverlapPercent)+"%"));
                s.Append("브레이크 15% 이상과 조향 20% 이상이 함께 관측된 시간의 비율입니다. 두 번의 관측 범위와 노면에 따라 달라집니다.");
            }
            string next=NextDrill();
            if (next.Length>0) s.AppendLine().Append("다음 시도 · ").Append(next);
            return s.ToString().TrimEnd();
        }
        internal object Snapshot()
        {
            if (run==null) return null;
            bool compare=baseline!=null && String.IsNullOrEmpty(unavailable);
            var splits=Enumerable.Range(1,run.LastGate).Select(i=>new {
                endPercent=i*25,gameSeconds=Split(run,i),baselineSeconds=compare?(double?)Split(baseline,i):null,
                differenceSeconds=compare?(double?)(Split(run,i)-Split(baseline,i)):null }).ToArray();
            return new { stage=stage,car=car,identitySource="driver-confirmed-per-attempt",basis=timed?"native-stage-clock-distance-quarters":"observed-input-time-share",
                hasPriorReference=baseline!=null,unavailable=unavailable,outcome=outcome,splits=splits,
                observedInputSeconds=timed?(double?)null:run.ObservedSeconds,
                brakeSteerPercent=!timed && run.ObservedSeconds>=1?(double?)run.OverlapPercent:null,
                baselineBrakeSteerPercent=!timed && compare?(double?)baseline.OverlapPercent:null };
        }
    }
}
