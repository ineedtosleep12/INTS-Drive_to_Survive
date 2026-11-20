using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    // Synthetic behavioural tests; these do not establish compatibility with a game.
    public static class PrincipleCoachTests
    {
        static int assertions;
        static void Check(bool condition,string message)
        {assertions++;if(!condition)throw new Exception("Principle test: "+message);}
        sealed class Fixture
        {
            public PrincipleCoach Coach=new PrincipleCoach();
            public DrivingDiscipline Discipline=DrivingDiscipline.Drift;
            public double Time;
            public bool CaptureClock;
            public string Car="Synthetic RWD";
            public List<CoachPrincipleView> Events=new List<CoachPrincipleView>();
            public TelemetryFrame Frame(double dt=.05)
            {
                Time+=dt;
                return new TelemetryFrame {Time=Time,SpeedKph=72,Brake=0,Throttle=.3,Steering=0,
                    OnTrack=true,Track="Synthetic playground",Car=Car,GameVersion="fixture",YawRate=0,LateralG=0,
                    LocalVelocityRight=0,LocalVelocityForward=20,TimeIsCaptureClock=CaptureClock};
            }
            public CoachPrincipleView Send(Action<TelemetryFrame> action=null,double dt=.05)
            {
                TelemetryFrame frame=Frame(dt);if(action!=null)action(frame);
                CoachPrincipleView v=Coach.Update(frame,Discipline);if(v.NewFeedback)Events.Add(v);return v;
            }
            public CoachPrincipleView For(double seconds,Action<TelemetryFrame> action=null,double dt=.05)
            {CoachPrincipleView v=null;for(int i=0;i<(int)Math.Ceiling(seconds/dt);i++)v=Send(action,dt);return v;}
        }
        static void Slide(TelemetryFrame f)
        {f.Steering=.4;f.Throttle=.55;f.YawRate=.5;f.LocalVelocityRight=4;f.LocalVelocityForward=15;f.SpeedKph=56;f.LateralG=.5;}
        static void BrakeTurn(TelemetryFrame f)
        {f.Brake=.7;f.Steering=.3;f.Throttle=0;f.YawRate=.2;f.LateralG=.6;}
        static Dictionary<string,object> Export(PrincipleCoach c)
        {return (Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(c.ExportJson());}
        static object[] Rows(Dictionary<string,object> doc,string name) {return (object[])doc[name];}

        public static int Run()
        {
            assertions=0;
            foreach(DrivingDiscipline d in Enum.GetValues(typeof(DrivingDiscipline)))
            {
                PrincipleLesson[] lessons=PrincipleCoach.GetLessons(d);
                Check(lessons.Length>=4,"each discipline has a usable offline curriculum");
                Check(lessons.All(l=>!string.IsNullOrWhiteSpace(l.Explanation)&&!string.IsNullOrWhiteSpace(l.Drill)&&!string.IsNullOrWhiteSpace(l.Limitations)),"lessons separate principle, drill and limits");
                Check(lessons.All(l=>l.SourceUrl.StartsWith("https://")&&!string.IsNullOrWhiteSpace(l.SourceTitle)),"lessons include primary source attribution");
            }
            PrincipleLesson detached=PrincipleCoach.GetLesson("race-grip");detached.Explanation="changed";
            Check(PrincipleCoach.GetLesson("race-grip").Explanation!="changed","catalog returns detached lessons");
            Check(PrincipleCoach.GetLesson("unknown")==null,"unknown lesson does not invent content");
            PrincipleCoach noData=new PrincipleCoach();CoachPrincipleView v=noData.Update(null,DrivingDiscipline.Drift);
            Check(!v.NewFeedback&&!v.HasObservation,"no unsupported observation on missing frames");
            Check(!noData.ExportJson().Contains("NaN"),"empty export is standard JSON");
            Export(noData);

            Fixture unavailable=new Fixture();
            v=unavailable.For(2,f=>{Slide(f);f.YawRate=double.NaN;f.LocalVelocityRight=double.NaN;f.LocalVelocityForward=double.NaN;});
            Check(v.Limits.Contains("미제공"),"missing dynamics are explained");
            unavailable.For(1);
            Check(unavailable.Events.Count==0,"pedals and steering alone cannot diagnose a slide");
            Check(unavailable.Coach.ExportJson().Contains("null")&&!unavailable.Coach.ExportJson().Contains("NaN"),"unknown optional channels export as null");
            Fixture contradictory=new Fixture();contradictory.For(2,f=>{Slide(f);f.LocalVelocityRight=100;});contradictory.For(1);
            Check(contradictory.Events.Count==0,"inconsistent local velocity and total speed cannot support sideways observation");

            Fixture sustained=new Fixture();sustained.For(1.8,Slide);
            Check(sustained.Events.Count==0,"feedback waits until the manoeuvre settles");
            v=sustained.For(1);
            Check(sustained.Events.Count==1&&sustained.Events[0].LessonId=="drift-donut","sustained measured sideways motion selects beginner donut lesson");
            Check(sustained.Events[0].Observation.Contains("1초")&&!sustained.Events[0].Observation.Contains("성공"),"observation reports duration without manoeuvre success claim");
            Check(sustained.Events[0].Limits.Contains("궤적"),"drift completion requires target path information");
            sustained.For(3,Slide);sustained.For(1);
            Check(sustained.Events.Count==1,"repeated events respect global cooldown");
            sustained.For(16);sustained.For(2,Slide);sustained.For(1);
            Check(sustained.Events.Count==1,"same rule has longer cooldown than global cooldown");
            sustained.For(26);sustained.For(2,Slide);sustained.For(1);
            Check(sustained.Events.Count==2,"rearmed event can coach again after cooldown");
            var observed=Export(sustained.Coach);
            Check(Rows(observed,"observations").Length==2,"local export stores delivered observations");

            Fixture brief=new Fixture();brief.For(.5,Slide);brief.For(1);
            Check(brief.Events.Count==0,"brief motion does not pass sustained event threshold");
            Fixture held=new Fixture();held.For(30,Slide);
            Check(held.Events.Count==0,"continuous long donut does not deliver feedback during the manoeuvre");
            held.For(1);
            Check(held.Events.Count==1,"thirty-second donut refreshes evidence and delivers once after settling");
            var longEvent=(Dictionary<string,object>)Rows(Export(held.Coach),"observations")[0];
            Check(Convert.ToDouble(longEvent["deliveredAt"])-Convert.ToDouble(longEvent["detectedAt"])<2,"long manoeuvre exports a recent evidence timestamp");
            held.For(20);Check(held.Events.Count==1,"settled long-donut observation does not repeat");
            Fixture expired=new Fixture();expired.For(1.8,Slide);expired.For(13,f=>f.Steering=.2);expired.For(1);
            Check(expired.Events.Count==0,"ended evidence expires if no settled delivery opportunity follows");

            Fixture absolute=new Fixture();absolute.CaptureClock=true;
            absolute.For(1.8,f=>{Slide(f);f.LocalVelocityRight=double.NaN;f.LocalVelocitySideAbs=4;});absolute.For(1);
            Check(absolute.Events.Count==1,"verified lateral magnitude works without an invented right-positive sign");
            Check(absolute.Events[0].Limits.Contains("수신 시계"),"capture-clock observation names its time source");
            Check(absolute.Coach.ExportJson().Contains("\"timeSource\":\"capture\""),"export explicitly distinguishes capture clock from simulation time");

            Fixture race=new Fixture();race.Discipline=DrivingDiscipline.Race;
            race.For(.9,BrakeTurn);race.For(1);
            Check(race.Events.Count==1&&race.Events[0].LessonId=="race-grip","sustained simultaneous brake and turn teaches shared grip");
            Check(race.Events[0].Limits.Contains("비율이 아닙니다"),"pedal percentages are not interpreted as tyre-force percentages");
            Check(!race.Events[0].Observation.Contains("실패")&&!race.Events[0].Observation.Contains("언더스티어"),"inputs do not cause an automatic failure diagnosis");
            Fixture invalid=new Fixture();invalid.Discipline=DrivingDiscipline.Race;
            v=invalid.For(1,f=>{BrakeTurn(f);f.ValidLapKnown=true;f.IsValidLap=false;});invalid.For(1);
            Check(invalid.Events.Count==0&&v.Status.Contains("무효"),"known-invalid circuit laps suppress current coaching");

            Fixture rally=new Fixture();rally.Discipline=DrivingDiscipline.Rally;
            rally.For(.8,f=>{f.Brake=.3;f.Steering=.3;f.ValidLapKnown=false;});rally.For(1);
            Check(rally.Events.Count==1&&rally.Events[0].LessonId=="rally-load","rally principles need neither lap validity nor a track length");
            Check(!rally.Events[0].Observation.Contains("실패"),"absent rally track limits are never treated as failure");
            Fixture invalidRally=new Fixture();invalidRally.Discipline=DrivingDiscipline.Rally;
            invalidRally.For(.8,f=>{f.Brake=.3;f.Steering=.3;f.ValidLapKnown=true;f.IsValidLap=false;});invalidRally.For(1);
            Check(invalidRally.Events.Count==1,"rally does not apply circuit valid-lap rules");

            Fixture swing=new Fixture();swing.Discipline=DrivingDiscipline.Rally;
            swing.For(.5,f=>{f.Steering=.3;f.YawRate=.3;});swing.For(.6,f=>{f.Steering=-.3;f.YawRate=-.3;});swing.For(1);
            Check(swing.Events.Count==1&&swing.Events[0].LessonId=="rally-pendulum","observed steering and rotation reversal suggests pendulum lesson without handedness assumption");

            Fixture hand=new Fixture();hand.For(1.1,f=>{f.Handbrake=.8;f.Steering=.2;});hand.For(1);
            Check(hand.Events.Count==1&&hand.Events[0].LessonId=="drift-handbrake","sustained actual handbrake input selects its lesson");
            Fixture holding=new Fixture();holding.For(2,f=>f.Handbrake=.8);
            Check(holding.Events.Count==0,"handbrake still active at driving speed is not a settled delivery moment");
            holding.For(1);Check(holding.Events.Count==1,"queued handbrake observation appears after release and settling");
            Fixture clutch=new Fixture();clutch.For(.2,f=>{f.Clutch=.9;f.Throttle=.6;});clutch.For(.2,f=>{f.Clutch=0;f.Throttle=.6;});clutch.For(1);
            Check(clutch.Events.Count==1&&clutch.Events[0].LessonId=="drift-clutch","measured clutch pulse selects clutch lesson");
            Check(clutch.Events[0].Limits.Contains("자동 클러치"),"clutch pulse is not automatically classified as a deliberate kick");

            Fixture replay=new Fixture();replay.For(2,f=>{Slide(f);f.IsReplay=true;});replay.For(1);
            Check(replay.Events.Count==0,"flagged replay frames cannot accumulate coaching events");
            Fixture pit=new Fixture();pit.For(1.8,Slide);v=pit.Send(f=>f.InPit=true);pit.For(1);
            Check(pit.Events.Count==0&&!v.HasObservation,"pit invalidation removes pending feedback");
            Fixture paused=new Fixture();paused.For(1.8,Slide);paused.Send(null,2);paused.For(1);
            Check(paused.Events.Count==0,"data gap removes pending event");
            Fixture changed=new Fixture();changed.For(1.8,Slide);changed.Car="Different car";changed.For(1);
            Check(changed.Events.Count==0,"identity change cannot deliver previous car's pending event");
            Fixture clockReset=new Fixture();clockReset.For(1.8,Slide);clockReset.Time=0;clockReset.For(1);
            Check(clockReset.Events.Count==0,"time reset cannot deliver a stale event");
            Fixture duplicate=new Fixture();TelemetryFrame same=duplicate.Frame();Slide(same);
            for(int i=0;i<100;i++)duplicate.Coach.Update(same,DrivingDiscipline.Drift);duplicate.For(1);
            Check(duplicate.Events.Count==0,"duplicate timestamps cannot satisfy sustained event duration");
            Fixture missing=new Fixture();missing.For(1.8,Slide);missing.Send(f=>f.Throttle=double.NaN);missing.For(1);
            Check(missing.Events.Count==0,"missing required input clears pending observation instead of using zero");

            v=sustained.Coach.Disconnect("연결 끊김");
            Check(!v.HasObservation&&!v.NewFeedback,"disconnect returns no stale active observation");
            Check(Rows(Export(sustained.Coach),"recentFrames").Length==0&&Rows(Export(sustained.Coach),"observations").Length==2,"disconnect clears live frames but retains contextual history for export");
            sustained.Coach.Reset();Check(Rows(Export(sustained.Coach),"observations").Length==0,"explicit reset clears history");

            Fixture bounded=new Fixture();bounded.For(35,null,.025);
            object[] frames=Rows(Export(bounded.Coach),"recentFrames");
            Check(frames.Length<=600,"frame retention has a hard bound even above nominal sample rate");
            double first=Convert.ToDouble(((Dictionary<string,object>)frames[0])["time"]);
            Check(bounded.Time-first<=30.001,"frame retention also has a thirty-second time bound");
            Fixture history=new Fixture();history.Discipline=DrivingDiscipline.Rally;
            for(int i=0;i<55;i++) {history.For(1.5,f=>{f.Handbrake=.8;f.Steering=.15;},.25);history.For(48,null,.25);}
            Check(history.Events.Count==55&&Rows(Export(history.Coach),"observations").Length==50,"observation history retains only the last fifty actual events");
            Check(!history.Coach.ExportJson().Contains("NaN")&&!history.Coach.ExportJson().Contains("Infinity"),"full bounded export remains standard JSON");
            return assertions;
        }
    }
}
