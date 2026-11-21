using System;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    internal static class ContinuousDriftCoachTests
    {
        sealed class Drive
        {
            public readonly ContinuousDriftCoach Coach = new ContinuousDriftCoach();
            public readonly TelemetryFrame Car = new TelemetryFrame {
                Time=0, OnTrack=true, Car="fixture", Track="practice", GameVersion="AC", TimeIsCaptureClock=true
            };
            public CoachPrincipleView Send(double angle, double yaw, int ticks, double speed = 10)
            {
                Car.SpeedKph = speed * 3.6;
                Car.LocalVelocitySideAbs = speed * Math.Sin(angle * Math.PI / 180);
                Car.LocalVelocityForward = speed * Math.Cos(angle * Math.PI / 180);
                Car.YawRate = yaw;
                CoachPrincipleView view = null;
                for (int i = 0; i < ticks; i++) { Car.Time += .05; view = Coach.Update(Car); }
                return view;
            }
        }
        public static int Run()
        {
            int count = 0;
            Action<bool,string> check = (ok,reason) => { if (!ok) throw new Exception("Continuous drift: " + reason); count++; };
            var d = new Drive();
            check(!d.Send(2, 0, 10).HasObservation, "road travel waits for a slide");
            d.Send(20,.5,3); d.Send(0,0,5);
            check(d.Coach.Snapshot().Current == null, "short spikes cannot start a chain");
            check(d.Send(25,.6,16).Status.Contains("연결 0회"), "live progress on first sustained side");
            double slide1 = d.Coach.Snapshot().Current.SlideSeconds;
            d.Send(0,0,12);
            check(d.Coach.Snapshot().Current.SlideSeconds == slide1, "neutral gap does not add slide seconds");
            var transfer=d.Send(23,-.6,12);
            check(transfer.Title.Contains("방향으로 연결") && transfer.Status.Contains("연결 1회"), "persistent transfer overlay and progress");
            check(d.Coach.Snapshot().Current.Transitions==1, "first reversal counted once");
            check(!d.Coach.Update(d.Car).NewFeedback, "duplicate sample never repeats an event");
            d.Send(20,.6,2); d.Send(22,-.6,8);
            check(d.Coach.Snapshot().Current.Transitions==1, "opposite yaw spike rejected");
            d.Send(1,0,14); d.Send(24,.6,12); d.Send(3,0,12); d.Send(23,-.7,13);
            var run=d.Coach.Snapshot().Current;
            check(run.Transitions==3 && run.Attempt==1, "multiple corners continue same chain");
            check(run.DurationSeconds-run.SlideSeconds>=1.5 && run.SlideSeconds>1.5, "duration and slide time remain distinct");
            d.Send(4,.05,32);
            var recap=d.Coach.Snapshot();
            check(recap.Current==null && recap.FinishedChains==1 && recap.Last.Transitions==3 && recap.Best.Transitions==3, "long straight finalises run once");
            check(d.Coach.LastReport != null && !d.Coach.LastReport.NewFeedback, "recap available even while waiting");
            recap.Last.Transitions=80;
            check(d.Coach.Snapshot().Last.Transitions==3, "immutable snapshot of finished run");
            d.Send(22,.5,16); d.Send(22,-.5,13); d.Send(2,0,2,1);
            check(d.Coach.Snapshot().Last.Attempt==2 && d.Coach.Snapshot().Best.Attempt==1, "low speed terminates only current run");
            check(d.Coach.Snapshot().Last.Clock=="capture", "host clock provenance");
            d.Send(25,.6,14); d.Coach.Update(null);
            check(d.Coach.Snapshot().Current==null && d.Coach.Snapshot().FinishedChains==3, "disconnect closes run and preserves recap");
            d.Coach.Update(null);
            check(d.Coach.Snapshot().FinishedChains==3, "repeated missing samples do not double count reports");
            d.Send(25,.6,15); d.Car.InPit=true; d.Send(25,-.6,1);
            check(d.Coach.Snapshot().Current==null && d.Coach.Snapshot().Last.Transitions==0, "pit interrupts");
            d.Car.InPit=false; d.Car.TimeIsCaptureClock=false; d.Send(25,.6,16);
            check(d.Coach.Snapshot().FinishedChains==0, "changed clock separates sessions");
            d.Car.Time += 1; d.Send(25,-.6,12);
            check(d.Coach.Snapshot().Current.Transitions==0 && d.Coach.Snapshot().FinishedChains==1, "missing frames cannot bridge discontinuity");
            d.Car.LocalVelocitySideAbs=double.NaN; d.Car.Time+=.05;
            check(!d.Coach.Update(d.Car).HasObservation && d.Coach.Snapshot().Current==null, "missing channel stops coach");
            check(!new JavaScriptSerializer().Serialize(d.Coach.Snapshot()).Contains("NaN"), "export remains valid JSON");
            d.Coach.Reset();
            check(d.Coach.LastReport==null && d.Coach.Snapshot().Recent.Length==0, "explicit reset clears past reports");
            d=new Drive();
            d.Car.Car="WDC fixture"; d.Car.Track="driftplayground_2021";
            d.Car.Throttle=.75; d.Send(23,.6,14); d.Send(1,0,4); d.Send(20,-.6,4);
            var missed=d.Send(2,.03,24);
            check(missed.Title.Contains("반대 회전이 짧게") && missed.Observation.Contains("0.2초") &&
                missed.NextDrill.Contains("첫 원의 끝"),"short opposite rotation gets a measured transition drill");
            check(d.Coach.Snapshot().Last.Diagnosis.EventId==missed.EventId &&
                d.Coach.ReviewText().Contains(missed.PossibleMechanism),"chain retains and exports the diagnosis");
            check(!d.Send(2,.03,1).NewFeedback,"the diagnosis is announced once");
            d=new Drive(); d.Car.Throttle=.2; d.Send(22,.5,12); d.Car.Throttle=.9; d.Send(22,.5,5);
            d.Send(40,.7,4); var spin=d.Send(86,1,8);
            check(spin.Observation.Contains("각도가 6° 이상 커지기 전 가속") && d.Coach.LastReport.NextDrill.Contains("올리는 속도"),
                "sustained post-slide rotation diagnosed on a full route");
            d=new Drive(); d.Send(24,.5,13); d.Send(1,0,35);
            check(d.Coach.LastReport.Observation.Contains("반대 옆미끄러짐") && !d.Coach.LastReport.Observation.Contains("가속"),
                "missing pedals still support motion diagnosis without an invented pedal change");
            d.Car.Car="different mod"; d.Car.LocalVelocityForward=double.NaN; d.Coach.Update(d.Car);
            check(d.Coach.LastReport==null,"a different car cannot inherit the last report even when its motion signal is missing");
            d=new Drive(); check(!d.Send(2,0,37).HasObservation && d.Coach.LastReport==null,
                "a normal straight with no preceding slide is not a failed transition");
            return count;
        }
    }
}
