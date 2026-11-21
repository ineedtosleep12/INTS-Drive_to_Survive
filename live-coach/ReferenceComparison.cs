using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IntsLiveCoach
{
    public sealed class ComparisonPoint
    {
        public double DistanceM,ReferenceTime,OwnTime,DeltaSeconds,SpeedDeltaKph=double.NaN,LineOffsetM=double.NaN,PathLateralDeltaM=double.NaN;
        public ReferenceSample Reference,Own;
    }
    public sealed class ComparisonSection
    {
        public string Name;
        public double StartDistanceM,EndDistanceM,TimeLossSeconds,ExitSpeedDeltaKph=double.NaN,MeanLineOffsetM=double.NaN;
    }
    public sealed class BrakeComparison
    {
        public string Name;
        public double ReferenceStartM,OwnStartM,StartDeltaM,ReferenceReleaseM=double.NaN,OwnReleaseM=double.NaN,ReleaseDeltaM=double.NaN;
        public double EstimatedUncertaintyM;
        public bool StartDifferenceResolved,ReleaseDifferenceResolved;
    }
    public sealed class ComparisonResult
    {
        public bool Comparable,HasLineComparison,HasLateralComparison,HasTimeComparison;
        public string Reason="비교 데이터가 없습니다.";
        public string[] Warnings=new string[0];
        public double LapDeltaSeconds=double.NaN;
        public ComparisonPoint[] Points=new ComparisonPoint[0];
        public ComparisonSection[] Sections=new ComparisonSection[0];
        public BrakeComparison[] BrakeZones=new BrakeComparison[0];
    }

    public static class ReferenceComparison
    {
        static bool Finite(double v) {return ReferenceLap.Finite(v);}
        static double Mix(double a,double b,double t) {return Finite(a)&&Finite(b)?a+(b-a)*t:double.NaN;}
        static bool Same(string a,string b) {return ReferenceLap.Key(a)==ReferenceLap.Key(b);}
        static bool Known(string s) {return !ReferenceLap.Unknown(s);}
        static string Metres(double x) {return x.ToString("F0",CultureInfo.InvariantCulture);}
        static bool CourseDistance(string basis)
        {
            switch(ReferenceLap.Key(basis))
            {
                case ReferenceLap.LmuDistanceBasis:case ReferenceLap.LmuMotecDistanceBasis:case "acc-normalized-track-position-m":case "ac-normalized-track-position-m":
                case "ams2-current-lap-distance-m":case "synthetic-course-distance-m":return true;
                default:return false;
            }
        }
        static bool MetreCoordinates(string frame)
        {
            switch(ReferenceLap.Key(frame))
            {
                case ReferenceLap.LmuCoordinateFrame:case "acc-world-xz-m":case "ac-world-xz-m":case "ams2-world-xz-m":case "synthetic-world-xz-m":return true;
                default:return false;
            }
        }
        static bool Pair(string a,string b,string first,string second)
        {return (Same(a,first)&&Same(b,second))||(Same(a,second)&&Same(b,first));}
        static bool MetreLateral(string basis)
        {return Same(basis,ReferenceLap.LmuLateralBasis)||Same(basis,ReferenceLap.LmuMotecLateralBasis)||Same(basis,"synthetic-path-lateral-m");}
        static bool LmuMappingExtent(ReferenceLap reference,ReferenceLap own,string nativeBasis=ReferenceLap.LmuDistanceBasis)
        {
            if(Finite(reference.TrackLengthM)&&Finite(own.TrackLengthM))
                return Math.Abs(reference.TrackLengthM-own.TrackLengthM)<=Math.Max(5,reference.TrackLengthM*.001);
            var native=Same(reference.DistanceBasis,nativeBasis)?reference:own;
            var motec=native==reference?own:reference;
            if(!Finite(native.TrackLengthM)||Finite(motec.TrackLengthM)||motec.Complete)return false;
            double first=motec.Samples[0].DistanceM,last=motec.Samples.Last().DistanceM;
            // Extent can qualify an explicitly requested exploration, never establish equivalence or synthesize a track length.
            return first<50&&last<=native.TrackLengthM+5&&native.TrackLengthM-last<=50&&last-first>=native.TrackLengthM*.98;
        }
        internal static bool HasRecordedCrossingExtent(ReferenceLap reference)
        {
            string extent;
            return reference!=null && (reference.DistanceBasis==ReferenceLap.LmuDistanceBasis || reference.DistanceBasis==ReferenceLap.LmuAnchoredDistanceBasis) &&
                reference.Conditions.TryGetValue("sourceLapExtent",out extent) && extent=="recorded-crossing-to-crossing";
        }
        internal static bool IsGameSelectedLayout(ReferenceLap reference)
        {
            string selection;
            return reference!=null && reference.DistanceBasis==ReferenceLap.LmuDistanceBasis &&
                reference.Conditions.TryGetValue("sourceLayoutSelection",out selection) && selection=="game-reported";
        }
        internal static bool NeedsNativePositionCheck(ReferenceLap reference)
        {
            return HasRecordedCrossingExtent(reference) || IsGameSelectedLayout(reference);
        }
        internal static bool NearbyWorldPositions(ReferenceSample a,ReferenceSample b)
        {
            if(a==null || b==null || !Finite(a.X) || !Finite(a.Z) || !Finite(b.X) || !Finite(b.Z))return false;
            // Coarse timing-line/layout check only. This tolerance is not a driving-line target.
            double dx=a.X-b.X,dz=a.Z-b.Z;
            return dx*dx+dz*dz<=35*35;
        }
        // A route can leave and rejoin between the seven coarse anchors. Wait for
        // measured disagreement over distance so an isolated scoring packet, missing
        // coordinate or late sample does not invalidate an otherwise useful lap.
        internal static bool SustainedLayoutMismatch(ReferenceSample expected,ReferenceSample actual,ref double firstMismatchM)
        {
            if(expected==null || actual==null || !Finite(expected.X) || !Finite(expected.Z) ||
                !Finite(actual.X) || !Finite(actual.Z) || NearbyWorldPositions(expected,actual))
            {
                firstMismatchM=double.NaN;
                return false;
            }
            if(!Finite(firstMismatchM) || actual.DistanceM<firstMismatchM)firstMismatchM=actual.DistanceM;
            return actual.DistanceM-firstMismatchM>=25;
        }
        internal static bool MatchesNativeWorldAnchors(ReferenceLap reference,ReferenceLap own,double from,double to)
        {
            if(reference.CoordinateFrame!=ReferenceLap.LmuCoordinateFrame || own.CoordinateFrame!=ReferenceLap.LmuCoordinateFrame)return false;
            if(IsGameSelectedLayout(reference))
            {
                double firstMismatchM=double.NaN;
                foreach(var sample in own.Samples)
                {
                    if(sample.DistanceM<from || sample.DistanceM>to)continue;
                    if(SustainedLayoutMismatch(SampleAtDistance(reference,sample.DistanceM),sample,ref firstMismatchM))return false;
                }
            }
            int nearby=0;
            for(int i=0;i<7;i++)
            {
                double distance=from+(to-from)*i/6;
                if(NearbyWorldPositions(SampleAtDistance(reference,distance),SampleAtDistance(own,distance)))nearby++;
            }
            return nearby>=5;
        }
        public static ComparisonResult Compare(ReferenceLap reference,ReferenceLap own,bool allowVersionMismatch=false,bool allowLmuMotecAlignment=false)
        {
            var output=new ComparisonResult();var warnings=new List<string>();
            if(reference==null||own==null){output.Reason="기준 주행과 내 주행을 모두 불러오세요.";return output;}
            try{reference.Validate();own.Validate();}catch(FormatException ex){output.Reason="기록 형식 오류: "+ex.Message;return output;}
            if(!Known(reference.Game)||!Known(own.Game)||!Known(reference.Car)||!Known(own.Car)||!Known(reference.Track)||!Known(own.Track))
            {output.Reason="게임·차량·트랙 식별자가 미확인이라 기록을 정렬하지 않습니다.";return output;}
            if(!Same(reference.Game,own.Game)){output.Reason="다른 게임의 기록은 비교하지 않습니다.";return output;}
            if(IsGameSelectedLayout(reference))
            {
                string nativeCar,nativeTrack;
                if(!own.Conditions.TryGetValue("sourceCarLabel",out nativeCar))nativeCar=own.Car;
                if(!own.Conditions.TryGetValue("sourceTrackLabel",out nativeTrack))nativeTrack=own.Track;
                if(nativeCar!=reference.Car || nativeTrack!=reference.Track || own.GameVersion!=reference.GameVersion ||
                   own.TrackLengthM!=reference.TrackLengthM || own.DistanceBasis!=ReferenceLap.LmuDistanceBasis)
                {output.Reason="내 레이아웃 기준과 게임의 차량·코스·거리·빌드가 달라 비교를 기다립니다.";return output;}
            }
            if(!Same(reference.Car,own.Car)){output.Reason="차량이 다릅니다. 검증된 같은 차량 식별자가 필요합니다.";return output;}
            if(!Same(reference.Track,own.Track)){output.Reason="트랙이 다릅니다. 같은 게임의 같은 코스가 필요합니다.";return output;}
            if(Known(reference.Layout)&&Known(own.Layout)&&!Same(reference.Layout,own.Layout)){output.Reason="트랙 레이아웃이 다릅니다.";return output;}
            if(!Known(reference.Layout)||!Known(own.Layout))warnings.Add("레이아웃 정보가 미확인입니다. 같은 코스 변형인지 확인하세요.");
            bool lmuMotecPair=Same(reference.Game,"LMU")&&Pair(reference.DistanceBasis,own.DistanceBasis,ReferenceLap.LmuDistanceBasis,ReferenceLap.LmuMotecDistanceBasis);
            bool mappedLmuMotec=allowLmuMotecAlignment&&lmuMotecPair&&LmuMappingExtent(reference,own);
            bool anchoredPair=Same(reference.Game,"LMU") && reference.DistanceBasis==ReferenceLap.LmuAnchoredDistanceBasis &&
                own.DistanceBasis==ReferenceLap.LmuDistanceBasis;
            bool mappedAnchored=anchoredPair && allowLmuMotecAlignment && NeedsNativePositionCheck(reference) &&
                reference.CoordinateFrame==ReferenceLap.LmuCoordinateFrame && LmuMappingExtent(reference,own);
            if(anchoredPair && !mappedAnchored)
            {output.Reason="후지 기록의 거리는 계측 지점 사이를 속도로 보간했습니다. 전체 코스 범위와 좌표 채널을 확인한 뒤 ‘기록 거리 탐색 정렬’을 선택하세요.";return output;}
            bool accReplayPair=Same(reference.Game,"ACC")&&Pair(reference.DistanceBasis,own.DistanceBasis,CapturedLap.AccDistanceBasis,AccReplayAlignment.SourceDistanceBasis);
            bool mappedAccReplay=allowLmuMotecAlignment&&accReplayPair&&LmuMappingExtent(reference,own,CapturedLap.AccDistanceBasis);
            bool sameReplay=Same(reference.Game,"ACC")&&Same(reference.DistanceBasis,AccReplayAlignment.SourceDistanceBasis)&&Same(reference.DistanceBasis,own.DistanceBasis);
            if((!Same(reference.DistanceBasis,own.DistanceBasis)||!CourseDistance(reference.DistanceBasis))&&!mappedLmuMotec&&!mappedAnchored&&!mappedAccReplay&&!sameReplay)
            {output.Reason=accReplayPair?"ACCReplay 거리와 실시간 위치의 대응은 미검증입니다. 코스 98% 이상 범위를 확인한 뒤 탐색 정렬을 선택하세요.":lmuMotecPair?"MoTeC와 공유 메모리의 거리 채널 매핑은 미검증입니다. 트랙 길이가 일치하거나 MoTeC 구간이 공유 메모리 코스의 98% 이상을 포함하는 경우에만 탐색 비교를 명시적으로 선택할 수 있습니다.":"공통 코스 거리 기준이 없습니다. 주행한 누적 거리를 코스 위치로 간주하지 않습니다.";return output;}
            if(mappedAccReplay)warnings.Add("ACCReplay spline 거리와 실시간 코스 위치는 탐색 정렬입니다. 좌표는 별도의 전체 랩 대응 확인 전까지 비교하지 않습니다. 정확한 미터 교정값이 아닙니다.");
            if(mappedAnchored)warnings.Add("기록 거리 탐색 정렬 · 게임 계측 지점 사이의 속도 적분이 포함된 거리입니다. 같은 지점의 위치·제동·구간 시간에 오차가 있을 수 있습니다.");
            bool hasTime=reference.HasTime&&own.HasTime;
            if(!hasTime)warnings.Add("기준 표본 시각 미제공: 위치·속도·입력만 비교하며 초 단위 손실은 계산하지 않습니다.");
            if(mappedLmuMotec)warnings.Add("MoTeC와 공유 메모리 거리·횡위치 채널의 매핑은 실제 주행 검증 전입니다. 탐색 비교이며 제동 위치·시간 손실·횡위치 차이를 검증된 운전 교정값으로 사용할 수 없습니다.");
            if(mappedLmuMotec&&(!Finite(reference.TrackLengthM)||!Finite(own.TrackLengthM)))warnings.Add("MoTeC 원본의 전체 트랙 길이는 미확인입니다. 기록된 구간 범위만 확인한 탐색 비교이며 전체 랩 시간 차이는 제공하지 않습니다.");
            if(Finite(reference.TrackLengthM)&&Finite(own.TrackLengthM)&&Math.Abs(reference.TrackLengthM-own.TrackLengthM)>Math.Max(5,reference.TrackLengthM*.001))
            {output.Reason="기록의 트랙 길이가 달라 거리 비교를 보류합니다.";return output;}
            if(Known(reference.GameVersion)&&Known(own.GameVersion)&&!Same(reference.GameVersion,own.GameVersion))
            {
                if(!allowVersionMismatch){output.Reason="게임/물리 버전이 다릅니다. 조건 차이를 확인한 뒤 탐색 비교를 명시적으로 선택해야 합니다.";return output;}
                warnings.Add("버전이 다른 기록의 탐색 비교입니다. 시간 차이는 운전자 실력 차이로 해석할 수 없습니다.");
            }
            else if(!Known(reference.GameVersion)||!Known(own.GameVersion))warnings.Add("게임/물리 버전이 미확인입니다. 패치·BoP 차이가 있을 수 있습니다.");
            if(Same(reference.Validity,"invalid")||Same(own.Validity,"invalid"))
            {output.Reason="무효로 확인된 랩은 성능 비교에서 제외합니다.";return output;}
            if(!Same(reference.Validity,"valid")||!Same(own.Validity,"valid"))warnings.Add("랩 유효성 미확인: 관측 비교이며 유효 핫랩 성능의 검증이 아닙니다.");
            if(!reference.Complete||!own.Complete)warnings.Add("부분 기록의 겹치는 구간만 비교합니다. 전체 랩 시간 차이는 표시하지 않습니다.");
            var keys=new HashSet<string>(reference.Conditions.Keys,StringComparer.OrdinalIgnoreCase);keys.UnionWith(own.Conditions.Keys);
            if(keys.Count==0)warnings.Add("연료·타이어·날씨·셋업 조건이 없습니다. 원인을 확정할 수 없습니다.");
            foreach(string key in keys.OrderBy(x=>x))
            {
                string a,b;reference.Conditions.TryGetValue(key,out a);own.Conditions.TryGetValue(key,out b);
                // Original labels/provenance are not physical comparison conditions.
                if(key.StartsWith("source",StringComparison.OrdinalIgnoreCase))continue;
                if(!Known(a)||!Known(b))warnings.Add(key+": 조건 미확인");
                else if(!Same(a,b))warnings.Add(key+": 기준 "+a+" / 내 주행 "+b);
            }
            warnings.Add("거리 정렬과 제동 위치는 표본 간 보간을 사용한 근사입니다. 시간 손실과 입력 차이만으로 원인을 확정하지 않습니다.");
            double from=Math.Max(reference.Samples[0].DistanceM,own.Samples[0].DistanceM);
            double to=Math.Min(reference.Samples.Last().DistanceM,own.Samples.Last().DistanceM);
            if(to-from<30){output.Reason="겹치는 코스 구간이 30 m보다 짧습니다.";return output;}
            if(NeedsNativePositionCheck(reference))
            {
                double coverage=reference.Samples.Last().DistanceM-reference.Samples[0].DistanceM;
                if(Finite(own.TrackLengthM) && Math.Abs(own.TrackLengthM-coverage)>Math.Max(60,own.TrackLengthM*.015))
                {output.Reason="기준 랩이 달린 거리와 현재 레이아웃의 길이가 다릅니다.";return output;}
                if(!MatchesNativeWorldAnchors(reference,own,from,to))
                {output.Reason="같은 코스 거리에서 두 차량의 실제 위치를 대응시키지 못했습니다. 레이아웃과 계측선 위치를 확인하세요.";return output;}
            }
            ReferenceSample r0=SampleAtDistance(reference,from),o0=SampleAtDistance(own,from);
            if(r0==null||o0==null){output.Reason="공통 시작 지점의 시간 표본이 없습니다.";return output;}
            double step=Math.Max(5,(to-from)/12000);var points=new List<ComparisonPoint>();
            bool line=Known(reference.CoordinateFrame)&&Same(reference.CoordinateFrame,own.CoordinateFrame)&&MetreCoordinates(reference.CoordinateFrame);
            bool lateral=(Same(reference.LateralPositionBasis,own.LateralPositionBasis)&&MetreLateral(reference.LateralPositionBasis))||
                (mappedLmuMotec&&Pair(reference.LateralPositionBasis,own.LateralPositionBasis,ReferenceLap.LmuLateralBasis,ReferenceLap.LmuMotecLateralBasis));
            if(!line)warnings.Add("월드 좌표계의 일치가 확인되지 않아 지도 위 주행선 차이는 계산하지 않습니다.");
            for(double s=from;s<to;s+=step)AddPoint(points,reference,own,s,r0.Time,o0.Time,line,lateral);
            AddPoint(points,reference,own,to,r0.Time,o0.Time,line,lateral);
            if(points.Count<4){output.Reason="데이터 공백이 커서 비교할 표본이 부족합니다.";return output;}
            bool sparse=points.Count<(int)((to-from)/step*.95);
            if(sparse)warnings.Add("긴 표본 공백은 보간하지 않았습니다. 그래프의 끊긴 구간은 비교에서 제외됩니다.");
            if(line&&points.Count(p=>Finite(p.LineOffsetM))<points.Count*.9)
            {line=false;foreach(var p in points)p.LineOffsetM=double.NaN;warnings.Add("좌표 누락·불일치가 많아 주행선 차이를 보류했습니다.");}
            if(lateral&&points.Count(p=>Finite(p.PathLateralDeltaM))<points.Count*.9)
            {lateral=false;foreach(var p in points)p.PathLateralDeltaM=double.NaN;warnings.Add("횡위치 표본 누락이 많아 횡위치 차이 비교를 보류했습니다.");}
            if(lateral)warnings.Add("횡위치 차이는 코스 경로에 대한 채널 값의 차이입니다. 월드 좌표 주행선을 복원하지 않으며 부호를 운전자의 좌/우로 단정하지 않습니다.");
            var sections=new List<ComparisonSection>();
            // Non-overlapping 100 m windows avoid repeatedly summing cumulative deltas.
            for(double start=from;start<to-10;start+=100)
            {
                double end=Math.Min(to,start+100);
                ComparisonPoint a=MakePoint(reference,own,start,r0.Time,o0.Time,line),b=MakePoint(reference,own,end,r0.Time,o0.Time,line);
                if(a==null||b==null||HasLargeGap(reference,start,end)||HasLargeGap(own,start,end))continue;
                double[] offsets=points.Where(p=>p.DistanceM>=start&&p.DistanceM<=end&&Finite(p.LineOffsetM)).Select(p=>Math.Abs(p.LineOffsetM)).ToArray();
                sections.Add(new ComparisonSection {Name=Metres(start)+"–"+Metres(end)+" m",StartDistanceM=start,EndDistanceM=end,
                    TimeLossSeconds=b.DeltaSeconds-a.DeltaSeconds,ExitSpeedDeltaKph=b.SpeedDeltaKph,MeanLineOffsetM=offsets.Length>0?offsets.Average():double.NaN});
            }
            output.Comparable=true;output.HasTimeComparison=hasTime;output.Reason=hasTime?"같은 코스 위치에서 기록된 입력·시간을 비교합니다. +시간은 내 주행이 더 오래 걸렸다는 뜻입니다.":"코스 위치별 입력·속도를 비교합니다. 표본 시각이 없어 시간 손실은 제공하지 않습니다.";
            output.Points=points.ToArray();output.Sections=sections.OrderByDescending(s=>s.TimeLossSeconds).ToArray();output.HasLineComparison=line;output.HasLateralComparison=lateral;
            output.BrakeZones=CompareBrakes(reference,own,from,to);output.Warnings=warnings.Distinct().ToArray();
            // Do not extrapolate an unrecorded finish/start, even for a declared complete lap.
            if(reference.Complete&&own.Complete&&Finite(reference.TrackLengthM)&&from<=.01&&Math.Abs(to-reference.TrackLengthM)<=.01&&
                Math.Abs(reference.TrackLengthM-own.TrackLengthM)<=.01&&!sparse)
                output.LapDeltaSeconds=points.Last().DeltaSeconds;
            return output;
        }
        static bool HasLargeGap(ReferenceLap lap,double from,double to)
        {for(int i=1;i<lap.Samples.Length;i++)if(lap.Samples[i].DistanceM>=from&&lap.Samples[i-1].DistanceM<=to&&(lap.Samples[i].Time-lap.Samples[i-1].Time>1||lap.Samples[i].DistanceM-lap.Samples[i-1].DistanceM>100))return true;return false;}
        static void AddPoint(List<ComparisonPoint> output,ReferenceLap r,ReferenceLap o,double s,double rt,double ot,bool line,bool lateral)
        {var p=MakePoint(r,o,s,rt,ot,line,lateral);if(p!=null)output.Add(p);}
        static ComparisonPoint MakePoint(ReferenceLap r,ReferenceLap o,double s,double rt,double ot,bool line,bool lateral=false)
        {
            ReferenceSample a=SampleAtDistance(r,s),b=SampleAtDistance(o,s);if(a==null||b==null)return null;
            var p=new ComparisonPoint {DistanceM=s,ReferenceTime=a.Time-rt,OwnTime=b.Time-ot,DeltaSeconds=(b.Time-ot)-(a.Time-rt),Reference=a,Own=b,
                SpeedDeltaKph=Finite(a.SpeedKph)&&Finite(b.SpeedKph)?b.SpeedKph-a.SpeedKph:double.NaN};
            if(lateral&&Finite(a.PathLateralM)&&Finite(b.PathLateralM))p.PathLateralDeltaM=b.PathLateralM-a.PathLateralM;
            if(line&&Finite(a.X)&&Finite(a.Z)&&Finite(b.X)&&Finite(b.Z))
            {
                var left=SampleAtDistance(r,Math.Max(r.Samples[0].DistanceM,s-5));var right=SampleAtDistance(r,Math.Min(r.Samples.Last().DistanceM,s+5));
                if(left!=null&&right!=null&&Finite(left.X)&&Finite(right.X))
                {
                    double dx=right.X-left.X,dz=right.Z-left.Z,length=Math.Sqrt(dx*dx+dz*dz),ex=b.X-a.X,ez=b.Z-a.Z;
                    if(length>.1&&Math.Sqrt(ex*ex+ez*ez)<75)p.LineOffsetM=(dx*ez-dz*ex)/length;
                }
            }
            return p;
        }
        public static ReferenceSample SampleAtDistance(ReferenceLap lap,double s)
        {
            if(lap==null||lap.Samples==null||lap.Samples.Length<2||!Finite(s)||s<lap.Samples[0].DistanceM||s>lap.Samples.Last().DistanceM)return null;
            var samples=lap.Samples;int lo=0,hi=samples.Length-1;
            while(lo<hi){int middle=(lo+hi)/2;if(samples[middle].DistanceM<s)lo=middle+1;else hi=middle;}
            if(lo==0||samples[lo].DistanceM==s)return samples[lo].Copy();
            var a=samples[lo-1];var b=samples[lo];double distance=b.DistanceM-a.DistanceM;
            if(distance<=0||distance>100||b.Time-a.Time>1)return null;
            double t=(s-a.DistanceM)/distance;
            return new ReferenceSample {DistanceM=s,Time=Mix(a.Time,b.Time,t),SpeedKph=Mix(a.SpeedKph,b.SpeedKph,t),Brake=Mix(a.Brake,b.Brake,t),Throttle=Mix(a.Throttle,b.Throttle,t),Steering=Mix(a.Steering,b.Steering,t),SteeringDegrees=Mix(a.SteeringDegrees,b.SteeringDegrees,t),X=Mix(a.X,b.X,t),Z=Mix(a.Z,b.Z,t),PathLateralM=Mix(a.PathLateralM,b.PathLateralM,t)};
        }
        sealed class BrakeEvent {public double Start,Release=double.NaN,Uncertainty;}
        static List<BrakeEvent> Brakes(ReferenceLap lap)
        {
            var events=new List<BrakeEvent>();BrakeEvent active=null;int candidate=-1,release=-1;
            for(int i=0;i<lap.Samples.Length;i++)
            {
                var s=lap.Samples[i];
                if(!Finite(s.Brake)||(i>0&&(s.Time-lap.Samples[i-1].Time>1||s.DistanceM-lap.Samples[i-1].DistanceM>100))){active=null;candidate=release=-1;continue;}
                if(active==null)
                {
                    if(s.Brake>=.15){if(candidate<0)candidate=i;if(lap.HasTime?s.Time-lap.Samples[candidate].Time>=.1:(i>candidate&&s.DistanceM-lap.Samples[candidate].DistanceM>=5)){active=new BrakeEvent {Start=lap.Samples[candidate].DistanceM,Uncertainty=PositionResolution(lap,candidate)};release=-1;}}
                    else candidate=-1;
                }
                else if(s.Brake<=.08)
                {
                    if(release<0)release=i;
                    if(lap.HasTime?s.Time-lap.Samples[release].Time>=.15:(i>release&&s.DistanceM-lap.Samples[release].DistanceM>=5))
                    {active.Release=lap.Samples[release].DistanceM;active.Uncertainty=Math.Max(active.Uncertainty,PositionResolution(lap,release));events.Add(active);active=null;candidate=release=-1;}
                }
                else release=-1;
            }
            return events;
        }
        static double PositionResolution(ReferenceLap lap,int index)
        {
            var s=lap.Samples[index];int left=index,right=index;
            while(left>0&&lap.Samples[left].DistanceM>=s.DistanceM)left--;
            while(right<lap.Samples.Length-1&&lap.Samples[right].DistanceM<=s.DistanceM)right++;
            return Math.Max(3,(lap.Samples[right].DistanceM-lap.Samples[left].DistanceM)/2)+(Finite(s.SpeedKph)?s.SpeedKph/3.6*.05:5);
        }
        static BrakeComparison[] CompareBrakes(ReferenceLap reference,ReferenceLap own,double from,double to)
        {
            var output=new List<BrakeComparison>();var a=Brakes(reference).Where(e=>e.Start>=from&&e.Release<=to).ToArray();var b=Brakes(own).Where(e=>e.Start>=from&&e.Release<=to).ToArray();var used=new HashSet<BrakeEvent>();
            foreach(var item in a)
            {
                var matches=b.Where(x=>!used.Contains(x)&&Math.Abs(x.Start-item.Start)<=100&&Math.Min(x.Release,item.Release)>=Math.Max(x.Start,item.Start)).OrderBy(x=>Math.Abs(x.Start-item.Start)).ToArray();
                if(matches.Length==0||(matches.Length>1&&Math.Abs(matches[1].Start-item.Start)-Math.Abs(matches[0].Start-item.Start)<20))continue;
                var match=matches[0];used.Add(match);double uncertainty=item.Uncertainty+match.Uncertainty+5;
                output.Add(new BrakeComparison {Name="제동 구간 "+Metres(item.Start)+" m",ReferenceStartM=item.Start,OwnStartM=match.Start,StartDeltaM=match.Start-item.Start,ReferenceReleaseM=item.Release,OwnReleaseM=match.Release,ReleaseDeltaM=match.Release-item.Release,
                    EstimatedUncertaintyM=uncertainty,StartDifferenceResolved=Math.Abs(match.Start-item.Start)>uncertainty,ReleaseDifferenceResolved=Math.Abs(match.Release-item.Release)>uncertainty});
            }
            return output.ToArray();
        }
    }
}
