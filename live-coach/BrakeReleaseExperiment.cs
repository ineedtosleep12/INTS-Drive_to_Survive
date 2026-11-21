using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    public sealed class BrakeReleasePass
    {
        public int AttemptNumber { get; internal set; }
        public int Lap { get; internal set; }
        public double EntrySpeedKph { get; internal set; }
        public double ExitSpeedKph { get; internal set; }
        public double SegmentSeconds { get; internal set; }
        public double? BrakeStartM { get; internal set; }
        public double? PeakBrakePct { get; internal set; }
        public double? ReleaseSeconds { get; internal set; }
        public double? ReleaseRatePctPerSecond { get; internal set; }
        public double? FuelLitres { get; internal set; }
        public double? FirstSteerM { get; internal set; }
        public double? ThrottleReturnM { get; internal set; }
        public string ReleaseNote { get; internal set; }
        public double TimeResolutionSeconds { get; internal set; }
        public double ReleaseResolutionSeconds { get; internal set; }
        public double BrakeStartResolutionM { get; internal set; }
    }

    // Runs under the host's telemetry lock. The host checks the selected layout's native
    // world-position anchors and only forwards LMU frames from that source.
    public sealed class BrakeReleaseExperiment
    {
        const double Onset=.15, High=.60, Low=.10, Dwell=.10, MaxGap=.5;
        const string Waiting="선택한 구간의 진입 지점 대기";
        readonly List<BrakeReleasePass> history = new List<BrakeReleasePass>();
        readonly List<Point> portion = new List<Point>();
        readonly CultureInfo culture = CultureInfo.InvariantCulture;
        string track, car, version;
        double length, lastLiveTime, lastPositionChangeTime;
        int lapNumber=-1;
        bool closed;
        Point last;
        public bool Armed { get; private set; }
        public string CornerName { get; private set; }
        public double EntryM { get; private set; }
        public double ExitM { get; private set; }
        public string Status { get; private set; }
        public int Count { get; private set; }
        public BrakeReleasePass Latest { get { return history.Count==0?null:history[history.Count-1]; } }
        public BrakeReleasePass Previous { get { return history.Count<2?null:history[history.Count-2]; } }
        public BrakeReleasePass[] Recent { get { return history.ToArray(); } }
        public string ResultText { get; private set; }
        public string ComparisonNote { get; private set; }

        sealed class Point
        {
            internal double Time, Distance, Speed, Brake, Age, Fuel, Throttle, Steer;
            internal static Point Between(Point a,Point b,double t)
            {
                if(t<=0) return a;
                if(t>=1) return b;
                return new Point {Time=a.Time+t*(b.Time-a.Time), Distance=a.Distance+t*(b.Distance-a.Distance),
                    Speed=a.Speed+t*(b.Speed-a.Speed), Brake=a.Brake+t*(b.Brake-a.Brake),
                    Throttle=a.Throttle+t*(b.Throttle-a.Throttle), Steer=a.Steer+t*(b.Steer-a.Steer),
                    Age=Math.Max(a.Age,b.Age), Fuel=a.Fuel+t*(b.Fuel-a.Fuel)};
            }
        }
        static bool Finite(double n) { return !Double.IsNaN(n) && !Double.IsInfinity(n); }
        static bool Unit(double n) { return n>=0 && n<=1; }
        static bool ValidSpeed(double n) { return n>=0 && n<=550; }
        static bool IsKnown(string s) { return !ReferenceLap.Unknown(s); }
        public void Reset()
        {
            history.Clear(); portion.Clear(); Count=0; Armed=false; last=null; closed=false;
            track=car=version=null; length=0; lapNumber=-1; lastLiveTime=Double.NaN;
            EntryM=ExitM=0; CornerName=""; Status="연습할 코너를 선택하세요."; ResultText=ComparisonNote="";
        }
        public bool Arm(string cornerName,double entryM,double exitM)
        {
            if(String.IsNullOrWhiteSpace(cornerName)||!Finite(entryM)||!Finite(exitM)||entryM<0||
                exitM-entryM<10||exitM>100000) return false;
            Reset(); Armed=true; CornerName=cornerName; EntryM=entryM; ExitM=exitM; Status=Waiting; return true;
        }
        public void Stop() { Interrupt(); Armed=false; Status="구간 연습 종료"; }
        public void Interrupt()
        {
            if (portion.Count>0) closed=true;
            portion.Clear(); last=null; if (Armed) Status="수신 중단 · 다음 전체 구간 대기";
        }
        bool AcceptContext(string course,string vehicle,string build,double metres)
        {
            if(!IsKnown(course)||!IsKnown(vehicle)||!IsKnown(build)||!Finite(metres)||metres<100||metres>100000||ExitM>metres) return false;
            if(track==null) {track=course;car=vehicle;version=build;length=metres;}
            return track==course && car==vehicle && version==build && length==metres;
        }
        void Discard(string reason)
        {
            portion.Clear();closed=true;Status=reason;
        }
        public bool Update(TelemetryFrame f)
        {
            if(!Armed) return false;
            if(f==null) {Interrupt();return false;}
            // Garage, invalid and replay frames cannot bind a newly armed exercise.
            if(!f.OnTrack||f.InPit||f.IsReplay||!f.ValidLapKnown||!f.IsValidLap)
            {
                if(track!=null && f.Lap>=0 && f.Track==track && f.Car==car && f.GameVersion==version && f.TrackLength==length)
                {lapNumber=f.Lap;Discard("피트·무효·리플레이 랩 제외");}
                last=null;portion.Clear();return false;
            }
            if(f.TimeIsCaptureClock||!Finite(f.Time)||f.Time<0||f.Lap<0||!Finite(f.LapDistance)||
                f.LapDistance<0||f.LapDistance>f.TrackLength||!ValidSpeed(f.SpeedKph)||
                !Finite(f.DistanceAgeSeconds)||f.DistanceAgeSeconds<0||f.DistanceAgeSeconds>MaxGap)
            {Interrupt();Status="게임 시계와 신선한 코스 위치 대기";return false;}
            if(!AcceptContext(f.Track,f.Car,f.GameVersion,f.TrackLength))
            {Interrupt();Status="선택한 차량·코스·빌드의 직접 주행 대기";return false;}
            if ((Finite(lastLiveTime) && f.Time<lastLiveTime) || (lapNumber>=0 && f.Lap<lapNumber))
            {
                history.Clear();Count=0;ResultText=ComparisonNote="";last=null;portion.Clear();closed=false;lapNumber=-1;
            }
            lastLiveTime=f.Time;
            var p=new Point {Time=f.Time,Distance=f.LapDistance,Speed=f.SpeedKph,Brake=f.Brake,
                Age=f.DistanceAgeSeconds,Fuel=f.FuelLitres,Steer=f.Steering,Throttle=f.Throttle};
            bool result=false;
            if (f.Lap!=lapNumber)
            {
                Point line=null;
                if(last!=null && f.Lap==lapNumber+1 && last.Distance>length*.8 && p.Distance<length*.2)
                {
                    double travel=length-last.Distance+p.Distance;
                    if(travel>0 && Continuous(last,p,travel))
                    {
                        var edge=Point.Between(last,p,(length-last.Distance)/travel);
                        line=new Point {Time=edge.Time,Speed=edge.Speed,Brake=edge.Brake,Age=edge.Age,Fuel=edge.Fuel,
                            Steer=edge.Steer,Throttle=edge.Throttle,Distance=length};
                        if(last.Distance<length && !closed) result=Observe(last,line);
                        line=new Point {Time=edge.Time,Speed=edge.Speed,Brake=edge.Brake,Age=edge.Age,Fuel=edge.Fuel,
                            Steer=edge.Steer,Throttle=edge.Throttle,Distance=0};
                    }
                }
                portion.Clear();closed=false;lapNumber=f.Lap;last=line;Status=Waiting;
            }
            if (last==null)
            {
                if (p.Distance==EntryM && !closed) {portion.Add(p);Status="선택 구간 측정 중";}
                else if(p.Distance>EntryM && !closed) Discard("진입 지점을 지난 뒤 연결됨 · 다음 랩 대기");
            }
            else if(!closed && p.Time!=last.Time)
            {
                double travel=p.Distance-last.Distance;
                bool frozenAtSpeed=travel==0 && Math.Min(last.Speed,p.Speed)>30 && p.Time-lastPositionChangeTime>MaxGap;
                if (!Continuous(last,p,travel) || frozenAtSpeed)
                {
                    if(portion.Count>0 || p.Distance>=EntryM) Discard("위치·시각 불연속 · 이번 구간 제외");
                }
                else result=Observe(last,p)||result;
            }
            if(last==null || last.Distance!=p.Distance || p.Speed<=30 || last.Speed<=30) lastPositionChangeTime=p.Time;
            if(last==null || last.Time!=p.Time || p.Distance==0) last=p;
            return result;
        }
        static bool Continuous(Point a,Point b,double travel)
        {
            double dt=b.Time-a.Time;
            // Scoring distance advances in steps behind the faster physics clock.
            double plausible=8+Math.Max(a.Speed,b.Speed)/3.6*(dt+a.Age+b.Age+.05)*1.8;
            return dt>0 && dt<=MaxGap && travel>=0 && travel<=plausible;
        }
        bool Observe(Point a,Point b)
        {
            if(portion.Count==0 && a.Distance<=EntryM && b.Distance>=EntryM && b.Distance>a.Distance)
            {
                portion.Add(Point.Between(a,b,(EntryM-a.Distance)/(b.Distance-a.Distance))); Status="선택 구간 측정 중";
            }
            if(portion.Count==0) return false;
            if(portion.Count>=6000 || b.Time-portion[0].Time>180)
            {Discard("구간 관측 시간이 길어 다음 시도 대기");return false;}
            if(b.Distance>=ExitM && b.Distance>a.Distance)
            {
                var exit=Point.Between(a,b,(ExitM-a.Distance)/(b.Distance-a.Distance));
                if(exit.Time>portion[portion.Count-1].Time) portion.Add(exit);
                var pass=Measure(lapNumber,portion);
                if(pass!=null) Add(pass);
                portion.Clear();closed=true;Status=pass==null?"구간 표본 부족 · 다음 랩 대기":"구간 측정 완료 · 다음 랩에서 같은 코너를 달리세요.";
                return pass!=null;
            }
            if(b.Time>portion[portion.Count-1].Time) portion.Add(b);
            return false;
        }
        // A recent native capture supplied by the host can be attempt 1. Imported creator
        // laps, remapped labels and unverified or partial recordings cannot seed a trial.
        public bool SeedFromLap(ReferenceLap lap)
        {
            if(!Armed || Count!=0 || portion.Count>0 || lap==null || lap.Game!="LMU" || !lap.HasTime ||
                !lap.Complete || lap.Validity!="valid" || lap.DistanceBasis!=ReferenceLap.LmuDistanceBasis ||
                lap.Conditions==null || lap.Samples==null || lap.Samples.Length<3) return false;
            string label; int nativeLap;
            if(!lap.Conditions.TryGetValue("sourceReader",out label) || label!="LMU live shared-memory reader" ||
                !lap.Conditions.TryGetValue("sourceTrackLabel",out label) || label!=lap.Track ||
                !lap.Conditions.TryGetValue("sourceCarLabel",out label) || label!=lap.Car ||
                !lap.Conditions.TryGetValue("sourceLapNumber",out label) || !Int32.TryParse(label,out nativeLap) || nativeLap<0) return false;
            try {lap.Validate();} catch(FormatException) {return false;}
            // Replay the fresh captured measurements through the same gate interpolation,
            // entrance clipping and pedal analysis as a live attempt.
            var replay=new BrakeReleaseExperiment();
            replay.Arm(CornerName,EntryM,ExitM);
            foreach(var s in lap.Samples)
            {
                if(!ValidSpeed(s.SpeedKph)) return false;
                // Preserve the measured timing precision and fuel of a fresh in-memory
                // capture. Missing age takes the least precise permitted native age.
                var f=new TelemetryFrame {Track=lap.Track,Car=lap.Car,GameVersion=lap.GameVersion,TrackLength=lap.TrackLengthM,
                    Lap=nativeLap,Time=s.Time,LapDistance=s.DistanceM,SpeedKph=s.SpeedKph,Brake=s.Brake,
                    DistanceAgeSeconds=Double.IsNaN(s.CapturedDistanceAgeSeconds)?MaxGap:s.CapturedDistanceAgeSeconds,FuelLitres=s.CapturedFuelLitres,
                    Steering=s.Steering,Throttle=s.Throttle,
                    ValidLapKnown=true,IsValidLap=true,OnTrack=true};
                if(replay.Update(f)) break;
            }
            var pass=replay.Latest;
            if(pass==null || !AcceptContext(lap.Track,lap.Car,lap.GameVersion,lap.TrackLengthM)) return false;
            if(lapNumber<0 || lapNumber==nativeLap) {lapNumber=nativeLap;closed=true;last=null;}
            Add(pass);Status="최근 완주 랩의 구간을 첫 시도로 기록했습니다.";return true;
        }
        static BrakeReleasePass Measure(int lap,List<Point> points)
        {
            if(points.Count<3) return null;
            Point first=points[0],end=points[points.Count-1];
            if(end.Time<=first.Time) return null;
            var pass=new BrakeReleasePass {Lap=lap,EntrySpeedKph=first.Speed,ExitSpeedKph=end.Speed,
                SegmentSeconds=end.Time-first.Time,FuelLitres=first.Fuel>=0 && Finite(first.Fuel)?(double?)first.Fuel:null,
                TimeResolutionSeconds=Math.Max(.02,first.Age+end.Age+points[1].Time-first.Time+end.Time-points[points.Count-2].Time)};
            MeasureRelease(points,pass);
            pass.FirstSteerM=ControlLandmark(points,true);pass.ThrottleReturnM=ControlLandmark(points,false);
            return pass;
        }
        // The first sustained steering buildup after a straight approach, and the last
        // sustained throttle return after a lift. Missing channels never become zeros.
        static double? ControlLandmark(List<Point> points,bool steering)
        {
            double high=steering?.15:.75, low=steering?.08:.5;
            double since=Double.NaN,at=Double.NaN;
            bool ready=false;
            double? marker=null;
            for(int i=0;i<points.Count;i++)
            {
                var p=points[i];double value=steering?Math.Abs(p.Steer):p.Throttle;
                if(!Unit(value)) return null;
                if(value<=low)
                {
                    ready=true;
                    if(!steering) marker=null;
                }
                if(!ready || value<high) {since=Double.NaN;continue;}
                if(!Finite(since)) {since=p.Time;at=p.Distance;}
                if(p.Time-since<Dwell) continue;
                if(!marker.HasValue || !steering) marker=at;
                ready=false;
            }
            return marker;
        }
        static Point PedalCrossing(Point a,Point b,double level)
        {return Point.Between(a,b,(level-a.Brake)/(b.Brake-a.Brake));}
        static void MeasureRelease(List<Point> points,BrakeReleasePass pass)
        {
            if(!Unit(points[0].Brake) || points[0].Brake>=Onset)
            {pass.ReleaseNote="구간 진입 전에 제동이 시작됐거나 진입 페달 미수신";return;}
            Point onset=null,from=null,to=null;
            double highSince=Double.NaN,lowSince=Double.NaN,minAfter=1,reapplySince=Double.NaN,maxStep=0,peak=0;
            bool braking=false,highHeld=false,releaseEstablished=false;
            for(int i=1;i<points.Count;i++)
            {
                Point a=points[i-1],b=points[i];
                if(!Unit(b.Brake)) {pass.ReleaseNote="첫 제동의 페달 신호 누락";return;}
                if(!braking)
                {
                    if(a.Brake<Onset && b.Brake>=Onset) onset=PedalCrossing(a,b,Onset);
                    if(b.Brake<Onset) {onset=null;highSince=Double.NaN;peak=0;continue;}
                    if(onset==null) continue;
                    peak=Math.Max(peak,b.Brake);
                    if(b.Brake>=High && !Finite(highSince)) highSince=a.Brake<High?PedalCrossing(a,b,High).Time:a.Time;
                    if(b.Brake<High) highSince=Double.NaN;
                    if(b.Time-onset.Time<Dwell) continue;
                    braking=true;pass.BrakeStartM=onset.Distance;
                    pass.BrakeStartResolutionM=Math.Max(3,(onset.Age+b.Time-a.Time)*onset.Speed/3.6);
                }
                peak=Math.Max(peak,b.Brake);pass.PeakBrakePct=peak*100;
                if(from!=null && !releaseEstablished)
                {
                    // A brief dip through 60% followed by a return to the held pedal
                    // must not move the start of the later, sustained release earlier.
                    if(b.Brake>=High) {from=null;to=null;lowSince=reapplySince=Double.NaN;maxStep=0;}
                    else if(b.Time-from.Time>=Dwell) releaseEstablished=true;
                }
                if(b.Brake>=High && from==null)
                {
                    if(!Finite(highSince)) highSince=a.Brake<High?PedalCrossing(a,b,High).Time:a.Time;
                    if(b.Time-highSince>=Dwell) highHeld=true;
                }
                if(b.Brake<High)
                {
                    if(highHeld && from==null && a.Brake>=High)
                    {from=PedalCrossing(a,b,High);minAfter=b.Brake;maxStep=b.Time-a.Time;}
                    highSince=Double.NaN;
                }
                if(from!=null)
                {
                    maxStep=Math.Max(maxStep,b.Time-a.Time);
                    minAfter=Math.Min(minAfter,b.Brake);
                    if(b.Brake-minAfter>=.20)
                    {
                        if(!Finite(reapplySince)) reapplySince=b.Time;
                        if(b.Time-reapplySince>=Dwell) {pass.ReleaseNote="첫 해제 도중 재제동 · 해제율 비교 보류";return;}
                    }
                    else reapplySince=Double.NaN;
                }
                if(b.Brake<=Low)
                {
                    if(!Finite(lowSince) && a.Brake>Low) {to=PedalCrossing(a,b,Low);lowSince=to.Time;}
                    if(!Finite(lowSince) || b.Time-lowSince<Dwell) continue;
                    if(from==null) {pass.ReleaseNote="첫 제동에서 60% 이상 유지 구간 미관측";return;}
                    double duration=to.Time-from.Time;
                    pass.ReleaseResolutionSeconds=2*maxStep;
                    if(duration<=maxStep) {pass.ReleaseNote="60→10% 해제가 표본 간격 이하";return;}
                    pass.ReleaseSeconds=duration;pass.ReleaseRatePctPerSecond=50/duration;pass.ReleaseNote="";
                    return;
                }
                lowSince=Double.NaN;to=null;
            }
            pass.ReleaseNote=braking?"첫 제동의 60→10% 해제 완료 미관측":"지속된 첫 제동 시작 미관측";
        }
        string N(double x,string format) { return x.ToString(format,culture); }
        void Add(BrakeReleasePass pass)
        {
            pass.AttemptNumber=++Count;history.Add(pass);
            if(history.Count>12) history.RemoveAt(0);
            var prior=Previous;
            string release=pass.ReleaseSeconds.HasValue?"60→10% 해제 "+N(pass.ReleaseSeconds.Value,"0.00")+"초":"해제율 미측정";
            ResultText=release+" · 탈출 "+N(pass.ExitSpeedKph,"0.0")+" km/h · 구간 "+N(pass.SegmentSeconds,"0.00")+"초";
            ComparisonNote="다음 랩에는 진입 속도와 제동 표지를 유지하고 브레이크를 푸는 속도만 바꿔 보세요.";
            if(prior==null)
            {
                if(!pass.ReleaseSeconds.HasValue) ComparisonNote=pass.ReleaseNote+" · 다음 시도와 같은 지점에서 비교합니다.";
                return;
            }
            ResultText+="\n직전 대비 탈출 "+N(pass.ExitSpeedKph-prior.ExitSpeedKph,"+0.0;-0.0;0.0")+" km/h · 구간 "+
                N(pass.SegmentSeconds-prior.SegmentSeconds,"+0.00;-0.00;0.00")+"초";
            if(!pass.ReleaseSeconds.HasValue || !prior.ReleaseSeconds.HasValue)
            {ComparisonNote="해제율 비교 보류 · "+(!pass.ReleaseSeconds.HasValue?pass.ReleaseNote:prior.ReleaseNote);return;}
            var conditions=new List<string>();
            if(Math.Abs(prior.EntrySpeedKph-pass.EntrySpeedKph)>5) conditions.Add("진입 속도");
            if(Math.Abs(prior.BrakeStartM.Value-pass.BrakeStartM.Value)>Math.Max(15,prior.BrakeStartResolutionM+pass.BrakeStartResolutionM)) conditions.Add("제동 시작점");
            if(Math.Abs(prior.PeakBrakePct.Value-pass.PeakBrakePct.Value)>10) conditions.Add("최대 제동 입력");
            double markerTolerance=Math.Max(15,pass.BrakeStartResolutionM+prior.BrakeStartResolutionM);
            if(pass.FirstSteerM.HasValue && prior.FirstSteerM.HasValue && Math.Abs(pass.FirstSteerM.Value-prior.FirstSteerM.Value)>markerTolerance) conditions.Add("첫 조향 위치");
            if(pass.ThrottleReturnM.HasValue && prior.ThrottleReturnM.HasValue && Math.Abs(pass.ThrottleReturnM.Value-prior.ThrottleReturnM.Value)>markerTolerance) conditions.Add("가속 복귀 위치");
            if(pass.FuelLitres.HasValue && prior.FuelLitres.HasValue && Math.Abs(prior.FuelLitres.Value-pass.FuelLitres.Value)>3) conditions.Add("연료량");
            if(conditions.Count>0)
            {ComparisonNote=String.Join("·",conditions.ToArray())+"도 달랐습니다. 다음 시도에서 함께 확인하세요.";return;}
            double difference=pass.ReleaseSeconds.Value-prior.ReleaseSeconds.Value;
            string change=Math.Abs(difference)<=pass.ReleaseResolutionSeconds+prior.ReleaseResolutionSeconds?
                "해제 시간 차이가 표본 오차 범위입니다.":
                "60→10%를 "+N(Math.Abs(difference),"0.00")+"초 "+(difference>0?"더 길게":"더 짧게")+" 풀었습니다.";
            ComparisonNote=change+(Math.Abs(pass.SegmentSeconds-prior.SegmentSeconds)<=pass.TimeResolutionSeconds+prior.TimeResolutionSeconds?
                " 구간 시간 차이는 계측 오차 범위입니다.":" 같은 진입 조건에서 결과가 반복되는지 확인하세요.");
        }
        public string ExportJson()
        {
            return new JavaScriptSerializer().Serialize(new { schemaVersion=1, armed=Armed, corner=CornerName,
                track=track,car=car,build=version,entryM=EntryM,exitM=ExitM,releaseFromPct=60,releaseToPct=10,
                passValidity="valid-at-segment-exit", totalAttempts=Count, recent=Recent, comparison=ComparisonNote });
        }
    }
}
