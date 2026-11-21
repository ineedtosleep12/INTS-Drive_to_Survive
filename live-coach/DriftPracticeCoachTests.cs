using System;

namespace IntsLiveCoach
{
    public static class DriftPracticeCoachTests
    {
        sealed class Drive
        {
            internal readonly DriftPracticeCoach Coach = new DriftPracticeCoach();
            internal readonly TelemetryFrame Frame = new TelemetryFrame {
                Time = 0, OnTrack = true, Car = "Fixture", Track = "Skidpad", GameVersion = "AC Fixture", Throttle = .5
            };
            internal string Lesson = "drift-donut";
            internal CoachPrincipleView Send(double angle, double yaw, int count, double metresPerSecond = 10)
            {
                CoachPrincipleView view = null;
                Frame.SpeedKph = metresPerSecond * 3.6;
                Frame.LocalVelocityForward = metresPerSecond * Math.Cos(angle * Math.PI / 180);
                Frame.LocalVelocitySideAbs = metresPerSecond * Math.Sin(angle * Math.PI / 180);
                Frame.YawRate = yaw;
                for (int i = 0; i < count; i++)
                {
                    Frame.Time += .05;
                    view = Coach.Update(Frame, Lesson);
                }
                return view;
            }
        }

        static int count;
        static void Check(bool ok, string reason) { count++; if (!ok) throw new Exception("Drift practice: " + reason); }

        public static int Run()
        {
            count = 0;
            var drive = new Drive();
            Check(!drive.Send(0, 0, 1).HasObservation, "straight line is a preparation cue");
            Check(drive.Send(2, .4, 18).Title.Contains("선회 중"), "grip-only circle receives its own cue before a slide");
            Check(drive.Send(20, .5, 1).Title.Contains("시작"), "responds to measured onset during slide");
            Check(drive.Send(20, .5, 12).Title.Contains("유지"), "same direction sustained");
            Check(!drive.Send(20, .5, 1).NewFeedback, "unchanged phase updates without repeated event");
            Check(drive.Send(35, .65, 9).Title.Contains("커지는"), "growing angle is observed");
            Check(drive.Send(15, .3, 9).Title.Contains("줄어드는"), "decreasing angle is observed");
            Check(drive.Send(3, .05, 1).Observation.Contains("직전"), "end reports completed duration");

            drive.Lesson = "drift-transition";
            Check(drive.Send(20, .5, 12).Title.Contains("첫 방향"), "new lesson starts a new attempt");
            drive.Send(4, .03, 5);
            Check(drive.Send(22, -.6, 3).Title.Contains("시작"), "one opposite yaw spike cannot complete transition");
            Check(drive.Send(22, -.6, 10).Title.Contains("반대 방향"), "sustained opposite rotation links across neutral");
            drive.Send(2, 0, 30);
            Check(drive.Send(22, .6, 12).Title.Contains("첫 방향"), "long straight breaks transition");
            drive.Frame.InPit = true;
            Check(!drive.Send(18, -.6, 2).HasObservation, "pit clears active state");
            drive.Frame.InPit = false;
            Check(drive.Send(18, -.6, 12).Title.Contains("첫 방향"), "cannot link across pit");
            drive.Frame.LocalVelocitySideAbs = double.NaN;
            Check(!drive.Coach.Update(drive.Frame, drive.Lesson).HasObservation, "missing motion clears coaching");
            drive.Frame.LocalVelocitySideAbs = 120;
            Check(!drive.Coach.Update(drive.Frame, drive.Lesson).HasObservation, "inconsistent local/world speed blocks angle");
            drive.Send(20, .5, 12);
            drive.Frame.Time += 2;
            Check(drive.Send(20, -.5, 12).Title.Contains("첫 방향"), "disconnected gap clears previous side");
            drive.Frame.Track = "another";
            Check(drive.Send(20, .5, 12).Title.Contains("첫 방향"), "track changes clear history");
            Check(!drive.Coach.Update(drive.Frame, drive.Lesson).NewFeedback, "duplicate frame never repeats a cue");
            drive.Frame.IsReplay = true;
            Check(!drive.Send(20, -.6, 1).HasObservation, "replay cannot deliver a cue");
            drive = new Drive();
            drive.Lesson = "drift-transition";
            Check(!drive.Send(88, .8, 8).Title.Contains("계속 돌아요"), "no unobserved slide inferred from an initial rotation");
            drive.Send(24, .65, 14);
            Check(!drive.Send(86, .9, 2).Title.Contains("계속 돌아요"), "brief transient does not deliver rotation cue");
            Check(drive.Send(87, .8, 7).Title.Contains("계속 돌아요"), "loss of forward travel with sustained rotation is distinguished");
            Check(drive.Send(22, -.7, 14).Title.Contains("첫 방향"), "a rotating low-forward episode breaks a transition");
            Check(drive.Send(3, -.05, 1).Title.Contains("펴졌어요"), "straightening has a different correction");
            Check(drive.Send(1, 0, 1, 2).Title.Contains("속도가 낮아져"), "low speed has its own cue without a noisy angle");
            drive.Send(24, -.8, 12);
            Check(!drive.Send(85, .7, 8).Title.Contains("계속 돌아요"), "rotation reversal does not substantiate continued rotation");

            drive=new Drive();
            drive.Frame.Car="wdts_nissan_silvia_s15"; drive.Frame.Track="skidpad05"; drive.Frame.Throttle=.8;
            var grip=drive.Send(3,.4,20);
            Check(grip.Title.Contains("선회 중") && grip.Observation.Contains("최대 3.0°") &&
                drive.Coach.ReviewText().Contains("원의 반지름"),"grip-only attempt has a retained, pedal-aware beginner exercise");
            drive.Frame.Steering=.6; drive.Send(25,.7,12);
            drive.Frame.Throttle=.35; drive.Send(25,.7,5); drive.Frame.Steering=-.2;
            var flat=drive.Send(3,.05,1);
            Check(flat.NewFeedback && flat.Observation.Contains("각도가 6° 이상 줄기 전 가속 80% → 35%") &&
                flat.Observation.Contains("조향 크기 60% → 20%") && flat.NextDrill.Contains("가속을 덜어내는 속도"),
                "a measured throttle lift preceding straightening changes the drill without assuming countersteer direction");
            Check(!drive.Send(2,.05,4).NewFeedback && drive.Coach.LastReport.EventId==flat.EventId,
                "straightening does not generate a fresh diagnosis on every sample");
            drive.Coach.Interrupt();
            Check(drive.Coach.ReviewText().Contains("가속 80% → 35%") && !drive.Coach.LastReport.NewFeedback,
                "retain completed evidence across a signal interruption");
            drive.Frame.Track="driftplayground_2021"; drive.Send(1,0,2);
            Check(drive.Coach.LastReport==null,"new course has no prior diagnosis");
            drive.Frame.Throttle=.8; drive.Send(23,.5,14); drive.Frame.Throttle=.25;
            var simultaneous=drive.Send(3,.04,1);
            Check(simultaneous.Observation.Contains("가속 80% → 25%") && !simultaneous.Observation.Contains("줄기 전") &&
                !simultaneous.NextDrill.Contains("덜어내는 속도"),"concurrent changes do not get a pedal-lead diagnosis");
            drive=new Drive(); drive.Frame.Throttle=.25; drive.Send(22,.6,12);
            drive.Frame.Throttle=.85; drive.Send(22,.6,5); drive.Send(38,.7,4);
            var rotated=drive.Send(86,.9,7);
            Check(rotated.Observation.Contains("앞쪽 속도") && rotated.Observation.Contains("각도가 6° 이상 커지기 전 가속 25% → 85%") &&
                rotated.NextDrill.Contains("가속을 올리는 속도"),"sustained rotation uses input chronology");
            drive.Coach.Reset(); Check(drive.Coach.ReviewText()=="" && drive.Coach.LastReport==null,"explicit reset clears debrief");
            drive=new Drive(); drive.Lesson="drift-transition"; drive.Send(24,.6,16);
            drive.Send(2,.03,3); drive.Send(22,-.65,4);
            var shortTransfer=drive.Send(2,.03,23);
            Check(shortTransfer.Title.Contains("반대 회전이 짧게") && shortTransfer.Observation.Contains("0.2초"),
                "eight-figure diagnosis uses the last sustained side, even if an unconfirmed opposite side has since ended");
            drive.Frame.Track="new mod track"; drive.Frame.LocalVelocitySideAbs=double.NaN;
            drive.Coach.Update(drive.Frame,drive.Lesson);
            Check(drive.Coach.LastReport==null,"an unreadable first frame on a new course cannot display the previous course's diagnosis");
            return count;
        }
    }
}
