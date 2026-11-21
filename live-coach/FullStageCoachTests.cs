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
        static TelemetryFrame Timed(int tick,double distance,double speedScale=1,double lane=0)
        {
            var f=Frame(tick,distance,100);
            f.Time=tick*.1*speedScale;
            f.StageElapsedSeconds=f.Time;
            f.Car="EA WRC 차량 (ID 미제공)"; f.Track="EA WRC 스테이지 (ID 미제공)";
            f.X=distance; f.Y=0; f.Z=lane;
            f.OnTrack=distance<100;
            return f;
        }
        static CoachPrincipleView Pass(FullStageCoach coach,double pace,double lane)
        {
            CoachPrincipleView result=null;
            // Quarter gates and the end fall between samples.
            for (int i=0;i<=34;i++) result=coach.Update(Timed(i,i*3,pace,lane));
            return result;
        }
        static Dictionary<string,object> Comparison(FullStageCoach c,string name)
        { return Part(c,name)["repeatComparison"] as Dictionary<string,object>; }
        static void CheckSkillFocus(Action<bool,string> check,bool capture)
        {
            var c=new FullStageCoach();
            CoachPrincipleView first=null;
            for(int i=0;i<=120;i++)
            {
                var f=capture?Acr(i):Frame(i,i,120);
                int step=i%40;
                f.Steering=step<7?.03:step<17?.45:step<23?.23:.05;
                f.Throttle=step<12 || i>=80?.12:.7;
                if (!capture) f.OnTrack=i<120;
                var v=c.Update(f);
                if (v.NewFeedback && v.Title.Contains("조향을 줄이기 전")) first=v;
            }
            var end=capture?c.FinishManually():c.LastReport;
            var run=Part(c,"lastRun");
            var focus=(Dictionary<string,object>)run["practiceFocus"];
            var evidence=(Dictionary<string,object>)focus["evidence"];
            check(first!=null && first.NextDrill.Contains("풀기 시작할 때") && first.PossibleMechanism.Contains("구동력"),
                "stage HUD preserves corner-specific action and possible mechanism");
            check((string)evidence["Skill"]=="power-before-unwind" && Convert.ToInt32(run["focusOccurrenceCount"])==2 &&
                (string)focus["next"]==end.NextDrill && end.PossibleMechanism==first.PossibleMechanism,
                "stage summary chooses repeated observation even after a later generic corner");
            check(evidence["TurnInMetres"]==null?capture:!capture && Math.Abs(Convert.ToDouble(evidence["TurnInMetres"])-47)<.01,
                "native turn-in metres when available");
            check((string)evidence["Clock"]==(capture?"capture":"simulation") && c.ReviewText().Contains(first.PossibleMechanism) &&
                c.ReviewText().Contains("같은 조작 순서 2회") && c.ReviewText().Contains(first.NextDrill),
                "measured evidence, location, actionable focus and principle preserved in review");
        }
        static void CheckRepeats(Action<bool,string> check)
        {
            var c=new FullStageCoach();
            check(!c.ArmRepeat(" ", "Rally1") && !c.ArmRepeat("Vissi", ""), "both per-run labels needed");
            c.ArmRepeat("Wrong direction","Puma Rally1");
            check(c.RepeatWaitingForStart && c.CancelPendingRepeat() && !c.RepeatAttemptOpen && !c.CancelPendingRepeat(),
                "can cancel and rename an attempt while waiting without any driving frames");
            Pass(c,1,0);check(Comparison(c,"lastRun")==null,"unarmed run never becomes a reference");
            c.ArmRepeat("Vissi forward","Puma Rally1");
            c.Update(Timed(20,40));check(c.RepeatAttemptOpen && Comparison(c,"current")==null,"arming late waits for the upcoming start");
            var first=Pass(c,1,0);
            check(!c.RepeatAttemptOpen && first.Observation.Contains("내 기준 저장") && c.ReviewText().Contains("75–100%"),"first fully observed run sets reference");
            var a=Comparison(c,"lastRun");var gates=(IList)a["splits"];
            var firstGate=(Dictionary<string,object>)gates[0];
            check(gates.Count==4 && Math.Abs(Convert.ToDouble(firstGate["gameSeconds"])-.8333333)<.00001 &&
                firstGate["differenceSeconds"]==null,"native split interpolated in stage timer, no fabricated zero delta");
            Pass(c,.9,1);check(Comparison(c,"lastRun")==null,"a retry cannot borrow old driver ID confirmation");
            c.ArmRepeat("Vissi forward","Puma Rally1");
            for(int i=0;i<=10;i++)c.Update(Timed(i,i*3,1.2,1));
            check(c.RepeatAttemptOpen && c.Update(Timed(11,33,1.2,1)).Status.Contains("1구간") && c.ReviewText().Contains("증가"),"live distance split compared before finish");
            for(int i=12;i<=34;i++)c.Update(Timed(i,i*3,1.2,1));
            var second=Comparison(c,"lastRun");var secondGate=(Dictionary<string,object>)((IList)second["splits"])[0];
            check(c.LastReport.Observation.Contains("전체 0.7초 증가") && Math.Abs(Convert.ToDouble(secondGate["differenceSeconds"])-1.0/6)<.0001,
                "fixed baseline, positive zone and stage loss");
            c.ArmRepeat("Vissi forward","Puma Rally1");
            check(Pass(c,.75,1).Observation.Contains("전체 0.8초 단축"),"third run still uses first baseline");
            c.ArmRepeat("Vissi forward","Puma Rally1");
            c.Update(Timed(0,0)); for(int i=1;i<=34;i++)c.Update(Timed(i,i*3,1,i>=15?150:0));
            var wrong=Comparison(c,"lastRun");
            check(c.LastReport.Observation.Contains("실제 위치가 다름") && ((Dictionary<string,object>)((IList)wrong["splits"])[0])["differenceSeconds"]==null,
                "diverging named course suppresses comparisons, including partial exported deltas");
            c.ArmRepeat("Vissi reverse","Puma Rally1");check(Pass(c,1.2,1).Observation.Contains("내 기준 저장"),"different driver-declared direction has its own baseline");
            c.ArmRepeat("Vissi forward","Rally2");check(Pass(c,1,1).Observation.Contains("내 기준 저장"),"different vehicle has its own baseline");
            c.ArmRepeat("Vissi forward","Puma Rally1");
            c.Update(Timed(0,0)); c.Update(Timed(1,3)); c.Interrupt();
            check(!c.RepeatAttemptOpen && c.LastReport.Observation.Contains("비교 보류"),"interruption cannot replace a baseline");
            c.ArmRepeat("Vissi forward","Puma Rally1");
            for(int i=0;i<=34;i++){var f=Timed(i,i*3);f.X=Double.NaN;c.Update(f);}
            check(c.LastReport.Observation.Contains("위치나"),"missing source position holds timing comparison");
            c.Reset();
            c.ArmRepeat("Hafren South","Alpine");
            for(int i=0;i<=30;i++){var f=Acr(i);f.Brake=.3;f.Steering=i<15?.3:0;c.Update(f);}
            check(c.FinishManually().Observation.Contains("내 기준 저장"),"first manually ended ACR observed pass freezes input profile");
            c.ArmRepeat("Hafren South","Alpine");
            for(int i=0;i<=30;i++){var f=Acr(i);f.Brake=.3;f.Steering=i<6?.3:0;c.Update(f);}
            var observed=c.FinishManually();var acr=Comparison(c,"lastRun");
            check(observed.Observation.Contains("30.0%p 감소") && ((IList)acr["splits"]).Count==0 &&
                Math.Abs(Convert.ToDouble(acr["brakeSteerPercent"])-20)<.001,
                "ACR compares measured time share, never a geographical or elapsed-time split");
            c.ArmRepeat("Hafren South","Alpine");
            for(int i=0;i<=20;i++){var f=Acr(i);f.Car="Other native car";c.Update(f);}
            check(c.FinishManually().Observation.Contains("내 기준 저장"),"ACR native car also participates in identity");
            c.ArmRepeat("Hafren South","Alpine");
            for(int i=0;i<=30;i++)c.Update(Acr(i));
            c.Interrupt();check(c.FinishManually().Observation.Contains("비교 보류"),"post-disconnect manual recap cannot certify the observed interval");
            c.Reset();c.ArmRepeat("Vissi forward","Puma Rally1");
            check(Pass(c,1,0).Observation.Contains("내 기준 저장"),"explicit practice change clears references");
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
            check(coach.ReviewText().Contains("75–100%") && coach.ReviewText().Contains("제동을 풀 때") &&
                coach.ReviewText().Contains("같은 진입 속도"),"section and corner principle/drill available inside app");
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
            CheckRepeats(check);
            CheckSkillFocus(check,false); CheckSkillFocus(check,true);
            return count;
        }
    }
}
