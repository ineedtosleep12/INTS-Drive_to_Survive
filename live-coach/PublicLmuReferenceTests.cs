using System;
using System.Linq;

namespace IntsLiveCoach
{
    // Synthetic, privately generated samples: a partial public recording with no game-reported
    // length, paired with a separate full native lap. No downloaded player samples are embedded.
    public static class PublicLmuReferenceTests
    {
        private const int Length = 1200;
        private static int checks;
        private static void Check(bool value, string description)
        {
            checks++;
            if (!value) throw new Exception("Public LMU reference: " + description);
        }

        private static ReferenceLap Lap(bool publicRecording, bool anchored)
        {
            string track = anchored ? "Synthetic Fuji" : "Synthetic Imola";
            var lap = new ReferenceLap {
                Game = "LMU", Car = "Synthetic Porsche", Track = track, Layout = "GP",
                GameVersion = "fixture", Validity = publicRecording ? "unknown" : "valid",
                Complete = !publicRecording, TrackLengthM = publicRecording ? Double.NaN : Length,
                CoordinateFrame = ReferenceLap.LmuCoordinateFrame,
                DistanceBasis = publicRecording && anchored ? ReferenceLap.LmuAnchoredDistanceBasis : ReferenceLap.LmuDistanceBasis,
                Samples = Enumerable.Range(publicRecording ? 1 : 0, publicRecording ? 119 : 121).Select(i => {
                    double distance = i * 10, angle = distance * 2 * Math.PI / Length;
                    bool turning = i >= 30 && i < 50;
                    return new ReferenceSample {
                        DistanceM = distance, Time = i * .2, Brake = i >= 30 && i < 40 ? .7 : 0,
                        Throttle = turning ? .25 : 1, Steering = turning ? .3 : 0, SpeedKph = 180,
                        X = Length * Math.Sin(angle) / (2 * Math.PI), Z = Length * Math.Cos(angle) / (2 * Math.PI)
                    };
                }).ToArray()
            };
            if (publicRecording) lap.Conditions["sourceLapExtent"] = "recorded-crossing-to-crossing";
            return lap;
        }

        private static TelemetryFrame Frame(ReferenceSample s, ReferenceLap own)
        {
            return new TelemetryFrame { Time = 100 + s.Time, Lap = 2, LapDistance = s.DistanceM,
                TrackLength = own.TrackLengthM, DistanceAgeSeconds = .1, Car = own.Car, Track = own.Track,
                GameVersion = own.GameVersion, OnTrack = true, ValidLapKnown = true, IsValidLap = true,
                SpeedKph = s.SpeedKph, Brake = s.Brake, Throttle = s.Throttle, Steering = s.Steering,
                LateralG = 0, X = s.X, Z = s.Z };
        }

        private static LiveReferenceCoach Configure(ReferenceLap source, bool allowAlignment)
        {
            var coach = new LiveReferenceCoach();
            coach.Configure(new ReferenceLiveSettings(source, true, false, allowAlignment));
            return coach;
        }

        private static bool EmptyHud(LiveReferenceView view)
        {
            return !view.Ready && !view.NewFeedback && view.ReferenceAtPosition == null &&
                String.IsNullOrEmpty(view.Heading) && String.IsNullOrEmpty(view.FeedbackId) &&
                String.IsNullOrEmpty(view.NextAction);
        }

        private static void CheckSource(bool anchored)
        {
            var source = Lap(true, anchored);
            var own = Lap(false, anchored);
            if (anchored)
            {
                Check(!ReferenceComparison.Compare(source, own).Comparable, "anchored offline comparison requires opt-in");
                var locked = Configure(source, false);
                Check(own.Samples.All(s => EmptyHud(locked.Update("LMU", Frame(s, own)))),
                    "anchored comparison disabled keeps every HUD frame clear");
                var noExtent = Lap(true, true);
                noExtent.Conditions.Clear();
                Check(!ReferenceComparison.Compare(noExtent, own, false, true).Comparable,
                    "opt-in alone cannot qualify unknown anchor extent");
            }

            var comparison = ReferenceComparison.Compare(source, own, false, anchored);
            Check(comparison.Comparable && comparison.HasLineComparison && comparison.HasTimeComparison,
                "matching native positions permit input, line and section timing");
            Check(comparison.Points.Length > 10 && comparison.Points.All(p => Math.Abs(p.DeltaSeconds) < 1e-6 &&
                Math.Abs(p.LineOffsetM) < 1e-6), "source mirror has no invented losses or line offset");
            Check(Double.IsNaN(comparison.LapDeltaSeconds), "unknown course length and unobserved endpoints yield no full-lap delta");
            if (anchored) Check(comparison.Warnings.Any(warning => warning.Contains("속도 적분")),
                "anchored position interpolation stays visible in the result");

            var alternate = Lap(false, anchored);
            alternate.Layout = "alternate";
            Check(!ReferenceComparison.Compare(source, alternate, false, anchored).Comparable, "explicit other layout");
            alternate.Layout = own.Layout;
            alternate.TrackLengthM = 1650;
            alternate.Complete = false;
            alternate.Validate();
            Check(!ReferenceComparison.Compare(source, alternate, false, anchored).Comparable,
                "implausible native length rejected even with identical coordinates");
            Check(EmptyHud(Configure(source, anchored).Update("LMU", Frame(alternate.Samples[5], alternate))),
                "wrong native length shows no reference pedal markers");

            var shifted = Lap(false, anchored);
            foreach (var s in shifted.Samples) s.Z += 90;
            Check(!ReferenceComparison.Compare(source, shifted, false, anchored).Comparable, "offset world origin refused");
            foreach (var s in shifted.Samples) { s.X = Double.NaN; s.Z = Double.NaN; }
            Check(!ReferenceComparison.Compare(source, shifted, false, anchored).Comparable,
                "missing world positions prevent time and line comparisons");
            var foreignCoordinates = Lap(true, anchored);
            foreignCoordinates.CoordinateFrame = "unknown";
            Check(!ReferenceComparison.Compare(foreignCoordinates, own, false, anchored).Comparable &&
                EmptyHud(Configure(foreignCoordinates, anchored).Update("LMU", Frame(own.Samples[5], own))),
                "numeric positions in an unidentified source frame cannot certify the layout");
            var coach = Configure(source, anchored);
            Check(coach.PreparedZoneCount == 1, "one fully observed braking complex");
            int events = 0;
            for (int i = 0; i < own.Samples.Length - 1; i++)
            {
                var v = coach.Update("LMU", Frame(own.Samples[i], own));
                if (!v.NewFeedback) continue;
                events++;
                Check(v.Ready && v.ReferenceAtPosition != null && v.Heading.Length > 0 &&
                    Math.Abs(v.TimeLossSeconds) < .000001 && Math.Abs(v.MeanAbsoluteLineOffsetM) < .000001,
                    "matching live mirror delivers timed corner feedback with zero line drift");
                var offset = Frame(own.Samples[++i], own);
                offset.X += 100;
                Check(EmptyHud(coach.Update("LMU", offset)), "position mismatch clears previous HUD cue immediately");
                var missing = Frame(own.Samples[++i], own);
                missing.X = missing.Z = Double.NaN;
                Check(EmptyHud(coach.Update("LMU", missing)), "missing positions keep the stale cue off screen");
                var resumed = coach.Update("LMU", Frame(own.Samples[++i], own));
                Check(resumed.Ready && resumed.ReferenceAtPosition != null && String.IsNullOrEmpty(resumed.Heading),
                    "return to valid positions restores reference inputs without replaying stale feedback");
            }
            Check(events == 1, "one delivered cue per fully observed complex");
            coach = Configure(source, anchored);
            bool withheld = true, skippedZone = true;
            foreach (var s in own.Samples)
            {
                var f = Frame(s, own);
                if (s.DistanceM == 390) f.Z += 60;
                var v = coach.Update("LMU", f);
                if (s.DistanceM == 390) withheld = EmptyHud(v);
                skippedZone &= !v.NewFeedback;
            }
            Check(withheld && skippedZone, "invalid position mid-corner discards just that incomplete observation");
        }

        public static int Run()
        {
            checks = 0;
            CheckSource(false);
            CheckSource(true);
            return checks;
        }
    }
}
