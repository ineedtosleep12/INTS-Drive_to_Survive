using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    // Synthetic telemetry fixtures exercise decisions, not game compatibility.
    public static class CoachEngineTests
    {
        static int assertions;
        static void Check(bool condition,string message)
        { assertions++;if(!condition)throw new Exception("Coach test: "+message); }
        sealed class Fixture
        {
            public double Clock;
            public int LastLap=-1;
            public double LastS,LastSpeed=180;
            public string Track="Synthetic circuit",Car="Synthetic GT3";
            public double DistanceAge;
            public int[] Starts=new int[]{200};
            public List<CoachView> Feedback=new List<CoachView>();
            public TelemetryFrame Frame(int lap,double s,int shift=0,double fuel=35)
            {
                int start=Starts.LastOrDefault(x=>s>=x+shift);if(start==0)start=Starts[0];
                double local=s-shift-start+200;
                double speed=local<200?180:local<280?180-(local-200)*1.5:local<500?60+(local-280)*.6:192;
                double distance=LastLap<0?5:lap==LastLap?s-LastS:1000-LastS+s;
                Clock+=Math.Max(.02,Math.Max(0,distance)/(Math.Max(20,(LastSpeed+speed)/2)/3.6));
                LastLap=lap;LastS=s;LastSpeed=speed;
                return new TelemetryFrame {Time=Clock,Lap=lap,LapDistance=s,TrackLength=1000,SpeedKph=speed,
                    Throttle=local<200||local>=330?.9:0,Brake=local>=200&&local<280?.8:0,
                    Steering=local>=230&&local<340?.4:.03,Clutch=0,FuelLitres=fuel,Gear=3,
                    DistanceAgeSeconds=DistanceAge,Track=Track,Car=Car,GameVersion="fixture",OnTrack=true,ValidLapKnown=true,IsValidLap=true};
            }
            public CoachView Send(CoachEngine engine,int lap,double s,int shift=0,double fuel=35)
            {
                CoachView v=engine.Update(Frame(lap,s,shift,fuel));if(v.NewFeedback)Feedback.Add(v);return v;
            }
            public CoachView Lap(CoachEngine engine,int lap,int start=0,int end=995,int shift=0,double fuel=35)
            { CoachView view=null;for(int s=start;s<=end;s+=5)view=Send(engine,lap,s,shift,fuel);return view; }
        }
        static CoachEngine Seed(out Fixture fixture)
        {
            fixture=new Fixture();CoachEngine engine=new CoachEngine();
            fixture.Lap(engine,0,400);fixture.Lap(engine,1);fixture.Send(engine,2,0);
            return engine;
        }
        public static int Run()
        {
            assertions=0;
            CoachEngine empty=new CoachEngine();CoachView e=empty.Update(null);
            Check(!e.HasReference,"no invented reference on missing data");
            Check(!empty.ExportJson().Contains("NaN"),"missing values serialize as null");
            new JavaScriptSerializer().DeserializeObject(empty.ExportJson());

            Fixture f=new Fixture();CoachEngine engine=new CoachEngine();
            CoachView v=f.Lap(engine,0,400);
            Check(!v.HasReference && v.CompletedLaps==0,"attach mid-lap never establishes reference");
            v=f.Send(engine,1,0);Check(!v.HasReference,"one boundary is only start of recording");
            f.Lap(engine,1,5);v=f.Send(engine,2,0);
            Check(v.HasReference && v.ReferenceLap==1 && v.CompletedLaps==1,"full observed valid lap becomes next-lap reference");
            Check(v.ReferenceAtPosition!=null && v.ReferenceAtPosition.LapDistance==0,"line sample available exactly at zero distance");
            Check(v.ReferenceBrakeDistance>0 && v.ReferenceBrakeDistance<300,"reference brake cue comes from recorded input");
            f.Lap(engine,2,5,700,20,30);
            Check(f.Feedback.Count==1,"single completed braking zone emits one after-corner feedback");
            CoachView feedback=f.Feedback[0];
            Check(feedback.Headline.Contains("20 m 늦게"),"braking position compared in metres");
            Check(feedback.Detail.Contains("같은 탈출 표시 지점"),"exit comparison uses common coordinate and labels approximate coordinates");
            Check(feedback.ResultSummary.Length<=90&&feedback.ResultSummary.Split('\n').Length<=2&&feedback.ResultSummary.Contains("오차 추정"),"overlay summary stays concise and includes uncertainty");
            Check(feedback.ReferenceLap==1,"reference remains fixed within current lap");
            Check(feedback.ReferenceAtPosition!=null,"distance aligned reference sample exists");
            Check(!feedback.Headline.Contains("언더스티어")&&!feedback.Detail.Contains("원인"),"input comparison does not invent handling diagnosis");
            var exported=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(engine.ExportJson());
            Check((int)exported["referenceLap"]==1,"export reference matches frozen lap");
            Check(!engine.ExportJson().Contains("NaN")&&!engine.ExportJson().Contains("Infinity"),"optional unknown channels remain valid JSON");
            Check(engine.ExportJson().Contains("Synthetic circuit"),"export retains context");
            Check(engine.ExportJson().Contains("brakeStartDeltaM"),"observed input deltas exported");
            f.Lap(engine,2,705);v=f.Send(engine,3,0);
            Check(v.ReferenceLap==2 && v.CompletedLaps==2,"reference advances only at completed lap boundary");

            Fixture invalidFixture;CoachEngine invalid=Seed(out invalidFixture);
            for(int s=5;s<=995;s+=5)
            {
                TelemetryFrame sample=invalidFixture.Frame(2,s);
                if(s==500)sample.IsValidLap=false;
                invalid.Update(sample);
            }
            v=invalidFixture.Send(invalid,3,0);
            Check(v.ReferenceLap==1&&v.CompletedLaps==1,"one invalidation prevents baseline replacement for entire lap");
            TelemetryFrame unknown=invalidFixture.Frame(3,5);unknown.ValidLapKnown=false;
            v=invalid.Update(unknown);Check(!v.NewFeedback && v.Status.Contains("미제공"),"unknown validity never silently passes");

            Fixture unknownFixture=new Fixture();CoachEngine unknownEngine=new CoachEngine();
            for(int lap=0;lap<3;lap++)for(int s=0;s<1000;s+=5)
            {TelemetryFrame sample=unknownFixture.Frame(lap,s);sample.ValidLapKnown=false;v=unknownEngine.Update(sample);}
            Check(!v.HasReference&&v.CompletedLaps==0,"unknown validity cannot form reference even across complete laps");

            Fixture bad;CoachEngine guarded=Seed(out bad);
            TelemetryFrame pit=bad.Frame(2,5);pit.InPit=true;v=guarded.Update(pit);
            Check(!v.HasReference&&v.CompletedLaps==0&&v.Status.Contains("피트"),"pit stops clear stale-condition comparison");
            guarded=Seed(out bad);TelemetryFrame replay=bad.Frame(2,5);replay.IsReplay=true;
            Check(!guarded.Update(replay).HasReference,"replay frames are excluded");
            guarded=Seed(out bad);TelemetryFrame gap=bad.Frame(2,5);gap.Time+=2;
            Check(!guarded.Update(gap).HasReference,"data gaps cannot bridge a valid lap");
            guarded=Seed(out bad);TelemetryFrame backwards=bad.Frame(2,5);backwards.Time=0;
            Check(!guarded.Update(backwards).HasReference,"session clock reset clears reference");
            guarded=Seed(out bad);TelemetryFrame car=bad.Frame(2,5);car.Car="Different car";
            Check(!guarded.Update(car).HasReference,"car change isolates references");
            guarded=Seed(out bad);TelemetryFrame track=bad.Frame(2,5);track.Track="Different circuit";
            Check(!guarded.Update(track).HasReference,"track change isolates references");
            guarded=Seed(out bad);TelemetryFrame missing=bad.Frame(2,5);missing.Brake=double.NaN;
            Check(!guarded.Update(missing).HasReference,"missing pedal channel is not zero");
            guarded=Seed(out bad);TelemetryFrame jump=bad.Frame(2,5);jump.LapDistance=500;
            Check(!guarded.Update(jump).HasReference,"impossible forward jump is a discontinuity");
            guarded=Seed(out bad);v=guarded.Disconnect("테스트 연결 끊김");
            Check(!v.HasReference&&v.RecentTrace.Length==0&&v.Status.Contains("끊김"),"disconnect clears current comparison and traces");
            guarded.Reset();Check(!guarded.ExportJson().Contains("Synthetic circuit"),"explicit reset removes retained feedback contexts");

            Fixture highFuel;CoachEngine fuel=Seed(out highFuel);
            highFuel.Lap(fuel,2,5,700,20,55);
            Check(highFuel.Feedback.Count==1&&highFuel.Feedback[0].Detail.Contains("시간 평가는 생략"),"large fuel difference withholds time assessment");
            TelemetryFrame invalidate=highFuel.Frame(2,705);invalidate.IsValidLap=false;
            v=fuel.Update(invalidate);Check(!v.Headline.Contains("코너 약"),"invalidated lap cannot retain stale coaching headline");
            string history=fuel.ExportJson();fuel.Disconnect("disconnected");
            Check(fuel.ExportJson().Contains("Synthetic circuit"),"retained feedback has its own explicit context after disconnect");

            // A duplicate sample is a pause, not a second lap or repeated advice.
            Fixture duplicate;CoachEngine dedup=Seed(out duplicate);TelemetryFrame same=duplicate.Frame(2,5);
            dedup.Update(same);for(int i=0;i<30;i++)v=dedup.Update(same);
            Check(v.CompletedLaps==1&&!v.NewFeedback,"duplicate timestamps do not repeat events");
            double original=v.ReferenceAtPosition.Brake;v.ReferenceAtPosition.Brake=999;
            CoachView detached=dedup.Update(duplicate.Frame(2,10));
            Check(detached.ReferenceAtPosition.Brake<=1,"view mutation cannot corrupt reference data");
            Check(original<=1,"reference contains normalized inputs");

            Fixture coarse;CoachEngine uncertain=Seed(out coarse);coarse.DistanceAge=.3;
            coarse.Lap(uncertain,2,5,700,10,30);
            Check(coarse.Feedback.Count==1&&coarse.Feedback[0].Headline.Contains("오차 범위"),"small metre delta inside scoring uncertainty is not a correction cue");
            Check(coarse.Feedback[0].Detail.Contains("시간 평가는 생략"),"coarse distance withholds apparent time gains");
            Check(!coarse.Feedback[0].NextAction.Contains("이전 위치로"),"uncertain differences do not prescribe optimal brake point");
            TelemetryFrame stale=coarse.Frame(2,705);stale.DistanceAgeSeconds=.6;
            Check(!uncertain.Update(stale).HasReference,"stale scoring timestamps reject comparison");

            Fixture finalInvalid;CoachEngine finalGuard=Seed(out finalInvalid);
            finalInvalid.Lap(finalGuard,2,5,990);TelemetryFrame final=finalInvalid.Frame(2,995);final.IsValidLap=false;finalGuard.Update(final);
            v=finalInvalid.Send(finalGuard,3,0);Check(v.ReferenceLap==1,"last invalid frame cannot be erased by next lap valid flag");
            Fixture unknownLine;CoachEngine lineGuard=Seed(out unknownLine);unknownLine.Lap(lineGuard,2,5);
            TelemetryFrame nextUnknown=unknownLine.Frame(3,0);nextUnknown.ValidLapKnown=false;v=lineGuard.Update(nextUnknown);
            Check(v.ReferenceLap==1,"unknown channel at line cannot promote an apparently complete lap");

            Fixture interpolated=new Fixture();CoachEngine continuity=new CoachEngine();
            interpolated.Lap(continuity,0,400);interpolated.Lap(continuity,1,5);interpolated.Send(continuity,2,5);
            var continuityData=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(continuity.ExportJson());
            object[] referenceFrames=(object[])continuityData["referenceFrames"];
            var first=(Dictionary<string,object>)referenceFrames[0];
            Check(Math.Abs(Convert.ToDouble(first["speedKph"])-186)<.01,"zero-distance sample interpolates line crossing rather than copying future speed");

            Fixture adjacent=new Fixture();adjacent.Starts=new int[]{200,400};CoachEngine ambiguous=new CoachEngine();
            adjacent.Lap(ambiguous,0,700);adjacent.Lap(ambiguous,1);adjacent.Send(ambiguous,2,0);
            adjacent.Starts=new int[]{300};adjacent.Lap(ambiguous,2,5,800);
            Check(adjacent.Feedback.Count==0,"ambiguous adjacent braking zones cannot produce confident matched-corner coaching");

            Fixture held=new Fixture();CoachEngine heldDistance=new CoachEngine();
            for(int lap=0;lap<2;lap++)for(int s=0;s<1000;s+=5)
            {
                TelemetryFrame sample=held.Frame(lap,s);
                sample.LapDistance=Math.Floor(s/10.0)*10;
                sample.DistanceAgeSeconds=(s%10)/(sample.SpeedKph/3.6);
                heldDistance.Update(sample);
            }
            v=held.Send(heldDistance,2,0);
            Check(v.HasReference&&v.CompletedLaps==1,"lower-rate held scoring distance still forms a coherent complete lap across wrap");
            TelemetryFrame captureOnly=held.Frame(2,5); captureOnly.TimeIsCaptureClock=true;
            Check(!heldDistance.Update(captureOnly).HasReference,"capture clock cannot qualify as measured game lap timing");
            return assertions;
        }
    }
}
