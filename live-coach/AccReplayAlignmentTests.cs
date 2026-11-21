using System;
using System.Linq;

namespace IntsLiveCoach
{
    public static class AccReplayAlignmentTests
    {
        static int count;
        static void Check(bool value,string message){count++;if(!value)throw new Exception("ACCReplay alignment: "+message);}
        static ReferenceLap Native()
        {
            var lap=new ReferenceLap {Game="ACC",Car="SYNTHETIC",Track="SYNTHETIC",Layout="GP",GameVersion="synthetic-v1",TrackLengthM=5200,DistanceBasis=CapturedLap.AccDistanceBasis,CoordinateFrame=CapturedLap.AccCoordinateFrame,Complete=true,Validity="valid",Author="synthetic test"};
            lap.Samples=Enumerable.Range(0,521).Select(i=>{double a=i/520.0*Math.PI*2;return new ReferenceSample {Time=i*.25,DistanceM=i*10,SpeedKph=144,Brake=0,Throttle=1,Steering=0,X=600*Math.Cos(a)+80*Math.Sin(3*a)+100,Z=900*Math.Sin(a)+50*Math.Cos(2*a)-150};}).ToArray();
            return lap;
        }
        static ReferenceLap Source(ReferenceLap native)
        {
            var source=ReferenceLap.LoadJson(native.ToJson());source.TrackLengthM=double.NaN;source.Complete=false;source.Validity="unknown";source.TimeBasis="unavailable-distance-only";source.DistanceBasis=AccReplayAlignment.SourceDistanceBasis;source.CoordinateFrame=AccReplayAlignment.SourceCoordinateFrame;
            foreach(var s in source.Samples){s.Time=double.NaN;s.Z=-s.Z;}
            return source;
        }
        public static int Run()
        {
            count=0;var own=Native();var source=Source(own);foreach(var s in own.Samples)s.X+=2;
            ReferenceLap mapped;string reason;
            Check(AccReplayAlignment.TryAlign(source,own,false,true,out mapped,out reason),"full-course observed native lap establishes unique sign mapping");
            Check(mapped.CoordinateFrame==CapturedLap.AccCoordinateFrame&&Math.Abs(mapped.Samples[20].X-(own.Samples[20].X-2))<1e-8&&Math.Abs(mapped.Samples[20].Z-own.Samples[20].Z)<1e-8,"preserves real two metre line difference, never fits translation");
            Check(mapped.Samples.All(s=>double.IsNaN(s.Time))&&mapped.TimeBasis==source.TimeBasis,"never invents reference sample times");
            Check(source.CoordinateFrame==AccReplayAlignment.SourceCoordinateFrame&&source.Samples[20].Z==-mapped.Samples[20].Z,"original source stays untouched");
            Check(mapped.DistanceBasis==source.DistanceBasis&&mapped.Conditions.ContainsKey("coordinateAlignment"),"distance still distinct and mapping provenance retained");
            Check(!AccReplayAlignment.TryAlign(source,own,false,false,out mapped,out reason),"explicit course alignment required");
            var wrong=Native();wrong.Car="DIFFERENT";Check(!AccReplayAlignment.TryAlign(source,wrong,false,true,out mapped,out reason),"different car rejected");
            wrong=Native();wrong.Complete=false;Check(!AccReplayAlignment.TryAlign(source,wrong,false,true,out mapped,out reason),"partial own lap rejected");
            wrong=Native();foreach(var s in wrong.Samples)s.X+=1000;Check(!AccReplayAlignment.TryAlign(source,wrong,false,true,out mapped,out reason),"unknown origin not silently translated");
            wrong=Native();foreach(var s in wrong.Samples){s.X*=100;s.Z*=100;}Check(!AccReplayAlignment.TryAlign(source,wrong,false,true,out mapped,out reason),"different coordinate scale rejected");
            wrong=Native();for(int i=0;i<wrong.Samples.Length;i+=3){wrong.Samples[i].X=double.NaN;wrong.Samples[i].Z=double.NaN;}Check(!AccReplayAlignment.TryAlign(source,wrong,false,true,out mapped,out reason),"sparse positions rejected");
            wrong=Native();foreach(var s in wrong.Samples)s.X=0;var ambiguous=Source(wrong);Check(!AccReplayAlignment.TryAlign(ambiguous,wrong,false,true,out mapped,out reason),"ambiguous axis signature rejected");
            return count;
        }
    }
}
