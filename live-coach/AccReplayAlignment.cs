using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IntsLiveCoach
{
    // Establish only a discrete axis correspondence from an actually captured,
    // complete native lap. Never fit a translation, arbitrary rotation or scale:
    // fitting a driver's line would conceal the very difference being compared.
    public static class AccReplayAlignment
    {
        public const string SourceDistanceBasis="acc-replay-spline-distance-m";
        public const string SourceCoordinateFrame="acc-replay-map-xy-m";
        sealed class Pair {public ReferenceSample Source,Native;}
        sealed class Candidate {public int Code;public double Rms;public double[] Errors;}
        static bool Same(string a,string b){return ReferenceLap.Key(a)==ReferenceLap.Key(b);}
        static bool Finite(double n){return ReferenceLap.Finite(n);}
        static bool Position(ReferenceSample s){return s!=null&&Finite(s.X)&&Finite(s.Z);}
        static void Transform(ReferenceSample source,int code,out double x,out double z)
        {double a=(code&4)==0?source.X:source.Z,b=(code&4)==0?source.Z:source.X;x=(code&1)==0?a:-a;z=(code&2)==0?b:-b;}
        public static bool CanTry(ReferenceLap source,ReferenceLap native)
        {return source!=null&&native!=null&&Same(source.Game,"ACC")&&Same(native.Game,"ACC")&&source.DistanceBasis==SourceDistanceBasis&&source.CoordinateFrame==SourceCoordinateFrame&&native.DistanceBasis==CapturedLap.AccDistanceBasis&&native.CoordinateFrame==CapturedLap.AccCoordinateFrame&&native.Complete&&native.Validity=="valid";}
        public static bool TryAlign(ReferenceLap source,ReferenceLap native,bool allowVersionMismatch,bool allowDistanceAlignment,out ReferenceLap aligned,out string message)
        {
            aligned=null;message="ACCReplay 좌표와 실제 ACC 유효 완주 랩이 필요합니다.";
            if(!CanTry(source,native))return false;
            if(!allowDistanceAlignment){message="먼저 ACCReplay ↔ 실시간 탐색 정렬을 선택하세요. 거리 채널의 대응은 실제 주행 대조 전입니다.";return false;}
            var comparison=ReferenceComparison.Compare(source,native,allowVersionMismatch,allowDistanceAlignment);
            if(!comparison.Comparable){message=comparison.Reason;return false;}
            double length=native.TrackLengthM,from=Math.Max(source.Samples[0].DistanceM,native.Samples[0].DistanceM),to=Math.Min(source.Samples.Last().DistanceM,native.Samples.Last().DistanceM);
            if(!Finite(length)||from>50||to-from<length*.98){message="같은 코스의 98% 이상을 포함하는 실제 완주 좌표가 필요합니다.";return false;}
            var pairs=new List<Pair>();int requested=0;
            for(double at=from;at<=to;at+=50)
            {
                requested++;var a=ReferenceComparison.SampleAtDistance(source,at);var b=ReferenceComparison.SampleAtDistance(native,at);
                if(Position(a)&&Position(b))pairs.Add(new Pair {Source=a,Native=b});
            }
            if(pairs.Count<60||pairs.Count<requested*.95){message="전 코스의 좌표 비교 표본이 부족합니다. 공백이 없는 유효 랩을 다시 기록하세요.";return false;}
            var candidates=new List<Candidate>();
            for(int code=0;code<8;code++)
            {
                var errors=pairs.Select(p=>{double x,z;Transform(p.Source,code,out x,out z);return Math.Sqrt(Math.Pow(x-p.Native.X,2)+Math.Pow(z-p.Native.Z,2));}).ToArray();
                candidates.Add(new Candidate {Code=code,Rms=Math.Sqrt(errors.Average(e=>e*e)),Errors=errors});
            }
            candidates=candidates.OrderBy(c=>c.Rms).ToList();var best=candidates[0];
            if(!Finite(best.Rms)||best.Rms>12||best.Errors.Max()>35||best.Errors.Count(e=>e<=20)<pairs.Count*.95||candidates[1].Rms<Math.Max(60,best.Rms*4))
            {message="좌표 축·원점·미터 척도의 대응을 확인하지 못했습니다. 주행선을 이동하거나 늘려 맞추지 않습니다. 위치·입력 비교만 유지합니다.";return false;}
            aligned=ReferenceLap.LoadJson(source.ToJson());
            foreach(var s in aligned.Samples)if(Position(s)){double x,z;Transform(s,best.Code,out x,out z);s.X=x;s.Z=z;}
            aligned.CoordinateFrame=CapturedLap.AccCoordinateFrame;
            aligned.Conditions["coordinateAlignment"]="Inferred discrete axis correspondence against a complete valid native ACC lap at matching course positions. Unit scale, zero translation, no fitted rotation. Not SDK documentation or independently certified accuracy.";
            aligned.Conditions["coordinateAlignmentTransform"]="signed-axis-permutation="+best.Code.ToString(CultureInfo.InvariantCulture)+"; RMS="+best.Rms.ToString("F3",CultureInfo.InvariantCulture)+" m; matched="+pairs.Count.ToString(CultureInfo.InvariantCulture);
            aligned.Conditions["coordinateAlignmentNative"]="Track="+native.Track+"; car="+native.Car+"; version="+native.GameVersion;
            aligned.Validate();
            message="내 완주 랩 전체로 좌표 축과 척도의 대응을 확인했습니다. 평균 제곱근 간격 "+best.Rms.ToString("F1",CultureInfo.InvariantCulture)+" m. 이는 주행선 차이를 포함하며 정렬 정확도의 보증이 아닙니다.";
            return true;
        }
    }
}
