using System;
using System.Collections.Generic;
using System.Linq;

namespace IntsLiveCoach
{
    public static class SpatialReferenceTests
    {
        static int count;
        static void Check(bool ok,string message){count++;if(!ok)throw new Exception("Spatial reference: "+message);}
        static void Reject(Action action,string message){bool rejected=false;try{action();}catch(FormatException){rejected=true;}Check(rejected,message);}
        static ReferenceLap Native()
        {
            var lap=new ReferenceLap {Game="ACC",Car="SYNTHETIC",Track="SYNTHETIC",GameVersion="test",Layout="GP",DistanceBasis=CapturedLap.AccDistanceBasis,CoordinateFrame=CapturedLap.AccCoordinateFrame,TrackLengthM=5200,Complete=true,Validity="valid"};
            lap.Samples=Enumerable.Range(0,1041).Select(i=>{double s=i*5,a=s/5200*Math.PI*2;bool turning=(s>=500&&s<800)||(s>=2500&&s<2800),braking=(s>=500&&s<640)||(s>=2500&&s<2640);return new ReferenceSample {Time=i*.1,DistanceM=s,SpeedKph=turning?120:180,Brake=braking?.7:0,Throttle=turning?.3:1,Steering=turning?.2:0,X=700*Math.Cos(a)+80*Math.Sin(3*a),Z=800*Math.Sin(a)-100};}).ToArray();return lap;
        }
        static ReferenceLap Spatial(ReferenceLap native)
        {var lap=ReferenceLap.LoadJson(native.ToJson());lap.TimeBasis=ReferenceLap.DistanceOnlyTimeBasis;lap.TrackLengthM=double.NaN;lap.Complete=false;lap.Validity="unknown";lap.DistanceBasis=AccReplayAlignment.SourceDistanceBasis;lap.CoordinateFrame=AccReplayAlignment.SourceCoordinateFrame;foreach(var s in lap.Samples){s.Time=double.NaN;s.SteeringDegrees=s.Steering*300;s.Steering=double.NaN;s.Z=-s.Z;}return lap;}
        public static int Run()
        {
            count=0;var native=Native();var source=Spatial(native);var restored=ReferenceLap.LoadJson(source.ToJson());
            Check(!restored.HasTime&&restored.Samples.All(s=>double.IsNaN(s.Time)),"missing clock roundtrips as null");
            Check(ReferenceLap.LoadJson(native.ToJson()).HasTime,"existing recorded clocks retained");
            var bad=Spatial(native);bad.Samples[4].Time=0;Reject(()=>bad.Validate(),"mixed fabricated timestamp rejected");
            bad=Spatial(native);bad.Samples[4].DistanceM=bad.Samples[3].DistanceM;Reject(()=>bad.Validate(),"duplicate spatial distance rejected");
            bad=Spatial(native);bad.TimeBasis="guessed";Reject(()=>bad.Validate(),"unknown time basis rejected");
            var c=ReferenceComparison.Compare(source,native);
            Check(!c.Comparable,"source-native distance mapping requires opt in");
            c=ReferenceComparison.Compare(source,native,false,true);
            Check(c.Comparable&&!c.HasTimeComparison&&double.IsNaN(c.LapDeltaSeconds),"spatial comparison never claims time delta");
            Check(c.Points.All(p=>double.IsNaN(p.DeltaSeconds))&&c.Sections.All(p=>double.IsNaN(p.TimeLossSeconds)),"all section and point losses remain missing");
            Check(c.BrakeZones.Length==2&&c.BrakeZones.All(b=>Math.Abs(b.StartDeltaM)<.001),"distance-persistent brake starts match real timed trace");
            Check(!c.HasLineComparison,"raw map and native frame cannot compare lines");
            ReferenceLap aligned;string reason;
            Check(AccReplayAlignment.TryAlign(source,native,false,true,out aligned,out reason),"actual native full-course geometry resolves axis sign");
            c=ReferenceComparison.Compare(aligned,native,false,true);Check(c.HasLineComparison&&!c.HasTimeComparison,"geometry check enables line but never creates clock");
            var coach=new LiveReferenceCoach();coach.Configure(new ReferenceLiveSettings(aligned,true,false,true));
            Check(coach.PreparedZoneCount==2,"spatial zones use degree-relative shape and distance without fake timestamps");
            var events=new List<LiveReferenceView>();
            foreach(var s in native.Samples)
            {
                var view=coach.Update("ACC",new TelemetryFrame {Time=s.Time,LapDistance=s.DistanceM,TrackLength=native.TrackLengthM,Lap=1,Car=native.Car,Track=native.Track,GameVersion=native.GameVersion,SpeedKph=s.SpeedKph,Brake=s.Brake,Throttle=s.Throttle,Steering=s.Steering,X=s.X,Z=s.Z,OnTrack=true,ValidLapKnown=true,IsValidLap=true,DistanceAgeSeconds=double.NaN,DistanceObservationAgeSeconds=0,DistanceObservationIntervalSeconds=.05});
                if(view.NewFeedback)events.Add(view);
            }
            Check(events.Count==2,"native ACC observed timing delivers spatial cues after both corners");
            Check(events.All(v=>double.IsNaN(v.TimeLossSeconds)&&v.Heading.Contains("위치")),"live cues do not invent seconds");
            Check(events.All(v=>!string.IsNullOrEmpty(v.LineObservation)),"calibrated native line evidence reaches live cues");
            Check(coach.ExportJson().Contains("\"lossSeconds\":null")&&!coach.ExportJson().Contains("NaN"),"export is valid JSON with absent times");
            var sample=ReferenceComparison.SampleAtDistance(source,12.5);Check(sample!=null&&double.IsNaN(sample.Time)&&sample.DistanceM==12.5,"spatial interpolation leaves time missing");
            return count;
        }
    }
}
