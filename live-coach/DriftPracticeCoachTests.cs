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
            return count;
        }
    }
}
