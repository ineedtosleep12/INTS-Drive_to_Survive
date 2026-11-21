using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    internal static class FullStageCoachTests
    {
        static TelemetryFrame Frame(int tick, double distance, double length)
        {
            return new TelemetryFrame { Time=tick*.1, StageElapsedSeconds=tick*.1, LapDistance=distance,
                TrackLength=length, SpeedKph=36, OnTrack=true, Steering=.04, Brake=0, Throttle=.3, Gear=2,
                GameVersion="fixture", Track="stage", Car="car" };
        }
        static TelemetryFrame Acr(int tick)
        {
            var f=Frame(tick,double.NaN,double.NaN);
            f.TimeIsCaptureClock=true; f.StageElapsedSeconds=double.NaN;
            return f;
        }
        static Dictionary<string,object> Part(FullStageCoach coach,string name)
        {
            var json=new JavaScriptSerializer();
            return json.Deserialize<Dictionary<string,object>>(json.Serialize(coach.Snapshot()))[name] as Dictionary<string,object>;
        }
        public static int Run()
        {
            int count=0;
            Action<bool,string> check=delegate(bool ok,string why) {
                if(!ok) throw new Exception("Full rally stage: "+why); count++;
            };
            var coach=new FullStageCoach();
            int recaps=0, sections=0;
            for (int i=0; i<=121; i++)
            {
                // A full continuous stage, two isolated bends and four quarter windows.
                var f=Frame(i,i,120);
                bool steering=(i>=10&&i<17)||(i>=61&&i<68);
                f.Steering=steering?.40:.03;
                f.Brake=steering&& (i<13|| (i>=61&&i<64))?.35:0;
                f.OnTrack=i<120;
                var v=coach.Update(f);
                if(v.NewFeedback && v.Title.Contains("코너 복기")) recaps++;
                if(v.NewFeedback && v.Title.Contains("구간 복기")) sections++;
                if(i==15) check(v.Status.Contains("%") && v.Status.Contains("km"),"native distance progress on every coaching phase");
                if(i==120) check(v.NewFeedback && v.Title.Contains("거리 도달"),"continuous endpoint crossing can be an off-track endpoint packet");
                if(i==121) check(!v.NewFeedback && v.Title.Contains("거리 도달"),"finish held until new attempt");
            }
            var run=Part(coach,"lastRun");
            check(recaps==2 && Convert.ToInt32(run["reviewedTurns"])==2,"reviews accumulate across multiple bends");
            check(sections>0 && ((IList)run["sections"]).Count==4,"four sections and a live section review");
            check((bool)run["observedNearStart"] && (string)run["state"]=="distance-end","coverage and endpoint provenance");
            check(((IList)run["notes"]).Count>=2 && Part(coach,"current")==null,"observations persist after finish");
            check(coach.ReviewText().Contains("75–100%") && coach.ReviewText().Contains("다음 비슷한 굽이"),"section and corner review available inside app");
            var restart=Frame(122,0,120); restart.StageElapsedSeconds=.1;
            check(coach.Update(restart).Status.Contains("0%") && Part(coach,"current")!=null,"restart after completed stage");
            coach.Reset();
            for(int i=0;i<20;i++) coach.Update(Frame(i,300+i,1000));
            check(!(bool)Part(coach,"current")["observedNearStart"],"late connection reports partial coverage");
            var fakeFinish=Frame(20,1000,1000); fakeFinish.OnTrack=false;
            check(coach.Update(fakeFinish).Title.Contains("관측 중단"),"teleport to endpoint cannot certify crossing");
            check((string)Part(coach,"lastRun")["state"]=="interrupted","interrupted observation kept");
            coach.Reset();
            for(int i=0;i<30;i++) coach.Update(Frame(i,100+i,1000));
            var resetClock=Frame(30,130,1000); resetClock.StageElapsedSeconds=.1;
            coach.Update(resetClock);
            check(Convert.ToDouble(Part(coach,"current")["observedSeconds"])==0,"native stage clock reset despite monotonic session clock");
            check(Part(coach,"lastRun")!=null,"restart preserves previous report");
            coach.Reset();
            check(!coach.CanFinishManually && coach.FinishManually()==null,"empty observation cannot be marked finished");
            int reversals=0;
            for(int i=0;i<310;i++)
            {
                var f=Acr(i);
                if(i<12) f.Steering=.4;
                else if(i<22) f.Steering=-.4;
                var view=coach.Update(f);
                if(i==5) check(view.Title.Contains("조향 중"),"joining in a bend observes active steering");
                if(view.NewFeedback && view.Title.Contains("반대 방향")) reversals++;
                if(i==90) check(view.Status.Contains("관측") && !view.Status.Contains("%"),"AC Rally progress follows live observation clock");
            }
            var partial=Part(coach,"current");
            check(reversals==1 && Convert.ToInt32(partial["steeringReversals"])==1,"linked bends receive one sustained reversal cue");
            check(partial["lengthM"]==null && partial["lastNativeStageSeconds"]==null && ((IList)partial["sections"]).Count==2,"time windows for ACR without distance or stage clock");
            coach.Interrupt();
            check(coach.CanFinishManually && coach.LastReport.Title.Contains("중단"),"lost feed ACR partial can be marked manually");
            var manual=coach.FinishManually();
            check(manual.NewFeedback && (string)Part(coach,"lastRun")["state"]=="manual-after-interruption","manual end retains exact observation");
            check(!coach.CanFinishManually && !coach.Update(Acr(311)).NewFeedback && Part(coach,"current")==null,"manual completion stays latched");
            coach.Interrupt();
            check(coach.LastReport.Title.Contains("직접 마감"),"repeated disconnection preserves frozen report");
            coach.Update(Acr(1));
            check(Part(coach,"current")!=null,"new ACR receive clock starts a new observed run");
            var duplicate=Acr(1);
            check(!coach.Update(duplicate).NewFeedback,"duplicate packet does not count distance/time twice");
            coach.Reset();
            var replay=Frame(1,1,120); replay.IsReplay=true;
            coach.Update(replay); check(Part(coach,"current")==null,"replay cannot begin a run");
            for(int i=0;i<20;i++) coach.Update(Acr(i));
            check(coach.FinishManually().Title.Contains("직접 종료") && !coach.LastReport.NewFeedback,"manual end while connected");
            return count;
        }
    }
}
