using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IntsLiveCoach
{
    // Drivers mark both ends of a route. AC's recorded X/Z path is used only to
    // find comparable attempts, including point-to-point layouts without lap timing.
    public sealed class TougeRunCoach
    {
        const string Lesson = "drift-touge";
        readonly ContinuousDriftCoach chains = new ContinuousDriftCoach();
        readonly List<Point> path = new List<Point>();
        readonly List<Run> history = new List<Run>();
        Run run, prior;
        CoachPrincipleView report;
        int number;
        double firstTime, lastTime, lastX, lastZ, positionChangedAt, previousSpeed, stride;
        bool havePath, endedSignal;
        string comparison = "";

        struct Point
        {
            public double X, Z, Distance, Slide;
        }
        sealed class Run
        {
            public int Number, Links, Longest;
            public string Car, Track, Version, Clock, State;
            public double Elapsed, Slide, Distance;
            public bool HasDistance;
            public CoachPrincipleView Diagnosis;
            public double DiagnosisAt;
            public Point[] Marks;
            public double Percent { get { return Elapsed > 0 ? 100 * Slide / Elapsed : 0; } }
            public object Export()
            {
                return new { attempt=Number, car=Car, track=Track, gameVersion=Version, clock=Clock, state=State,
                    observedSeconds=Elapsed, slideSeconds=Slide, transitions=Links, longestChainTransitions=Longest,
                    pathMetres=HasDistance ? (double?)Distance : null, diagnosis=Diagnosis,
                    diagnosisAtSeconds=Diagnosis==null?(double?)null:DiagnosisAt };
            }
        }

        public bool IsRecording { get; private set; }
        public bool CanFinishManually { get { return IsRecording; } }
        public CoachPrincipleView LastReport
        {
            get { if (report == null) return null; var copy=report.Copy(); copy.NewFeedback=false; return copy; }
        }

        public void Reset()
        {
            IsRecording=endedSignal=false; run=prior=null; path.Clear(); history.Clear(); report=null; comparison="";
            chains.Reset(); number=0;
        }
        public void Start()
        {
            if (IsRecording) return;
            IsRecording=true; run=prior=null; report=null; comparison=""; path.Clear(); chains.Reset();
            stride=5; havePath=endedSignal=false;
        }
        public void Interrupt()
        {
            if (!IsRecording || run==null || endedSignal) return;
            endedSignal=true; run.State="signal-ended";
            report=new CoachPrincipleView {LessonId=Lesson, Status="마지막 수신 지점에서 기록 정지", Title="도착 표지에서 주행을 마쳤나요?",
                Observation=Stats(run), NextDrill="관측한 구간은 ‘구간 종료’로 마감하세요. 새 주행은 시작을 다시 누르세요.",
                Limits="마지막 신호 이후 이동은 기록에 포함하지 않습니다.", HasObservation=true, EventId="touge-signal-"+number};
        }
        public CoachPrincipleView FinishManually()
        {
            if (!IsRecording) return null;
            if (run!=null && run.Elapsed>=1) return Seal(true,endedSignal?"마지막 수신 지점 직접 종료":"직접 종료");
            return Seal(false,"관측 시간이 짧아 기록 대기");
        }

        static string N(double value) { return value.ToString("0.0",CultureInfo.InvariantCulture); }
        static string Change(double value) { return (value>0?"+":"")+N(value); }
        static bool Finite(double n) { return !Double.IsNaN(n) && !Double.IsInfinity(n); }
        static double Separation(double x,double z) { return Math.Sqrt(x*x+z*z); }
        static string RunClock(TelemetryFrame f) { return f.TimeIsCaptureClock?"capture":"simulation"; }
        static bool SameContext(Run a, Run b)
        { return a.Car==b.Car && a.Track==b.Track && a.Version==b.Version && a.Clock==b.Clock; }
        static string Stats(Run r)
        { return "연결 "+r.Links+"회 · 최장 연속 전환 "+r.Longest+"회 · 옆미끄러짐 "+N(r.Slide)+"초 ("+N(r.Percent)+"%)"; }

        public CoachPrincipleView Update(TelemetryFrame f)
        {
            if (!IsRecording) return LastReport ?? new CoachPrincipleView { LessonId=Lesson, Status="구간 선택",
                Title="출발과 도착 표지를 정하세요", NextDrill="AC에서 같은 방향으로 반복할 구간의 출발 지점에 차를 두고 기록을 시작하세요." };
            if (endedSignal) return LastReport;
            if (f==null && run!=null) { Interrupt(); return LastReport; }
            double angle;
            bool valid=f!=null && f.OnTrack && !f.InPit && !f.IsReplay &&
                !String.IsNullOrWhiteSpace(f.Car) && !String.IsNullOrWhiteSpace(f.Track) &&
                !String.IsNullOrWhiteSpace(f.GameVersion) && ContinuousDriftCoach.ReadAngle(f,out angle);
            if (!valid)
            {
                if (run!=null) return Seal(false,"직접 주행 신호 중단");
                return new CoachPrincipleView {LessonId=Lesson, Status="출발 신호 대기", Title="AC 직접 주행 대기",
                    NextDrill="피트를 나와 정한 구간을 달리세요. 차량 조작 신호가 들어오면 기록합니다."};
            }
            bool first=run==null;
            if (first)
            {
                if (f.SpeedKph<5) return new CoachPrincipleView {LessonId=Lesson, Status="출발 표지에서 대기",
                    Title="준비되면 출발하세요",NextDrill="속도가 5 km/h에 이르면 이 위치에서 구간 기록을 시작합니다."};
                run=new Run {Number=++number,Car=f.Car,Track=f.Track,Version=f.GameVersion,Clock=RunClock(f),State="recording"};
                firstTime=lastTime=positionChangedAt=f.Time; lastX=f.X; lastZ=f.Z;
                havePath=Finite(f.X) && Finite(f.Z);
            }
            else
            {
                if (f.Car!=run.Car || f.Track!=run.Track || f.GameVersion!=run.Version || RunClock(f)!=run.Clock)
                    return Seal(false,"차량·코스 또는 기록 시계 변경");
                if (f.Time<lastTime || f.Time-lastTime>.5) return Seal(false,"주행 시계 불연속");
            }
            if (havePath && !first && f.Time>lastTime)
            {
                if (!Finite(f.X) || !Finite(f.Z)) havePath=false;
                else
                {
                    double step=Separation(f.X-lastX,f.Z-lastZ);
                    if (step>0)
                    {
                        double dt=f.Time-positionChangedAt;
                        if (step>Math.Max(12,5+Math.Max(previousSpeed,f.SpeedKph)/3.6*Math.Min(.6,dt)*2))
                            return Seal(false,"차량 위치가 불연속으로 이동");
                        run.Distance+=step; positionChangedAt=f.Time;
                    }
                    else if (f.SpeedKph>20 && f.Time-positionChangedAt>1) havePath=false;
                    lastX=f.X; lastZ=f.Z;
                }
            }
            var cue=chains.Update(f);
            run.Elapsed=f.Time-firstTime; run.Links=chains.TotalTransitions;
            run.Slide=chains.TotalSlideSeconds; run.Longest=chains.LongestTransitions; run.HasDistance=havePath;
            var finding=chains.LatestDiagnosis;
            if (finding!=null && (run.Diagnosis==null || run.Diagnosis.EventId!=finding.EventId))
            { run.Diagnosis=finding.Copy(); run.DiagnosisAt=run.Elapsed; }
            if (havePath && (path.Count==0 || run.Distance-path[path.Count-1].Distance>=stride))
            {
                path.Add(CurrentPoint());
                if (path.Count>=2048)
                {
                    // Preserve the first point and cumulative distance on long mod roads.
                    for (int i=1;i<1024;i++) path[i]=path[2*i];
                    path.RemoveRange(1024,1024); stride*=2;
                }
            }
            previousSpeed=f.SpeedKph; lastTime=f.Time;
            if (chains.BetweenAttempts)
            {
                cue.Title="다음 굽이 준비 · 구간 기록 중";
                cue.NextDrill="직선에서는 차를 펴고 다음 굽이를 보세요. 원하는 구간의 끝에서 기록을 마감하세요.";
            }
            cue.LessonId=Lesson; cue.EventId="touge-"+run.Number+"-"+cue.EventId;
            cue.Status="구간 "+N(run.Elapsed)+"초 · 전체 연결 "+run.Links+"회 · 옆미끄러짐 "+N(run.Slide)+"초";
            return cue;
        }
        Point CurrentPoint()
        { return new Point {Distance=run.Distance,X=lastX,Z=lastZ,Slide=run.Slide}; }
        Point[] RouteMarks()
        {
            if (!havePath || run.Distance<40 || path.Count<2) return null;
            var end=CurrentPoint();
            if (end.Distance==path[path.Count-1].Distance) path[path.Count-1]=end;
            else path.Add(end);
            var marks=new Point[9]; int index=1;
            for (int i=0;i<marks.Length;i++)
            {
                double distance=run.Distance*i/8;
                while (index<path.Count-1 && path[index].Distance<distance) index++;
                var a=path[index-1]; var b=path[index]; double t=(distance-a.Distance)/(b.Distance-a.Distance);
                marks[i]=new Point {Distance=distance,X=a.X+(b.X-a.X)*t,Z=a.Z+(b.Z-a.Z)*t,
                    Slide=a.Slide+(b.Slide-a.Slide)*t};
            }
            marks[0]=path[0]; marks[8]=end;
            return marks;
        }
        static bool Matches(Run a,Run b)
        {
            if (!SameContext(a,b) || a.Marks==null || b.Marks==null) return false;
            double length=Math.Min(a.Distance,b.Distance);
            if (Math.Abs(a.Distance-b.Distance)>length*.15) return false;
            double ends=Math.Max(5, Math.Min(25,length*.035)), interior=Math.Max(8,Math.Min(40,length*.08));
            bool leavesStart=false;
            for (int i=0;i<a.Marks.Length;i++)
            {
                var p=a.Marks[i]; var q=b.Marks[i];
                if (Separation(p.X-q.X,p.Z-q.Z)>((i==0 || i==8)?ends:interior)) return false;
                if (Separation(p.X-a.Marks[0].X,p.Z-a.Marks[0].Z)>20) leavesStart=true;
            }
            return leavesStart;
        }
        static string CompareRuns(Run a,Run b)
        {
            string label="같은 경로의 직전 "+b.Number+"회차 대비";
            string line=label+" · 연결 "+b.Links+" → "+a.Links+"회 · 옆미끄러짐 비율 "+Change(a.Percent-b.Percent)+"%p";
            int section=-1; double biggest=.5, difference=0;
            for (int i=0;i<4;i++)
            {
                var start=a.Marks[2*i]; var end=a.Marks[2*i+2]; var oldStart=b.Marks[2*i]; var oldEnd=b.Marks[2*i+2];
                double delta=(end.Slide-start.Slide)-(oldEnd.Slide-oldStart.Slide);
                if (Math.Abs(delta)>biggest) { biggest=Math.Abs(delta); section=i; difference=delta; }
            }
            if (section>=0) line+="\n주행거리 "+(section*25)+"–"+((section+1)*25)+"% · 옆미끄러진 시간 약 "+Change(difference)+"초";
            return line;
        }
        CoachPrincipleView Seal(bool complete,string reason)
        {
            IsRecording=false;
            if (run!=null) run.State=complete?(endedSignal?"manual-after-signal":"finished"):"interrupted";
            if (complete)
            {
                run.Marks=RouteMarks();
                for (int i=history.Count-1;i>=0;i--) if (Matches(run,history[i])) {prior=history[i]; break;}
                comparison=prior!=null?CompareRuns(run,prior):run.Marks!=null?"비교할 같은 경로의 완주 기록 대기":"경로 비교에 필요한 연속 위치·주행거리 부족";
                if (run.Marks!=null) { history.Add(run); if (history.Count>16) history.RemoveAt(0); }
            }
            report=new CoachPrincipleView {LessonId=Lesson, Status=complete?"구간 기록 완료":"구간 기록 중단",
                Title=complete?(endedSignal?"마지막 수신 지점에서 구간 마감":"직접 선택한 구간 기록 완료"):"구간 관측 종료", Observation=run==null?reason:Stats(run),
                NextDrill=complete?"같은 두 표지에서 한 번 더 달려 연결 횟수와 옆미끄러짐 비율을 비교해 보세요.":"같은 출발 표지에서 기록을 다시 시작하세요.",
                Limits=complete?"출발·도착 표지는 직접 정합니다. 구간 일치는 같은 차량·코스와 실측 경로의 근사 비교입니다.":reason,
                HasObservation=run!=null, NewFeedback=true, EventId="touge-end-"+number};
            if (complete) report.Status=comparison.Split('\n')[0];
            if (complete && run.Diagnosis!=null)
            {
                report.Observation=N(run.DiagnosisAt)+"초 지점 · "+run.Diagnosis.Observation;
                report.NextDrill=run.Diagnosis.NextDrill; report.PossibleMechanism=run.Diagnosis.PossibleMechanism;
            }
            return report.Copy();
        }
        public string ReviewText()
        {
            if (run==null) return report==null?"":report.Status+" · "+report.Observation;
            var text=new StringBuilder();
            text.AppendLine(run.Track+" / "+run.Car+" · "+run.Number+"회차");
            text.AppendLine(Stats(run));
            text.Append(run.Clock=="capture"?"수신 시계 ":"게임 시계 ").Append(N(run.Elapsed)).Append("초");
            if (run.HasDistance) text.Append(" · 관측한 평면 주행거리 ").Append(N(run.Distance)).Append("m");
            if (comparison.Length>0) text.AppendLine().Append(comparison);
            if (run.Diagnosis!=null)
                text.AppendLine().Append(N(run.DiagnosisAt)).Append("초 지점 · ").Append(DriftAttemptEvidence.Review(run.Diagnosis));
            if (report!=null)
            {
                text.AppendLine().Append(report.Limits);
                if (run.Diagnosis==null || report.NextDrill!=run.Diagnosis.NextDrill) text.AppendLine().Append(report.NextDrill);
            }
            return text.ToString();
        }
        public object Snapshot()
        {
            return new { recording=IsRecording, run=run==null?null:run.Export(), previous=prior==null?null:prior.Export(), comparison=comparison };
        }
    }
}
