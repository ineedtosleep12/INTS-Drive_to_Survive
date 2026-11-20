using System;
using System.IO;
using System.Linq;

namespace IntsLiveCoach
{
    // Pipeline checks only. A mirror replay is artificial, never a user's lap or a new reference.
    public static class ReferenceIntegrationTests
    {
        static int assertions;
        static void Check(bool ok,string label)
        {assertions++;if(!ok)throw new Exception("Reference integration: "+label);}

        static ReferenceLap ArtificialSource()
        {
            return new ReferenceLap {Game="LMU",Car="synthetic-model",Track="synthetic-track",GameVersion="unknown",
                TrackLengthM=double.NaN,DistanceBasis=ReferenceLap.LmuMotecDistanceBasis,LateralPositionBasis=ReferenceLap.LmuMotecLateralBasis,
                SourceUrl="local:synthetic-test-only",Author="synthetic fixture",Provenance="Artificial integration trace; never a driving reference",Complete=false,Validity="unknown",
                Samples=Enumerable.Range(0,199).Select(i=>new ReferenceSample {Time=i*.2,DistanceM=5+i*5,SpeedKph=90,
                    Brake=i>=40&&i<65?.8:0,Throttle=i>=40&&i<65?0:.8,Steering=.1,PathLateralM=2}).ToArray()};
        }
        static TelemetryFrame ReplayFrame(ReferenceSample source,int lap,double distance,double time,double length)
        {
            return new TelemetryFrame {Time=time,Lap=lap,LapDistance=distance,TrackLength=length,SpeedKph=source.SpeedKph,
                Brake=source.Brake,Throttle=source.Throttle,Steering=source.Steering,PathLateralM=source.PathLateralM,
                Track="SYNTHETIC track alias",Car="SYNTHETIC livery alias",GameVersion="unknown",OnTrack=true,
                ValidLapKnown=true,IsValidLap=true,DistanceAgeSeconds=.1};
        }
        static void Exercise(ReferenceLap source,double syntheticNativeLength)
        {
            var engine=new CoachEngine();var first=source.Samples.First();var last=source.Samples.Last();
            engine.Update(ReplayFrame(first,0,syntheticNativeLength-5,39.8,syntheticNativeLength));
            foreach(var s in source.Samples)engine.Update(ReplayFrame(s,1,s.DistanceM,40+s.Time-first.Time,syntheticNativeLength));
            var view=engine.Update(ReplayFrame(first,2,first.DistanceM,40+last.Time-first.Time+.2,syntheticNativeLength));
            Check(view.CompletedLaps==1,"two observed line crossings produce exactly one complete mirrored lap");
            ReferenceLap own;string error;
            Check(CapturedLap.TryCreate("LMU",engine.GetLatestCompleteLap(),out own,out error),"engine result is accepted by capture exporter: "+error);
            own=ReferenceLap.LoadJson(own.ToJson());
            Check(own.Complete&&own.Validity=="valid"&&own.Samples.First().DistanceM==0&&own.Samples.Last().DistanceM==syntheticNativeLength,"exported boundaries survive canonical serialization");
            Check(!ReferenceComparison.Compare(source,own,false,true).Comparable,"exploratory mapping cannot bypass model and track identity");
            // Simulate the explicit driver-confirmed label action, preserving original source strings.
            own.Car=source.Car;own.Track=source.Track;own.Layout=source.Layout;
            own.Conditions["sourceIdentityMapping"]="SYNTHETIC test mapping only";
            Check(own.Conditions["sourceCarLabel"]=="SYNTHETIC livery alias","identity mapping preserves the recorded label");
            Check(!ReferenceComparison.Compare(source,own).Comparable,"name confirmation alone never silently opts into MoTeC mapping");
            var result=ReferenceComparison.Compare(source,own,false,true);
            Check(result.Comparable&&result.Points.Length>100,"explicit identity and channel mapping enable real comparison samples");
            Check(result.Sections.Length>5&&result.BrakeZones.Length>0,"ranked losses and sustained braking zones available after full pipeline");
            Check(result.HasLateralComparison&&!result.HasLineComparison,"lateral evidence remains separate from absent world line data");
            Check(result.Points.All(p=>Math.Abs(p.DeltaSeconds)<.000001&&Math.Abs(p.PathLateralDeltaM)<.000001),"mirror replay aligns at course position without inventing time loss or offset");
            Check(double.IsNaN(result.LapDeltaSeconds)&&double.IsNaN(source.TrackLengthM)&&!source.Complete,"partial source never becomes a full lap or acquires invented length");
            Check(result.Warnings.Any(w=>w.Contains("실제 주행 검증 전")),"explicit exploration retains mapping qualification");
        }
        public static int Run()
        {assertions=0;Exercise(ArtificialSource(),1000);return assertions;}

        // Optional local smoke check: reads user-downloaded evidence without embedding or redistributing it.
        public static int RunPrivate(string referencePath)
        {assertions=0;Exercise(ReferenceLap.LoadJson(File.ReadAllText(referencePath)),5793);return assertions;}
    }
}
