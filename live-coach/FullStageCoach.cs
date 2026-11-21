using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IntsLiveCoach
{
    public sealed class FullStageCoach
    {
        const string Lesson = "rally-stage";
        readonly RallySkillCoach corners = new RallySkillCoach();
        readonly RallyStageComparison repeat = new RallyStageComparison();
        readonly string sessionId = Guid.NewGuid().ToString("N");
        readonly List<Section> sections = new List<Section>();
        readonly List<Turn> notes = new List<Turn>();
        TelemetryFrame previous;
        string identity, runId;
        bool active, holdReport, distanceBased;
        double firstTime, firstDistance, lastDistance, stageClock, stageLength, highestSpeed;
        int runNumber, turnCount, switches, steeringSide, candidateSide;
        double candidateSince, lastSteerAt, switchUntil, lastCornerAt;
        object lastRun;
        string lastState, lastReviewText="";
        Section currentSection;
        Section previousSection;
        double sectionNoticeUntil;
        bool sectionShown;
        CoachPrincipleView current, lastReport;

        sealed class Section
        {
            public int Index, Turns, Switches;
            public double ObservedSeconds, MaxSpeedKph;
            public object Summary() { return new { index=Index + 1, observedSeconds=ObservedSeconds, peakKph=MaxSpeedKph, reviewedTurns=Turns, steeringReversals=Switches }; }
        }
        sealed class Turn
        {
            public double Elapsed;
            public double? AtM;
            public string Kind, Title, Observation, Principle, Next;
            public RallySkillCoach.CornerEvidence Evidence;
            public object Summary() { return new { observedSeconds=Elapsed, distanceM=AtM, kind=Kind, title=Title,
                observation=Observation, possibleMechanism=Principle, next=Next, evidence=Evidence }; }
        }

        static bool Finite(double x) { return !double.IsInfinity(x) && !double.IsNaN(x); }
        static double? Optional(double x) { return Finite(x) ? (double?)x : null; }
        static bool Unit(double x) { return x >= 0 && x <= 1; }
        static string Clock(double seconds)
        {
            int s = (int)Math.Max(0, Math.Min(seconds, 86400));
            return (s/60).ToString(CultureInfo.InvariantCulture) + ":" + (s%60).ToString("00", CultureInfo.InvariantCulture);
        }
        static bool ValidInputs(TelemetryFrame f)
        {
            return f != null && !f.InPit && !f.IsReplay && Finite(f.Time) && f.Time >= 0 &&
                f.SpeedKph >= 0 && f.SpeedKph <= 400 && Unit(f.Throttle) && Unit(f.Brake) && Math.Abs(f.Steering) <= 1.01;
        }
        static bool HasDistance(TelemetryFrame f) { return f.TrackLength >= 100 && f.TrackLength <= 100000 && Finite(f.LapDistance); }
        static string Identity(TelemetryFrame f) { return String.Join("|", f.GameVersion, f.Car, f.Track, f.TimeIsCaptureClock.ToString()); }
        static CoachPrincipleView Waiting()
        {
            return new CoachPrincipleView {LessonId=Lesson, Title="스테이지 출발 대기", Status="스테이지 대기",
                NextDrill="스테이지에서 직접 출발하면 조향·제동·재가속을 차례로 관찰합니다."};
        }
        static CoachPrincipleView Repeat(CoachPrincipleView cue)
        {
            if (cue == null) return null;
            var copy = cue.Copy(); copy.NewFeedback = false; return copy;
        }
        public CoachPrincipleView LastReport { get { return Repeat(lastReport); } }
        public bool RepeatAttemptOpen { get { return repeat.AttemptOpen; } }
        public bool RepeatWaitingForStart { get { return repeat.WaitingForStart; } }
        public bool CancelPendingRepeat() { return repeat.CancelPending(); }
        public bool ArmRepeat(string stageName,string carName)
        {
            if (!repeat.Arm(stageName,carName)) return false;
            Interrupt(); holdReport=false; previous=null; current=lastReport=null;
            return true;
        }
        public bool CanFinishManually
        {
            get { return previous != null && previous.Time-firstTime >= 1 &&
                (active || (!distanceBased && lastState == "interrupted")); }
        }

        // Reset is an explicit exercise/session change. A lost game connection uses
        // Interrupt instead, leaving a frozen summary available to the UI/export.
        public void Reset()
        {
            active = holdReport = false; previous = null; current = lastReport = null;
            lastRun = null; lastState = identity = null; lastReviewText=""; sections.Clear(); notes.Clear(); corners.Reset(); repeat.Reset();
        }
        public void Interrupt()
        {
            if (active) { Seal("interrupted", "스테이지 관측 중단"); holdReport = false; }
            corners.Reset();
        }
        public CoachPrincipleView FinishManually()
        {
            if (!CanFinishManually) return null;
            return active ? Seal("manual", "스테이지 기록 · 직접 종료") :
                Seal("manual-after-interruption", "수신 종료 구간 · 직접 마감");
        }
        void Start(TelemetryFrame frame)
        {
            active = true; holdReport = false; previous = null; current = null;
            runId = sessionId + "-" + (++runNumber);
            identity = Identity(frame); firstTime = frame.Time; distanceBased = HasDistance(frame);
            firstDistance = lastDistance = distanceBased ? frame.LapDistance : double.NaN;
            repeat.Begin(frame,distanceBased);
            stageLength = distanceBased ? frame.TrackLength : double.NaN;
            stageClock = frame.StageElapsedSeconds; highestSpeed = 0;
            turnCount = switches = steeringSide = candidateSide = 0;
            switchUntil = lastSteerAt = lastCornerAt = double.NegativeInfinity;
            sections.Clear(); currentSection=previousSection=null; notes.Clear(); corners.Reset();
        }
        bool BrokenContinuity(TelemetryFrame f)
        {
            if (previous == null) return false;
            if (Identity(f) != identity || distanceBased != HasDistance(f)) return true;
            double dt = f.Time - previous.Time;
            if (dt < 0 || dt > 1.5 || f.StageElapsedSeconds < stageClock - .1) return true;
            if (!distanceBased) return false;
            double metres = f.LapDistance - previous.LapDistance;
            double plausible = Math.Max(12, 5 + 1.8 * Math.Max(f.SpeedKph, previous.SpeedKph) / 3.6 * dt);
            return Math.Abs(f.TrackLength-stageLength) > 1 || Math.Abs(metres) > plausible;
        }
        bool CanStart(TelemetryFrame f)
        {
            if (!f.OnTrack) return false;
            if (HasDistance(f)) return f.LapDistance >= 0 && f.LapDistance < f.TrackLength;
            // The native WRC source always supplies both distance channels. ACR
            // uses an observation clock and starts its first window when moving.
            return double.IsNaN(f.LapDistance) && double.IsNaN(f.TrackLength) && f.TimeIsCaptureClock && f.SpeedKph >= 5 && f.Gear >= 0;
        }
        bool StartsAfterReport(TelemetryFrame f)
        {
            return previous == null || Identity(f) != identity || f.Time < previous.Time ||
                f.StageElapsedSeconds < stageClock - .1 ||
                (distanceBased && f.LapDistance < lastDistance - 30);
        }
        public CoachPrincipleView Update(TelemetryFrame f)
        {
            if (!ValidInputs(f)) { Interrupt(); return LastReport ?? Waiting(); }
            if (active && BrokenContinuity(f)) Interrupt();
            if (!active)
            {
                if (holdReport && !StartsAfterReport(f)) return LastReport;
                if (!CanStart(f)) return LastReport ?? Waiting();
                Start(f);
            }
            if (previous != null && f.Time == previous.Time) return Repeat(current);

            // The WRC reader marks the first packet beyond the stage distance
            // off track. Accept that packet solely to detect a continuous crossing.
            bool reachedEnd = distanceBased && previous != null && previous.LapDistance < stageLength &&
                f.LapDistance >= stageLength && f.Gear >= 0;
            if (!f.OnTrack && !reachedEnd) { Interrupt(); return LastReport ?? Waiting(); }
            double dt = previous == null ? 0 : f.Time - previous.Time;
            if (dt > .5) { corners.Reset(); steeringSide = candidateSide = 0; switchUntil = double.NegativeInfinity; }
            repeat.Observe(f);
            Account(f, dt);
            if (previous == null) previous = new TelemetryFrame {Track=f.Track,Car=f.Car,GameVersion=f.GameVersion,TimeIsCaptureClock=f.TimeIsCaptureClock};
            previous.Time = f.Time; previous.LapDistance = f.LapDistance; previous.SpeedKph = f.SpeedKph;
            if (reachedEnd) return Seal("distance-end", "스테이지 끝 거리 도달");

            var cue = corners.Update(f).Copy();
            bool cornerReview = !String.IsNullOrEmpty(cue.EventId);
            if (cornerReview)
            {
                int reviewed = turnCount + (cue.NewFeedback?1:0);
                cue.EventId = runId+"-corner-"+reviewed;
            }
            bool changedSide = ObserveSteering(f);
            if (cue.NewFeedback)
            {
                lastCornerAt=f.Time;
                turnCount++; currentSection.Turns++;
                Note(f, "corner", cue, corners.LastCorner);
            }
            else if (changedSide || f.Time < switchUntil)
            {
                cue = new CoachPrincipleView { Title="연속 코너 · 반대 방향 조향", HasObservation=true,
                    NewFeedback=changedSide, EventId=runId+"-switch-"+switches,
                    Observation="한쪽 조향에 이어 반대쪽 조향을 유지했습니다.",
                    NextDrill="첫 방향의 핸들을 풀 때 다음 출구를 보고, 두 번째 방향으로 옮기는 속도를 느껴 보세요.",
                    PossibleMechanism="방향이 바뀌면 옆으로 실린 하중도 옮겨 가므로 차의 반응과 손의 움직임 사이에 차이가 생길 수 있어요." };
                if (changedSide) Note(f, "steering-reversal", cue, null);
            }
            else if ((!cue.HasObservation || (cornerReview && f.Time-lastCornerAt>2)) &&
                previousSection != null && f.Time < sectionNoticeUntil &&
                f.SpeedKph >= 8 && Math.Abs(f.Steering)<.15 && f.Brake < .12)
            {
                cue.Title = "구간 복기 · " + (previousSection.Index+1) + "구간";
                cue.Observation = Clock(previousSection.ObservedSeconds)+" 관측 · 최고 "+
                    Math.Round(previousSection.MaxSpeedKph)+" km/h · 코너 복기 "+previousSection.Turns+"회";
                cue.NextDrill = "다음 구간은 브레이크를 풀면서 조향을 넣는 느낌에 집중하세요.";
                cue.PossibleMechanism = "브레이크를 풀면 감속에 쓰던 접지를 회전에 쓸 여유가 생깁니다.";
                cue.HasObservation = true; cue.NewFeedback = !sectionShown;
                cue.EventId = runId+"-section-"+previousSection.Index;
                sectionShown = true;
            }
            else if (!cue.HasObservation && f.SpeedKph >= 8)
            {
                // Use the currently observed inputs while travelling between
                // turns, rather than instructing a driver to repeat one corner.
                bool braking = f.Brake >= .18;
                if (Math.Abs(f.Steering) >= .22)
                {
                    cue.Title = "연속 구간 · 조향 중";
                    cue.Observation = "조향 "+Math.Round(100*Math.Abs(f.Steering))+"% · 브레이크 "+Math.Round(100*f.Brake)+"%";
                    cue.HasObservation = true;
                    cue.NextDrill = "페달을 바꿀 때 차가 도는 느낌과 출구 방향을 함께 확인하세요.";
                    cue.PossibleMechanism = "타이어는 회전과 감속·가속에 쓸 수 있는 접지를 나눠 씁니다.";
                }
                else
                {
                    cue.Title = braking ? "접근 · 감속 중" : "다음 코너 접근";
                    cue.Observation = braking ? "브레이크 " + Math.Round(100*f.Brake) + "% · 현재 " + Math.Round(f.SpeedKph) + " km/h" : "";
                    cue.NextDrill = braking ? "조향을 시작할 때 남아 있는 브레이크 압력을 느껴 보세요." : "페이스노트를 듣고 다음 코너의 출구를 보세요.";
                }
            }
            cue.LessonId = Lesson; cue.Status = Progress();
            if (!distanceBased) cue.Limits += " 시간은 수신 시각 기준입니다.";
            current = cue;
            return cue.Copy();
        }
        void Note(TelemetryFrame f, string kind, CoachPrincipleView cue, RallySkillCoach.CornerEvidence evidence)
        {
            if (notes.Count >= 4096) return;
            notes.Add(new Turn { Elapsed=f.Time-firstTime, AtM=Optional(lastDistance), Kind=kind, Title=cue.Title,
                Observation=cue.Observation, Principle=cue.PossibleMechanism, Next=cue.NextDrill, Evidence=evidence });
        }
        Turn PracticeFocus(out int count)
        {
            // Pick a repeated, actually observed timing pattern. A split time alone
            // offers no evidence for choosing a skill at a particular corner.
            var group = notes.Where(n => n.Evidence != null && n.Evidence.Skill != null)
                .GroupBy(n => n.Evidence.Skill).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Last().Elapsed).FirstOrDefault();
            count = group == null ? 0 : group.Count();
            return group == null ? null : group.Last();
        }
        static string FocusLabel(Turn focus, int count)
        {
            string location = focus.Evidence.TurnInMetres.HasValue ? Metres(focus.Evidence.TurnInMetres.Value)+" 진입" : "관측 "+Clock(focus.Elapsed)+" 출구";
            return focus.Title+" · "+location+(count>1?" · 같은 조작 순서 "+count+"회":"");
        }
        bool ObserveSteering(TelemetryFrame f)
        {
            if (f.Gear < 0 || f.SpeedKph < 15 || f.Time - lastSteerAt > 1.25) steeringSide = 0;
            if (f.Gear < 0 || f.SpeedKph < 15 || Math.Abs(f.Steering) < .22) { candidateSide=0; return false; }
            int side = Math.Sign(f.Steering);
            lastSteerAt = f.Time;
            if (side != candidateSide) { candidateSide=side; candidateSince=f.Time; }
            if (f.Time-candidateSince < .25 || side == steeringSide) return false;
            bool switched = steeringSide != 0; steeringSide = side;
            if (switched) { switches++; currentSection.Switches++; switchUntil=f.Time+.9; }
            return switched;
        }
        void Account(TelemetryFrame f, double dt)
        {
            double elapsed = f.Time-firstTime;
            lastDistance = distanceBased ? Math.Min(stageLength, f.LapDistance) : double.NaN;
            stageClock = f.StageElapsedSeconds;
            int index = distanceBased ? Math.Max(0, Math.Min(3,(int)(lastDistance/stageLength*4))) : (int)Math.Min(2880, elapsed/30);
            if (currentSection == null || currentSection.Index != index)
            {
                previousSection = currentSection != null && index > currentSection.Index ? currentSection : null;
                sectionNoticeUntil = f.Time+5; sectionShown=false;
                currentSection=sections.FirstOrDefault(s=>s.Index==index);
                if (currentSection==null) { currentSection=new Section {Index=index}; sections.Add(currentSection); }
            }
            currentSection.ObservedSeconds += dt;
            currentSection.MaxSpeedKph=Math.Max(currentSection.MaxSpeedKph,f.SpeedKph);
            highestSpeed=Math.Max(highestSpeed,f.SpeedKph);
        }
        string Progress()
        {
            string where = distanceBased ? (int)Math.Floor(lastDistance*100/stageLength) + "% · " +
                (lastDistance/1000).ToString("0.00", CultureInfo.InvariantCulture) + "/" +
                (stageLength/1000).ToString("0.00", CultureInfo.InvariantCulture)+" km"
                : "관측 "+Clock(previous.Time-firstTime);
            string comparison=repeat.Progress;
            return comparison.Length>0 ? where+" · "+comparison : where+" · 코너 복기 "+turnCount+" · 조향 전환 "+switches;
        }
        CoachPrincipleView Seal(string reason, string title)
        {
            var lastTurn = notes.LastOrDefault(n => n.Kind == "corner") ?? notes.LastOrDefault();
            int count;
            var focus=PracticeFocus(out count);
            string comparison=repeat.Finish(reason);
            var report = new CoachPrincipleView { LessonId=Lesson, Status=Progress(), Title=title, HasObservation=true,
                NewFeedback=true, EventId=runId+"-"+reason,
                Observation="관측 "+Clock(previous.Time-firstTime)+" · 최고 "+Math.Round(highestSpeed)+" km/h · 복기 "+turnCount+"회"+
                    (distanceBased && firstDistance>5 ? " · 중간부터 관측" : ""),
                NextDrill=lastTurn == null ? "다음 주행은 진입 속도를 일정하게 맞춘 뒤 제동 해제를 관찰해 보세요." : lastTurn.Next,
                PossibleMechanism=lastTurn==null?"":lastTurn.Principle,
                Limits=distanceBased ? "게임의 스테이지 거리 기준입니다. 주행 유효성·공식 완주 결과는 게임에서 확인하세요." :
                    "스테이지 거리·공식 완주 신호가 없어 수신 시각과 직접 종료 기록을 사용합니다." };
            if(comparison.Length>0) report.Observation=comparison+"\n"+report.Observation;
            string repeatDrill=repeat.NextDrill();
            if (focus!=null)
            {
                report.Observation=FocusLabel(focus,count)+"\n"+report.Observation;
                report.PossibleMechanism=focus.Principle; report.NextDrill=focus.Next;
            }
            else if (repeatDrill.Length>0) report.NextDrill=repeatDrill;
            lastRun=RunSummary(reason); lastState=reason; lastReport=report.Copy(); current=report;
            lastReviewText=DescribeRun(title);
            holdReport=true; active=false;
            return report;
        }
        object RunSummary(string state)
        {
            int count;
            var focus=PracticeFocus(out count);
            return new {id=runId, state=state, track=previous.Track, car=previous.Car, gameVersion=previous.GameVersion,
                clock=previous.TimeIsCaptureClock?"capture":"simulation", observedSeconds=previous.Time-firstTime,
                startDistanceM=Optional(firstDistance), endDistanceM=Optional(lastDistance), lengthM=Optional(stageLength),
                lastNativeStageSeconds=Optional(stageClock), observedNearStart=distanceBased?(bool?)(firstDistance<=5):null,
                peakSpeedKph=highestSpeed, reviewedTurns=turnCount, steeringReversals=switches,
                sectionBasis=distanceBased?"quarters-of-stage-distance":"30s-observation-windows",
                sections=sections.Select(s=>s.Summary()).ToArray(), notes=notes.Select(n=>n.Summary()).ToArray(),
                practiceFocus=focus==null?null:focus.Summary(), focusOccurrenceCount=count,
                repeatComparison=repeat.Snapshot() };
        }
        public object Snapshot()
        {
            return new { schemaVersion=1, current=active?RunSummary("observing"):null, lastRun=lastRun };
        }
        public string ReviewText()
        {
            return active ? DescribeRun("스테이지 주행 중") : lastReviewText;
        }
        string DescribeRun(string title)
        {
            var s=new StringBuilder();
            s.AppendLine(title+" · "+previous.Track);
            s.AppendLine(Progress()+" · 관측 시간 "+Clock(previous.Time-firstTime));
            if (distanceBased) s.AppendLine("관측 범위 "+Metres(firstDistance)+" → "+Metres(lastDistance)+
                (firstDistance>5 ? " · 도중 연결" : " · 출발 부근부터 관측"));
            else s.AppendLine("첫 주행 신호부터 PC 수신 시각으로 기록했습니다.");
            int count;
            var focus=PracticeFocus(out count);
            if (focus!=null)
            {
                s.AppendLine().AppendLine("다음 주행에서 연습할 코너");
                s.AppendLine(FocusLabel(focus,count));
                s.AppendLine(focus.Principle); s.AppendLine(focus.Next);
            }
            string comparison=repeat.Review();
            if (comparison.Length>0) { s.AppendLine(); s.AppendLine(comparison); }
            s.AppendLine();
            foreach (var item in sections.OrderBy(section=>section.Index))
            {
                string label=distanceBased ? item.Index*25+"–"+(item.Index+1)*25+"%" :
                    Clock(30*item.Index)+"–"+Clock(30*(item.Index+1));
                s.AppendLine(label+" · "+Clock(item.ObservedSeconds)+" 관측 · 최고 "+Math.Round(item.MaxSpeedKph)+
                    " km/h · 코너 복기 "+item.Turns+" · 조향 전환 "+item.Switches);
            }
            foreach (var note in notes)
            {
                s.AppendLine();
                string where=Clock(note.Elapsed)+(note.AtM.HasValue ? " · "+Metres(note.AtM.Value) : "");
                s.AppendLine(where+" · "+note.Title);
                s.AppendLine(note.Observation);
                s.AppendLine(note.Principle);
                s.AppendLine(note.Next);
            }
            return s.ToString().TrimEnd();
        }
        static string Metres(double m) { return Math.Round(m).ToString(CultureInfo.InvariantCulture)+" m"; }
    }
}
