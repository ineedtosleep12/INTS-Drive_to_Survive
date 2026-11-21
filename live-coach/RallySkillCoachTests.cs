using System;

namespace IntsLiveCoach
{
    internal static class RallySkillCoachTests
    {
        static TelemetryFrame Frame(int tick, double speed, double steer, double brake)
        {
            return new TelemetryFrame { OnTrack = true, Time = tick * .1, Car = "car", Track = "stage",
                SpeedKph = speed, Steering = steer, Brake = brake, Throttle = .25, Gear = 3 };
        }
        static CoachPrincipleView ExitOrder(int throttleTick, bool captureClock)
        {
            var coach = new RallySkillCoach();
            for (int i = 0; i < 40; i++)
            {
                var f = Frame(i, 40, i < 7 ? 0 : i < 17 ? .45 : i < 23 ? .23 : .05, i < 5 ? .3 : 0);
                f.Throttle = i >= throttleTick ? .7 : .1;
                f.TimeIsCaptureClock = captureClock;
                var shown = coach.Update(f);
                if (shown.NewFeedback) return shown;
            }
            throw new Exception("Rally turn did not finish");
        }
        static CoachPrincipleView Turn(RallySkillCoach coach, int firstTick, bool brakeAtTurnIn)
        {
            CoachPrincipleView view = null;
            for (int i = 0; i <= 20; i++)
            {
                bool turning = i >= 7 && i <= 14;
                double pedal = i >= 2 && i <= (brakeAtTurnIn ? 9 : 5) ? .4 : 0;
                view = coach.Update(Frame(firstTick + i, i < 7 ? 48-i : 32, turning ? -.35 : .05, pedal));
                if (view.NewFeedback) break;
            }
            return view;
        }
        static CoachPrincipleView InputSequence(RallySkillCoach coach, Action<int,TelemetryFrame> set)
        {
            for (int i=0;i<42;i++)
            {
                var f=Frame(i,i<7?50-i:34,i<7?.03:i<17?.45:i<29?.23:.05,0);
                f.Throttle=.22;
                set(i,f);
                var cue=coach.Update(f);
                if (cue.NewFeedback) return cue;
            }
            throw new Exception("Rally sequence did not finish");
        }
        public static int Run()
        {
            int assertions = 0;
            Action<bool,string> check = (value, name) => {
                if (!value) throw new Exception("Rally skill: " + name);
                assertions++;
            };
            var coach = new RallySkillCoach();
            var result = Turn(coach, 0, true);
            check(result.NewFeedback && result.HasObservation, "steer/pedal/speed alone supports a completed input turn");
            check(result.Observation.Contains("0.3초") && result.Observation.Contains("48") && result.Observation.Contains("32"), "measures first release and approach/entry speeds");
            var duplicate = coach.Update(Frame(20, 32, .05, 0));
            check(!duplicate.NewFeedback && duplicate.EventId == result.EventId, "readback does not issue a second message");
            var second = Turn(coach, 21, false);
            check(second.NewFeedback && second.EventId != result.EventId && second.Observation.Contains("접근 중 제동"), "second turn uses its own pedal history");
            coach.Reset();
            for (int i = 0; i < 10; ++i) coach.Update(Frame(i, 40, .05, .4));
            coach.Update(Frame(10, 40, .4, .3));
            for (int i = 11; i < 16; ++i) coach.Update(Frame(i, 36, .4, .1));
            check(!coach.Update(Frame(16, 34, -.4, .05)).HasObservation, "direction reversal interrupts the single corner exercise");
            for (int i = 17; i < 27; ++i) check(!coach.Update(Frame(i, 34, .02, 0)).NewFeedback, "no recap for interrupted direction");
            var corrupt = Frame(27, 35, .2, .1); corrupt.Brake = double.NaN;
            check(!coach.Update(corrupt).HasObservation, "missing pedal data blocks observations");
            var replay = Frame(28, 38, .4, .4); replay.IsReplay = true;
            check(!coach.Update(replay).HasObservation, "replay rejected");
            for (int i = 29; i < 39; ++i) coach.Update(Frame(i, 40, .05, .4));
            coach.Update(Frame(39, 40, .4, .2));
            check(!coach.Update(Frame(49, 40, .4, .2)).HasObservation, "long gap discards active turn");
            coach.Reset();
            for (int i = 0; i < 8; ++i) coach.Update(Frame(i, 40, .05, .4));
            coach.Update(Frame(8, 40, .4, .3));
            var restarted = Frame(9, 40, .4, .2); restarted.Track = "another stage";
            check(!coach.Update(restarted).HasObservation, "context changes discard active turn");
            check(ExitOrder(12, false).Observation.Contains("0.5초 전"), "sustained throttle increase before steering unwind");
            check(ExitOrder(12,false).Title.Contains("조향을 줄이기 전") && ExitOrder(12,false).NextDrill.Contains("풀기 시작할 때"),
                "early power gets its own hypothesis and repeatable drill");
            check(ExitOrder(20, true).Observation.Contains("0.3초 후") && ExitOrder(20, true).Limits.Contains("수신 시각"), "later increase measured using the capture clock");
            check(ExitOrder(17, false).Observation.Contains("비슷한 때"), "near-simultaneous release and power");
            check(!ExitOrder(0, false).Observation.Contains("재입력"), "constant high throttle is not counted");
            coach.Reset();
            var brake=InputSequence(coach,(i,f)=>{f.Brake=i<24?.4:0;});
            check(coach.LastCorner.Skill=="brake-through-unwind" && Math.Abs(coach.LastCorner.FootBrakeAfterUnwindSeconds-.7)<.001 &&
                brake.Observation.Contains("0.7초") && brake.NextDrill.Contains("압력"),"held brake after measured unwind gets pressure-release drill");
            coach.Reset();
            InputSequence(coach,(i,f)=>{f.Brake=i<23 && i!=14?.4:0;});
            check(coach.LastCorner.Skill==null && Math.Abs(coach.LastCorner.ReleaseAfterTurnInSeconds.Value-1.6)<.001,
                "isolated low pedal does not confirm release; interrupted initial pressure not described as held continuously");
            coach.Reset();
            var low=InputSequence(coach,(i,f)=>{f.Throttle=i>=16 && i<26?.05:.22;f.TimeIsCaptureClock=true;});
            check(coach.LastCorner.Skill=="low-pedals-on-exit" && low.Observation.Contains("8%") && low.Limits.Contains("수신 시각") &&
                !coach.LastCorner.HandbrakeAfterUnwindSeconds.HasValue,"measured small pedals with unknown handbrake, timer provenance");
            coach.Reset();
            var handbrake=InputSequence(coach,(i,f)=>{f.Handbrake=i>=16 && i<24?.9:0;});
            check(coach.LastCorner.Skill=="handbrake-through-unwind" && handbrake.NextDrill.Contains("핸드브레이크를 놓고") &&
                coach.LastCorner.HandbrakeAfterUnwindSeconds>.5,"available handbrake channel supports an actionable observation");
            coach.Reset();
            InputSequence(coach,(i,f)=>{f.Handbrake=i==19?.9:0;});
            check(coach.LastCorner.Skill==null && coach.LastCorner.HandbrakeAfterUnwindSeconds==0,"momentary handbrake not an exit pattern");
            coach.Reset();
            for (int i = 0; i < 8; ++i) coach.Update(Frame(i, 40, 0, .4));
            var reverse = Frame(8, 38, .4, .2); reverse.Gear = -1;
            check(!coach.Update(reverse).HasObservation, "native reverse with missing forward velocity resets the exercise");
            return assertions;
        }
    }
}
