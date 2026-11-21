using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    internal static class TougeRunCoachTests
    {
        static void Check(bool ok,string message)
        { if (!ok) throw new Exception("Touge route: "+message); checks++; }
        static int checks;
        sealed class Drive
        {
            public readonly TougeRunCoach Coach=new TougeRunCoach();
            public readonly TelemetryFrame Frame=new TelemetryFrame {OnTrack=true, Time=0, Car="AE86 Drift",
                GameVersion="AC 1",Track="practice/downhill",SpeedKph=36,TimeIsCaptureClock=true};
            public CoachPrincipleView Send(double x,double z,double yaw)
            {
                Frame.Time+=.05; Frame.X=x; Frame.Z=z; Frame.YawRate=yaw;
                double angle=(yaw==0?0:25)*Math.PI/180, speed=Frame.SpeedKph/3.6;
                Frame.LocalVelocityForward=speed*Math.Cos(angle); Frame.LocalVelocitySideAbs=speed*Math.Sin(angle);
                return Coach.Update(Frame);
            }
            public void Route(bool linked,double shape=0,double start=0,bool missingPosition=false)
            {
                Coach.Start();
                for (int i=0;i<=150;i++)
                {
                    double yaw=i<25?.6:i<50?-.6:i<85?(linked?.6:0):i<110?.6:-.6;
                    Send(start+i*.5, missingPosition?double.NaN:shape*Math.Sin(i*Math.PI/150), yaw);
                }
            }
            public Dictionary<string,object> State()
            {
                var js=new JavaScriptSerializer();
                return (Dictionary<string,object>)js.DeserializeObject(js.Serialize(Coach.Snapshot()));
            }
            public Dictionary<string,object> Run { get { return (Dictionary<string,object>)State()["run"]; } }
        }
        public static int Run()
        {
            checks=0;
            var d=new Drive(); var coach=d.Coach;
            Check(!coach.IsRecording && coach.FinishManually()==null,"manual start is required");
            coach.Start(); coach.Interrupt();
            Check(coach.IsRecording && d.State()["run"]==null,"arming before the first frame survives disconnect");
            d.Frame.SpeedKph=0; d.Send(0,0,0);
            Check(d.State()["run"]==null,"standing at the departure marker excludes idle time");
            Check(coach.FinishManually()!=null && !coach.IsRecording,"unstarted run can be cancelled");
            d.Frame.SpeedKph=36;
            d.Route(false); var recap=coach.FinishManually();
            Check(recap!=null && recap.LessonId=="drift-touge" && recap.HasObservation,"whole route recap delivered");
            Check(Convert.ToInt32(d.Run["transitions"])==2 && Convert.ToInt32(d.Run["longestChainTransitions"])==1,
                "links across two separated chains accumulate without joining the intervening straight");
            double slide=Convert.ToDouble(d.Run["slideSeconds"]);
            Check(slide>5 && slide<6 && Convert.ToDouble(d.Run["observedSeconds"])>7.4,"observed time distinct from slide time");
            Check(d.State()["previous"]==null && !coach.Update(d.Frame).NewFeedback,"first summary retained without repeated cue");
            d.Route(true); coach.FinishManually();
            Check(Convert.ToInt32(d.Run["transitions"])==3 && Convert.ToInt32(d.Run["longestChainTransitions"])==3,"linked course registers uninterrupted alternate turns");
            Check(d.State()["previous"]!=null && coach.ReviewText().Contains("연결 2 → 3회") && coach.ReviewText().Contains("주행거리 "),
                "spatially matched repeat identifies change across route and quarter");
            Check(d.Run["clock"].Equals("capture") && Math.Abs(Convert.ToDouble(d.Run["pathMetres"])-75)<.01,"clock and measured path length exported");
            d.Route(true,25); coach.FinishManually();
            d.Route(true,-25); coach.FinishManually();
            Check(d.State()["previous"]==null,"same endpoints but different intermediate path do not compare");
            d.Route(true,0,150); coach.FinishManually();
            Check(d.State()["previous"]==null,"different start/finish markers do not compare");
            d.Frame.Car="another car"; d.Route(false); coach.FinishManually();
            Check(d.State()["previous"]==null,"car identity required");
            d.Frame.Car="AE86 Drift"; d.Frame.Track="practice/uphill"; d.Route(false); coach.FinishManually();
            Check(d.State()["previous"]==null,"track layout required");
            d.Frame.Track="practice/downhill"; d.Frame.TimeIsCaptureClock=false; d.Route(false); coach.FinishManually();
            Check(d.State()["previous"]==null,"different clock not compared");
            d.Frame.TimeIsCaptureClock=true; d.Route(false,0,0,true); coach.FinishManually();
            Check(d.State()["previous"]==null && d.Run["pathMetres"]==null,"missing native X/Z leaves summary without geometry comparison");
            d.Route(true);
            coach.Interrupt(); string stopped=coach.ReviewText(); d.Send(900,900,-.5);
            Check(coach.CanFinishManually && coach.ReviewText()==stopped,"focus loss retains the last observed endpoint");
            recap=coach.FinishManually();
            Check(d.Run["state"].Equals("manual-after-signal") && d.State()["previous"]!=null &&
                recap.Title.Contains("마지막 수신"),"driver can explicitly finish at the frozen endpoint");
            coach.Start(); d.Send(0,0,.6); d.Send(100,0,.6);
            Check(!coach.IsRecording && d.Run["state"].Equals("interrupted") && d.State()["previous"]==null,
                "a teleport cannot bridge a route");
            coach.Start(); d.Send(0,0,.6); d.Frame.Time+=1; d.Send(1,0,.6);
            Check(!coach.IsRecording && coach.LastReport.Limits.Contains("시계"),"clock gap interrupts");
            coach.Start(); d.Send(0,0,.6); d.Frame.InPit=true; d.Send(1,0,.6);
            Check(!coach.IsRecording && !coach.CanFinishManually,"pit entries cannot become completed comparisons");
            d.Frame.InPit=false; coach.Start(); d.Send(0,0,.6); d.Frame.Track="other"; d.Send(.5,0,.6);
            Check(!coach.IsRecording,"context change mid-route interrupts");
            coach.Reset();
            Check(coach.LastReport==null && d.State()["run"]==null && coach.ReviewText()=="","reset clears history and pending run");
            return checks;
        }
    }
}
