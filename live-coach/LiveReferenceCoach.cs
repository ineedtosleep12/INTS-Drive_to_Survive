using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    public sealed class LiveReferenceView
    {
        public bool Ready,NewFeedback;
        public bool UsedObservedGraphicsTiming;
        public string Status="기준 주행 코칭 대기",FeedbackId="",Heading="",Observation="",Principle="",NextAction="",Estimate="",CornerName="";
        public string LineObservation="";
        public double MeanAbsoluteLineOffsetM=double.NaN;
        public double LineDivergenceM=double.NaN;
        public double TimeResolutionSeconds=double.NaN;
        public string EvidenceKind="";
        public ReferenceSample ReferenceAtPosition;
        public double StartDistanceM=double.NaN,EndDistanceM=double.NaN,TimeLossSeconds=double.NaN;
        public double EntryLossSeconds=double.NaN,MidLossSeconds=double.NaN,ExitLossSeconds=double.NaN;
    }

    // Runs under the host's telemetry lock. Source and aliases are frozen at Configure.
    // Zones describe recorded braking/lift complexes, not official named corners.
    public sealed partial class LiveReferenceCoach
    {
        sealed class Zone
        {public int Id;public double Start,End,First,Second;public string Name;}
        ReferenceLap reference;
        bool enabled,allowVersion,allowMapping,compatible,activeObservedTiming;
        string sourceCar="",sourceTrack="",sourceLayout="",context="",hold="기준 주행을 선택하세요.";
        string[] warnings=new string[0];
        readonly List<Zone> zones=new List<Zone>();
        readonly List<ReferenceSample> activeSamples=new List<ReferenceSample>();
        readonly HashSet<int> visited=new HashSet<int>();
        readonly List<LiveReferenceView> history=new List<LiveReferenceView>();
        Zone active,pendingZone;
        ReferenceSample previous;
        double layoutMismatchStartM=double.NaN;
        int lap=-1,rejectedLap=-1;
        double ownPeak,activeDistanceAge,safeSince=double.NaN,pendingTime,feedbackUntil;
        LiveReferenceView feedback;
        ReferenceLap ownMetadata;
        public int PreparedZoneCount {get{return zones.Count;}}
        public double[] PreparedZoneStarts {get{return zones.Select(z=>z.Start).ToArray();}}
        internal double[] PreparedZoneEnds {get{return zones.Select(z=>z.End).ToArray();}}

        public void Configure(ReferenceLiveSettings settings)
        {
            reference=settings==null?null:settings.Reference;
            enabled=settings!=null&&settings.Enabled;
            allowVersion=settings!=null&&settings.AllowVersionMismatch;allowMapping=settings!=null&&settings.AllowMotecAlignment;
            sourceCar=settings==null?"":settings.ConfirmedSourceCar;sourceTrack=settings==null?"":settings.ConfirmedSourceTrack;sourceLayout=settings==null?"":settings.ConfirmedSourceLayout;
            zones.Clear();history.Clear();Reset();
            if(reference!=null)try {reference.Validate();BuildZones();}catch(FormatException ex){reference=null;hold="기준 파일 확인: "+ex.Message;}
        }
        public void Reset()
        {context="";compatible=false;ownMetadata=null;previous=null;lap=rejectedLap=-1;layoutMismatchStartM=double.NaN;visited.Clear();ClearCorner();hold="기준 주행과 실제 차량·코스를 확인하는 중";}
        void ClearCorner()
        {active=pendingZone=null;activeSamples.Clear();feedback=null;safeSince=double.NaN;feedbackUntil=0;ownPeak=activeDistanceAge=0;activeObservedTiming=false;}
        static bool Finite(double n){return ReferenceLap.Finite(n);}
        static bool Same(string a,string b){return ReferenceLap.Key(a)==ReferenceLap.Key(b);}
        static bool DistanceTiming(string game,TelemetryFrame frame,out double margin,out bool observed)
        {
            margin=0;observed=false;
            if(Finite(frame.DistanceAgeSeconds))
            {margin=frame.DistanceAgeSeconds;return margin>=0&&margin<=.5;}
            // ACC pages have no shared source timestamp. Observe graphics packet
            // changes separately; never relabel host observation as source age.
            double age=frame.DistanceObservationAgeSeconds,interval=frame.DistanceObservationIntervalSeconds;
            if(!double.IsNaN(frame.DistanceAgeSeconds)||!Same(game,"ACC")||!Finite(age)||age<0||age>.5||!Finite(interval)||interval<=0||interval>.5)return false;
            observed=true; margin=.5+age+interval;
            // The extra 0.5 s is a conservative decision margin, not a measured
            // latency or an upper bound on synchronization error.
            return true;
        }
        static string Number(double n){return n.ToString("0.0",CultureInfo.InvariantCulture);}
        static string Delta(double n){return n.ToString("+0.00;-0.00;0.00",CultureInfo.InvariantCulture);}
        static double MaxGap(ReferenceLap lap)
        {return lap.Samples.Zip(lap.Samples.Skip(1),(a,b)=>b.Time-a.Time).DefaultIfEmpty(.2).Max();}
        static bool Turning(ReferenceSample s)
        {return Finite(s.Brake)&&s.Brake>=.15 || Finite(s.Steering)&&Finite(s.Throttle)&&Math.Abs(s.Steering)>=.05&&s.Throttle<.9;}
        static bool Settled(ReferenceSample s,double referencePeak)
        {return Finite(s.Brake)&&Finite(s.Throttle)&&Finite(s.Steering)&&s.Brake<.08&&s.Throttle>=.85&&Math.Abs(s.Steering)<=Math.Max(.06,Math.Min(.12,referencePeak*.4));}
        void BuildZones()
        {
            if(!reference.HasTime){BuildSpatialZones();return;}
            int begin=-1,candidate=-1,clear=-1;
            double referencePeak=0;
            var raw=new List<Zone>();var samples=reference.Samples;
            for(int i=0;i<samples.Length;i++)
            {
                var s=samples[i];
                if(i>0&&(s.Time-samples[i-1].Time>1||s.DistanceM-samples[i-1].DistanceM>100)){begin=candidate=clear=-1;continue;}
                if(begin<0)
                {
                    if(Turning(s))
                    {if(candidate<0)candidate=i;double required=s.Brake>=.15?.1:.3;if(s.Time-samples[candidate].Time>=required){begin=candidate;clear=-1;referencePeak=samples.Skip(begin).Take(i-begin+1).Where(p=>Finite(p.Steering)).Select(p=>Math.Abs(p.Steering)).DefaultIfEmpty(0).Max();}}
                    else candidate=-1;
                }
                else
                {
                    if(Finite(s.Steering))referencePeak=Math.Max(referencePeak,Math.Abs(s.Steering));
                    if(s.Time-samples[begin].Time>40){begin=candidate=clear=-1;continue;}
                    if(Settled(s,referencePeak)&&s.Time-samples[begin].Time>1)
                    {
                        if(clear<0)clear=i;
                        if(s.Time-samples[clear].Time>=.6)
                        {
                            double start=Math.Max(samples[0].DistanceM,samples[begin].DistanceM-100);
                            if(s.DistanceM-start>=60)raw.Add(new Zone {Start=start,End=s.DistanceM});
                            begin=candidate=clear=-1;
                        }
                    }
                    else clear=-1;
                }
            }
            foreach(var z in raw)
            {
                if(zones.Count>0&&z.Start<=zones.Last().End+25)zones.Last().End=z.End;
                else zones.Add(z);
            }
            for(int i=0;i<zones.Count;i++)
            {
                var z=zones[i];z.Id=i;z.Name="감속·회전 구간 "+(i+1);
                // Fixed thirds avoid pretending to identify an apex from sparse speed data.
                z.First=z.Start+(z.End-z.Start)/3;z.Second=z.Start+(z.End-z.Start)*2/3;
            }
        }
        static string Basis(string game)
        {switch(ReferenceLap.Key(game)){case "lmu":return ReferenceLap.LmuDistanceBasis;case "ams2":return CapturedLap.Ams2DistanceBasis;case "acc":return CapturedLap.AccDistanceBasis;case "ac":return CapturedLap.AcDistanceBasis;default:return "unsupported";}}
        static string Coordinates(string game)
        {switch(ReferenceLap.Key(game)){case "lmu":return ReferenceLap.LmuCoordinateFrame;case "ams2":return CapturedLap.Ams2CoordinateFrame;case "acc":return CapturedLap.AccCoordinateFrame;case "ac":return CapturedLap.AcCoordinateFrame;default:return "unknown";}}
        void CheckContext(string game,TelemetryFrame f)
        {
            bool alias=!string.IsNullOrEmpty(sourceCar)&&!string.IsNullOrEmpty(sourceTrack)&&Same(f.Car,sourceCar)&&Same(f.Track,sourceTrack)&&Same(game,reference.Game);
            // This metadata-only probe reuses source samples solely to exercise the shared
            // compatibility gates. It is never a driver recording or a coaching result.
            var probe=new ReferenceLap {
                Game=game,Car=alias?reference.Car:f.Car,Track=alias?reference.Track:f.Track,
                Layout=alias&&!ReferenceLap.Unknown(sourceLayout)?sourceLayout:"unknown",GameVersion=f.GameVersion,
                TimeBasis=reference.TimeBasis,TrackLengthM=f.TrackLength,DistanceBasis=Basis(game),Complete=false,Validity="valid",
                Samples=reference.Samples.Select(s=>s.Copy()).ToArray(),CoordinateFrame=Coordinates(game),
                LateralPositionBasis=Same(game,"LMU")?ReferenceLap.LmuLateralBasis:"unknown"
            };
            probe.Conditions["sourceCarLabel"]=f.Car;
            probe.Conditions["sourceTrackLabel"]=f.Track;
            var check=ReferenceComparison.Compare(reference,probe,allowVersion,allowMapping);
            compatible=check.Comparable;hold=check.Reason;warnings=check.Warnings;ownMetadata=probe;ownMetadata.Samples=new ReferenceSample[0];
            if(compatible&&zones.Count==0){compatible=false;hold="기준 파일에서 완전한 감속·회전 구간을 찾지 못했습니다.";}
        }
        static ReferenceSample Sample(TelemetryFrame f)
        {return new ReferenceSample {Time=f.Time,DistanceM=f.LapDistance,SpeedKph=f.SpeedKph,Brake=f.Brake,Throttle=f.Throttle,Steering=f.Steering,X=Finite(f.X)&&Finite(f.Z)?f.X:double.NaN,Z=Finite(f.X)&&Finite(f.Z)?f.Z:double.NaN,PathLateralM=f.PathLateralM};}
        bool Safe(TelemetryFrame f)
        {
            double relaxed=Math.Max(.06,Math.Min(.15,ownPeak*.4));
            return f.Brake<.08&&f.Throttle>.25&&f.SpeedKph>40&&Math.Abs(f.Steering)<=relaxed&&(!Finite(f.LateralG)||Math.Abs(f.LateralG)<.5);
        }
        LiveReferenceView View(TelemetryFrame f,bool isNew=false)
        {
            var v=new LiveReferenceView {Ready=compatible,Status=compatible?"기준 구간을 통과하면 코너 뒤에서 알려드립니다.":hold};
            if(compatible&&f!=null)v.ReferenceAtPosition=ReferenceComparison.SampleAtDistance(reference,f.LapDistance);
            if(feedback!=null&&f!=null&&f.Time<feedbackUntil)
            {
                v.FeedbackId=feedback.FeedbackId;v.Heading=feedback.Heading;v.Observation=feedback.Observation;v.Principle=feedback.Principle;v.NextAction=feedback.NextAction;v.Estimate=feedback.Estimate;v.CornerName=feedback.CornerName;v.StartDistanceM=feedback.StartDistanceM;v.EndDistanceM=feedback.EndDistanceM;v.TimeLossSeconds=feedback.TimeLossSeconds;v.EntryLossSeconds=feedback.EntryLossSeconds;v.MidLossSeconds=feedback.MidLossSeconds;v.ExitLossSeconds=feedback.ExitLossSeconds;v.NewFeedback=isNew;
                v.LineObservation=feedback.LineObservation;v.MeanAbsoluteLineOffsetM=feedback.MeanAbsoluteLineOffsetM;
                v.LineDivergenceM=feedback.LineDivergenceM;v.EvidenceKind=feedback.EvidenceKind;v.TimeResolutionSeconds=feedback.TimeResolutionSeconds;
                v.UsedObservedGraphicsTiming=feedback.UsedObservedGraphicsTiming;
            }
            else v.Estimate=compatible?"표본·주행 조건 차이가 있는 관측 비교입니다. 정확한 교정값이 아닙니다.":"";
            return v;
        }
        public LiveReferenceView Update(string game,TelemetryFrame f)
        {
            if(!enabled){hold="기준 주행 오버레이 코칭 꺼짐";compatible=false;return View(null);}
            if(reference==null){hold="실제 기준 주행 파일을 먼저 선택하세요.";compatible=false;return View(null);}
            if(f==null){Reset();hold="실시간 데이터 대기 · 이전 알림을 지웠습니다.";return View(null);}
            double distanceMargin;bool observedTiming;
            bool timing=DistanceTiming(game,f,out distanceMargin,out observedTiming);
            if(!Finite(f.Time)||f.TimeIsCaptureClock||!Finite(f.LapDistance)||!Finite(f.TrackLength)||f.Lap<0||!Finite(f.Brake)||!Finite(f.Throttle)||!Finite(f.Steering)||!Finite(f.SpeedKph)||f.SpeedKph<0||f.SpeedKph>550||f.Brake<0||f.Brake>1||f.Throttle<0||f.Throttle>1||Math.Abs(f.Steering)>1||f.LapDistance<0||f.LapDistance>f.TrackLength||!timing)
            {ClearCorner();previous=null;hold="거리·시계·입력 갱신을 확인할 수 없어 코칭 대기";return new LiveReferenceView {Status=hold};}
            string key=(game??"")+"|"+f.Car+"|"+f.Track+"|"+f.GameVersion+"|"+f.TrackLength.ToString("R",CultureInfo.InvariantCulture);
            if(key!=context){Reset();context=key;CheckContext(game,f);}
            if(!compatible)return View(null);
            if(lap!=f.Lap)
            {ClearCorner();previous=null;visited.Clear();lap=f.Lap;layoutMismatchStartM=double.NaN;}
            if(!f.OnTrack||f.InPit||f.IsReplay||!f.ValidLapKnown||!f.IsValidLap)
            {rejectedLap=f.Lap;ClearCorner();previous=null;return new LiveReferenceView {Status="피트·무효·리플레이 랩은 기준 코칭에서 제외합니다."};}
            if(rejectedLap==f.Lap)return new LiveReferenceView {Status="이번 랩 비교 제외 · 다음 유효 랩을 기다립니다."};
            var current=Sample(f);
            if(ReferenceComparison.NeedsNativePositionCheck(reference))
            {
                var expected=ReferenceComparison.SampleAtDistance(reference,current.DistanceM);
                if(ReferenceComparison.IsGameSelectedLayout(reference) &&
                    ReferenceComparison.SustainedLayoutMismatch(expected,current,ref layoutMismatchStartM))rejectedLap=f.Lap;
                if(!ReferenceComparison.NearbyWorldPositions(expected,current))
                {
                    ClearCorner();previous=null;
                    return new LiveReferenceView {Status="같은 코스 거리의 실제 위치를 확인하는 중 · 레이아웃과 계측선을 확인하세요."};
                }
            }
            if(previous!=null)
            {
                double dt=f.Time-previous.Time,ds=f.LapDistance-previous.DistanceM;
                if(dt<=0){if(dt<0){ClearCorner();previous=current;}return View(f);}
                if(dt>1||ds< -2||ds>Math.Max(80,Math.Max(f.SpeedKph,previous.SpeedKph)/3.6*dt*3+25))
                {ClearCorner();previous=current;return new LiveReferenceView {Status="기록이 끊겨 이번 구간을 제외했습니다."};}
                // Scoring can briefly jitter backwards. Never create non-monotonic traces.
                if(ds<0)current.DistanceM=previous.DistanceM;
            }
            bool newFeedback=false;
            // A stale previous-corner tail must not consume the same scoring frame
            // that crosses the next zone's start (the frame usually overshoots it).
            if(active!=null&&previous!=null&&zones.Any(z=>z.Id!=active.Id&&z.Start>active.Start&&previous.DistanceM<=z.Start&&current.DistanceM>=z.Start))
            {visited.Add(active.Id);ClearCorner();}
            if(active==null)
            {
                var zone=zones.FirstOrDefault(z=>!visited.Contains(z.Id)&&previous!=null&&previous.DistanceM<=z.Start&&current.DistanceM>=z.Start&&current.DistanceM<z.End);
                if(zone!=null)
                {feedback=null;pendingZone=null;safeSince=double.NaN;active=zone;ownPeak=activeDistanceAge=0;activeObservedTiming=false;activeSamples.Clear();activeSamples.Add(previous.Copy());}
            }
            if(active!=null)
            {
                ownPeak=Math.Max(ownPeak,Math.Abs(f.Steering));activeDistanceAge=Math.Max(activeDistanceAge,distanceMargin);activeObservedTiming|=observedTiming;activeSamples.Add(current.Copy());
                if(activeSamples.Count>4000){visited.Add(active.Id);ClearCorner();}
                else if(current.DistanceM>=active.End)
                {
                    if(pendingZone==null){visited.Add(active.Id);pendingZone=active;pendingTime=f.Time;safeSince=double.NaN;}
                    bool nextCorner=zones.Any(z=>z.Id!=active.Id&&z.Start>active.Start&&current.DistanceM>=z.Start);
                    if(nextCorner||f.Time-pendingTime>4||current.DistanceM>active.End+220)ClearCorner();
                    else if(Safe(f))
                    {
                        if(!Finite(safeSince))safeSince=f.Time;
                        if(f.Time-safeSince>=.6)
                        {
                            feedback=Analyze(active,activeSamples);active=pendingZone=null;activeSamples.Clear();safeSince=double.NaN;
                            if(feedback!=null){feedbackUntil=f.Time+8;newFeedback=true;history.Add(feedback);if(history.Count>100)history.RemoveAt(0);}
                        }
                    }
                    else safeSince=double.NaN;
                }
            }
            if(feedback!=null&&(!Safe(f)||f.Time>=feedbackUntil))feedback=null;
            previous=current;return View(f,newFeedback);
        }
        ReferenceLap OwnLap(IList<ReferenceSample> samples)
        {return new ReferenceLap {Game=ownMetadata.Game,Car=ownMetadata.Car,Track=ownMetadata.Track,Layout=ownMetadata.Layout,GameVersion=ownMetadata.GameVersion,TrackLengthM=ownMetadata.TrackLengthM,DistanceBasis=ownMetadata.DistanceBasis,CoordinateFrame=ownMetadata.CoordinateFrame,LateralPositionBasis=ownMetadata.LateralPositionBasis,Conditions=ownMetadata.Conditions,Validity="valid",Complete=false,Samples=samples.Select(s=>s.Copy()).ToArray()};}
        static bool ContinuousCoordinates(ReferenceLap data,double start,double end)
        {
            ReferenceSample previous=null;
            foreach(var s in data.Samples.Where(p=>p.DistanceM>=start&&p.DistanceM<=end))
            {
                if(previous!=null)
                {
                    double dt=s.Time-previous.Time;
                    if((data.HasTime&&(dt<=0||dt>1))||s.DistanceM-previous.DistanceM>100)return false;
                    if(Finite(s.X)&&Finite(s.Z)&&Finite(previous.X)&&Finite(previous.Z))
                    {
                        double speed=Math.Max(s.SpeedKph,previous.SpeedKph),dx=s.X-previous.X,dz=s.Z-previous.Z;
                        double permitted=data.HasTime?Math.Max(20,speed/3.6*dt*1.5+5):Math.Max(20,(s.DistanceM-previous.DistanceM)*3+5);
                        if(!Finite(speed)||Math.Sqrt(dx*dx+dz*dz)>permitted)return false;
                    }
                }
                previous=s;
            }
            return previous!=null;
        }
        void ObserveLine(LiveReferenceView view,ComparisonResult result,ReferenceLap own,Zone zone)
        {
            if(!result.HasLineComparison||!ContinuousCoordinates(reference,zone.Start,zone.End)||!ContinuousCoordinates(own,zone.Start,zone.End))return;
            var pairs=result.Points.Where(p=>Finite(p.LineOffsetM)&&p.Reference!=null&&p.Own!=null).ToArray();
            if(pairs.Length<10||pairs.Length<result.Points.Length*.95)return;
            // A large coordinate discrepancy can be a frame/origin mismatch rather
            // than a driver's line. No automatic alignment or left/right inference.
            foreach(var p in pairs)
            {double dx=p.Own.X-p.Reference.X,dz=p.Own.Z-p.Reference.Z;if(!Finite(dx)||!Finite(dz)||Math.Sqrt(dx*dx+dz*dz)>25)return;}
            view.MeanAbsoluteLineOffsetM=pairs.Average(p=>Math.Abs(p.LineOffsetM));
            view.LineObservation="기준 경로와 평균 "+Number(view.MeanAbsoluteLineOffsetM)+" m 차이.";
            view.Observation+=" "+view.LineObservation;
            view.Estimate+=" 경로 차이는 같은 코스 위치에서 기준 경로의 접선에 수직인 거리의 절댓값 평균입니다. 실측 좌표만 사용하며 좌우 지시·최속 라인·시간 손실 원인으로 단정하지 않습니다.";
        }
        double TimeAt(ReferenceLap data,double position)
        {var s=ReferenceComparison.SampleAtDistance(data,position);return s==null?double.NaN:s.Time;}
        static double EndOfBrake(ReferenceLap lap,double start,double end)
        {
            bool braking=false;double released=double.NaN,candidate=double.NaN,releaseTime=double.NaN,releasePosition=double.NaN;
            foreach(var s in lap.Samples.Where(s=>s.DistanceM>=start&&s.DistanceM<=end))
            {
                if(!braking)
                {
                    if(s.Brake>=.15){if(!Finite(candidate))candidate=s.Time;if(s.Time-candidate>=.1){braking=true;released=releaseTime=releasePosition=double.NaN;}}
                    else candidate=double.NaN;
                }
                else if(s.Brake<=.08)
                {if(!Finite(releaseTime)){releaseTime=s.Time;releasePosition=s.DistanceM;}if(s.Time-releaseTime>=.15){released=releasePosition;braking=false;candidate=double.NaN;}}
                else releaseTime=releasePosition=double.NaN;
            }
            return released;
        }
        static double Unwind(ReferenceLap lap,double start,double end)
        {
            var samples=lap.Samples.Where(s=>s.DistanceM>=start&&s.DistanceM<=end&&Finite(s.Steering)).ToArray();
            if(samples.Length<4)return double.NaN;double peak=samples.Max(s=>Math.Abs(s.Steering));if(peak<.05)return double.NaN;
            int index=Array.FindLastIndex(samples,s=>Math.Abs(s.Steering)>peak*.3);
            double since=double.NaN,position=double.NaN;
            for(int i=index+1;i<samples.Length;i++)
            {if(Math.Abs(samples[i].Steering)<=peak*.3){if(!Finite(since)){since=samples[i].Time;position=samples[i].DistanceM;}if(samples[i].Time-since>=.3)return position;}else since=position=double.NaN;}
            return double.NaN;
        }
        static double ThrottleReturn(ReferenceLap lap,double start,double end)
        {
            bool lifted=false;double since=double.NaN,position=double.NaN,settled=double.NaN;
            foreach(var s in lap.Samples.Where(s=>s.DistanceM>=start&&s.DistanceM<=end))
            {if(s.Throttle<.6){lifted=true;since=position=settled=double.NaN;}else if(lifted&&s.Throttle>=.8){if(!Finite(since)){since=s.Time;position=s.DistanceM;}if(s.Time-since>=.3)settled=position;}else since=position=double.NaN;}
            return settled;
        }
        LiveReferenceView Analyze(Zone zone,List<ReferenceSample> samples)
        {
            var ownInput=OwnLap(samples);var own=OwnLap(samples);var first=ReferenceComparison.SampleAtDistance(own,zone.Start);var last=ReferenceComparison.SampleAtDistance(own,zone.End);
            if(first==null||last==null)return null;
            own.Samples=(new[]{first}).Concat(own.Samples.Where(s=>s.DistanceM>zone.Start&&s.DistanceM<zone.End)).Concat(new[]{last}).ToArray();
            ComparisonResult result=ReferenceComparison.Compare(reference,own,allowVersion,allowMapping);if(!result.Comparable||result.Points.Length<5)return null;
            return Diagnose(zone,own,ownInput,result,first,last,Math.Min(reference.Samples.Last().DistanceM,ownInput.Samples.Last().DistanceM));
        }
        LiveReferenceView Diagnose(Zone zone,ReferenceLap own,ReferenceLap ownInput,ComparisonResult result,ReferenceSample first,ReferenceSample last,double inputEnd)
        {
            LiveReferenceView view=result.HasTimeComparison ? AnalyzeTimed(zone,own,ownInput,result,first,last,inputEnd) :
                AnalyzeSpatial(zone,own,result,first,last);
            if(view!=null) ExplainDrivingDifference(view,zone,ownInput,result);
            return view;
        }
        LiveReferenceView AnalyzeTimed(Zone zone,ReferenceLap own,ReferenceLap ownInput,ComparisonResult result,ReferenceSample first,ReferenceSample last,double inputEnd)
        {
            double entry=(TimeAt(own,zone.First)-TimeAt(own,zone.Start))-(TimeAt(reference,zone.First)-TimeAt(reference,zone.Start));
            double middle=(TimeAt(own,zone.Second)-TimeAt(own,zone.First))-(TimeAt(reference,zone.Second)-TimeAt(reference,zone.First));
            double exit=(TimeAt(own,zone.End)-TimeAt(own,zone.Second))-(TimeAt(reference,zone.End)-TimeAt(reference,zone.Second));
            if(!Finite(entry)||!Finite(middle)||!Finite(exit))return null;
            double loss=entry+middle+exit;
            var localReference=new ReferenceLap {Samples=reference.Samples.Where(s=>s.DistanceM>=zone.Start&&s.DistanceM<=zone.End).ToArray()};
            double sampleUncertainty=MaxGap(localReference)+MaxGap(own)+activeDistanceAge+.1;
            double threshold=Math.Max(.25,sampleUncertainty);
            string phase=entry>=middle&&entry>=exit?"전반":middle>=exit?"중반":"후반";
            double worst=Math.Max(entry,Math.Max(middle,exit));
            var v=new LiveReferenceView {Ready=true,FeedbackId=lap+":"+zone.Id,CornerName=zone.Name,StartDistanceM=zone.Start,EndDistanceM=zone.End,TimeLossSeconds=loss,EntryLossSeconds=entry,MidLossSeconds=middle,ExitLossSeconds=exit,
                Heading=zone.Name+" · "+(loss>threshold?"약 "+Number(loss)+"초 손실":loss< -threshold?"약 "+Number(-loss)+"초 단축":"시간 차이 작음"),
                Observation=worst>threshold?phase+" "+Number(zone.Start+((phase=="전반")?0:phase=="중반"?(zone.End-zone.Start)/3:(zone.End-zone.Start)*2/3))+" m 이후에서 약 "+Number(worst)+"초 차이.":"표본 해상도와 조건 차이 안일 수 있습니다.",
                Principle="기준과 조건이 다르면 같은 입력도 결과가 달라질 수 있어요.",NextAction="다음 랩도 같은 진입 속도로 재현해 보세요.",
                Estimate="자동 감지한 감속·회전 복합 구간의 전반/중반/후반입니다. 공식 코너·에이펙스 판정이 아닙니다. 작은 시간 차이 판단 기준 약 "+Number(threshold)+"초. 조건을 통제하지 않은 관측입니다. "+string.Join(" ",warnings.Take(6).ToArray())};
            ObserveLine(v,result,own,zone);
            v.TimeResolutionSeconds=threshold;
            v.UsedObservedGraphicsTiming=activeObservedTiming;
            if(activeObservedTiming)v.Estimate+=" ACC 위치·입력의 공통 원본 시각이 없어 동기 오차는 미확인입니다. PC에서 관측한 그래픽 갱신 간격·경과 시간에 판단 여유 0.5초를 추가했습니다. 이는 측정된 원본 지연이나 오차 상한이 아닙니다.";
            if(loss<=threshold)return v;
            var rEntry=ReferenceComparison.SampleAtDistance(reference,zone.Start);var rExit=ReferenceComparison.SampleAtDistance(reference,zone.End);
            if(rEntry==null||rExit==null||!Finite(rEntry.SpeedKph)||!Finite(rExit.SpeedKph))return v;
            double speedDelta=first.SpeedKph-rEntry.SpeedKph;
            // Keep comparison timing at fixed course anchors. Later driver unwind/release
            // remains observable in this bounded tail, without moving the time-loss window.
            double brakeRelease=EndOfBrake(ownInput,zone.Start,inputEnd),refBrakeRelease=EndOfBrake(reference,zone.Start,inputEnd);
            double ownUnwind=Unwind(ownInput,zone.Start,inputEnd),refUnwind=Unwind(reference,zone.Start,inputEnd);
            double ownThrottle=ThrottleReturn(ownInput,zone.Start,inputEnd),refThrottle=ThrottleReturn(reference,zone.Start,inputEnd);
            double positionTolerance=Math.Max(15,Math.Max(rEntry.SpeedKph,first.SpeedKph)/3.6*sampleUncertainty);
            if(Math.Abs(speedDelta)>Math.Max(4,Math.Min(10,rEntry.SpeedKph*.04)))
            {v.EvidenceKind="entry-speed";v.Observation+=" 진입 속도도 "+Number(Math.Abs(speedDelta))+" km/h "+(speedDelta>0?"높았음.":"낮았음.");v.Principle="진입 속도가 다르면 필요한 감속량과 회전 여유도 달라져요.";v.NextAction="제동·조향을 함께 바꾸지 말고 진입 속도부터 맞춰 보세요.";}
            else if(Finite(brakeRelease)&&Finite(refBrakeRelease)&&brakeRelease-refBrakeRelease>positionTolerance&&middle+exit>threshold&&last.SpeedKph<rExit.SpeedKph-3)
            {v.EvidenceKind="release-exit-speed";v.Observation+=" 제동 해제가 늦고 탈출 속도도 낮았음.";v.Principle="감속과 회전은 접지를 나눠 쓰고, 해제는 차 균형에도 영향을 줘요.";v.NextAction="진입 속도는 유지하고 브레이크 해제만 조금 앞당겨 부드럽게 시험하세요.";}
            else if(exit>threshold&&Finite(ownUnwind)&&Finite(refUnwind)&&ownUnwind-refUnwind>positionTolerance&&Finite(ownThrottle)&&Finite(refThrottle)&&ownThrottle-refThrottle>positionTolerance)
            {v.EvidenceKind="unwind-throttle";v.Observation+=" 내 조향을 푸는 시점과 가속 복귀가 뒤였음.";v.Principle="조향을 풀수록 가속에 쓸 접지 여유가 생길 수 있어요.";v.NextAction="진입 속도를 유지하고 출구를 보며 조향을 점진적으로 풀어 보세요.";v.Estimate+=" 조향은 각 랩 자체 최대 입력 대비 해제 시점만 비교했습니다. 휠 각도나 좌우 부호 비교가 아닙니다.";}
            else
            {v.Observation+=" 입력 차이만으로 원인을 정하기는 어려움.";v.NextAction="다음 랩은 진입 속도를 유지하고 해제 구간을 다시 비교하세요.";}
            return v;
        }
        public string ExportJson()
        {
            return new JavaScriptSerializer().Serialize(new {schemaVersion=1,enabled=enabled,source=reference==null?null:reference.SourceUrl,game=reference==null?null:reference.Game,car=reference==null?null:reference.Car,track=reference==null?null:reference.Track,allowVersionMismatch=allowVersion,allowMotecAlignment=allowMapping,confirmedSourceCar=sourceCar,confirmedSourceTrack=sourceTrack,preparedZones=zones.Count,
                events=history.Select(v=>new {id=v.FeedbackId,corner=v.CornerName,startM=v.StartDistanceM,endM=v.EndDistanceM,lossSeconds=JsonNumber(v.TimeLossSeconds),firstThirdLoss=JsonNumber(v.EntryLossSeconds),middleThirdLoss=JsonNumber(v.MidLossSeconds),lastThirdLoss=JsonNumber(v.ExitLossSeconds),observation=v.Observation,lineObservation=v.LineObservation,meanAbsoluteLineOffsetM=JsonNumber(v.MeanAbsoluteLineOffsetM),lineDivergenceM=JsonNumber(v.LineDivergenceM),evidenceKind=v.EvidenceKind,usedObservedGraphicsTiming=v.UsedObservedGraphicsTiming,principle=v.Principle,nextAction=v.NextAction,limits=v.Estimate}).ToArray()});
        }
        static object JsonNumber(double value){return Finite(value)?(object)value:null;}
        static double Magnitude(ReferenceSample s){return Finite(s.Steering)?Math.Abs(s.Steering):Math.Abs(s.SteeringDegrees);}
        void BuildSpatialZones()
        {
            var raw=new List<Zone>();var samples=reference.Samples;int begin=-1,candidate=-1,clear=-1;double peak=0;
            for(int i=0;i<samples.Length;i++)
            {
                var s=samples[i];double magnitude=Magnitude(s);bool normalized=Finite(s.Steering);
                if(i>0&&s.DistanceM-samples[i-1].DistanceM>100){begin=candidate=clear=-1;continue;}
                bool turning=s.Brake>=.15||(Finite(magnitude)&&magnitude>=(normalized?.05:12)&&s.Throttle<.8);
                if(begin<0)
                {
                    if(turning){if(candidate<0)candidate=i;if(i>candidate&&s.DistanceM-samples[candidate].DistanceM>=(s.Brake>=.15?5:15)){begin=candidate;clear=-1;peak=Finite(magnitude)?magnitude:0;}}
                    else candidate=-1;
                }
                else
                {
                    if(Finite(magnitude))peak=Math.Max(peak,magnitude);
                    if(s.DistanceM-samples[begin].DistanceM>1200){begin=candidate=clear=-1;continue;}
                    bool straight=Finite(magnitude)&&magnitude<=Math.Max(normalized?.06:3,Math.Min(normalized?.12:30,peak*.4));
                    if(s.Brake<.08&&s.Throttle>=.85&&straight&&s.DistanceM-samples[begin].DistanceM>50)
                    {
                        if(clear<0)clear=i;
                        if(s.DistanceM-samples[clear].DistanceM>=25){raw.Add(new Zone {Start=Math.Max(samples[0].DistanceM,samples[begin].DistanceM-100),End=s.DistanceM});begin=candidate=clear=-1;}
                    }
                    else clear=-1;
                }
            }
            foreach(var z in raw){if(zones.Count>0&&z.Start<=zones.Last().End+25)zones.Last().End=z.End;else zones.Add(z);}
            for(int i=0;i<zones.Count;i++){var z=zones[i];z.Id=i;z.Name="감속·회전 구간 "+(i+1);z.First=z.Start+(z.End-z.Start)/3;z.Second=z.Start+(z.End-z.Start)*2/3;}
        }
        LiveReferenceView AnalyzeSpatial(Zone zone,ReferenceLap own,ComparisonResult result,ReferenceSample first,ReferenceSample last)
        {
            var a=ReferenceComparison.SampleAtDistance(reference,zone.Start);var b=ReferenceComparison.SampleAtDistance(reference,zone.End);
            if(a==null||b==null||!Finite(a.SpeedKph)||!Finite(b.SpeedKph))return null;
            double entry=first.SpeedKph-a.SpeedKph,exit=last.SpeedKph-b.SpeedKph;
            var v=new LiveReferenceView {Ready=true,FeedbackId=lap+":"+zone.Id,CornerName=zone.Name,StartDistanceM=zone.Start,EndDistanceM=zone.End,UsedObservedGraphicsTiming=activeObservedTiming,
                Heading=zone.Name+" · 위치·입력 비교",Observation="진입 "+Number(entry)+" / 탈출 "+Number(exit)+" km/h 차이.",
                NextAction="진입 속도를 유지하며 제동 해제와 출구를 다시 확인하세요.",Principle="같은 위치의 입력 차이도 속도와 노면 조건을 함께 봐야 해요.",
                Estimate="기준 표본 시각이 없어 초 단위 손실을 계산하지 않습니다. 코스 거리 탐색 정렬이며 조향 각도와 정규화 입력은 직접 비교하지 않습니다. "+string.Join(" ",warnings.Take(4).ToArray())};
            if(activeObservedTiming)v.Estimate+=" 원본 위치·입력 동기화는 미확인입니다. 관측된 갱신 간격에 보수적인 판단 여유를 추가했습니다.";
            var brake=result.BrakeZones.FirstOrDefault();
            double tolerance=Math.Max(15,Math.Max(a.SpeedKph,first.SpeedKph)/3.6*(activeDistanceAge+.1));
            if(brake!=null)
            {
                tolerance=Math.Max(tolerance,brake.EstimatedUncertaintyM);
                if(Math.Abs(brake.StartDeltaM)>tolerance)v.Observation+=" 제동 시작은 약 "+Number(Math.Abs(brake.StartDeltaM))+" m "+(brake.StartDeltaM>0?"뒤.":"앞.");
                if(Finite(brake.ReleaseDeltaM)&&Math.Abs(brake.ReleaseDeltaM)>tolerance)v.Observation+=" 해제는 약 "+Number(Math.Abs(brake.ReleaseDeltaM))+" m "+(brake.ReleaseDeltaM>0?"뒤.":"앞.");
            }
            if(Math.Abs(entry)>Math.Max(5,a.SpeedKph*.04))
            {v.NextAction="제동과 조향을 함께 바꾸지 말고 진입 속도부터 맞춰 보세요.";v.Principle="진입 속도가 다르면 필요한 감속량과 회전 여유도 달라져요.";}
            else if(brake!=null&&brake.ReleaseDeltaM>tolerance&&exit< -4)
            {v.NextAction="진입 속도는 유지하고 브레이크 해제를 조금 앞당겨 부드럽게 시험하세요.";v.Principle="제동과 회전은 접지를 나눠 쓰고, 해제는 차 균형에도 영향을 줘요.";}
            ObserveLine(v,result,own,zone);return v;
        }
    }
}
