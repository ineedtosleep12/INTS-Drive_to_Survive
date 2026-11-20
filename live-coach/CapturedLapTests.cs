using System;

namespace IntsLiveCoach
{
    // Synthetic contract tests; they do not establish live game compatibility.
    public static class CapturedLapTests
    {
        static int assertions;
        static void Check(bool condition, string message)
        { assertions++; if (!condition) throw new Exception("Captured lap: " + message); }
        static TelemetryFrame[] Frames()
        {
            var result = new TelemetryFrame[101];
            for (int i = 0; i < result.Length; i++) result[i] = new TelemetryFrame {
                Time = 40 + i * .2, LapDistance = i * 10, TrackLength = 1000, Lap = 2,
                SpeedKph = 180, Brake = i >= 20 && i < 30 ? .7 : 0, Throttle = i >= 20 && i < 30 ? 0 : 1,
                Steering = .2, X = i * 10, Y = 9999, Z = 5, PathLateralM = -1.5 + i * .01, Car = "Porsche 992 #Fixture livery",
                Track = "Synthetic circuit / GP", GameVersion = "fixture-v1", OnTrack = true,
                ValidLapKnown = true, IsValidLap = true, DistanceAgeSeconds = .1,
                CaptureUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), FuelLitres = 25
            };
            return result;
        }
        static void Reject(TelemetryFrame[] frames, string message)
        {
            ReferenceLap lap; string error;
            Check(!CapturedLap.TryCreate("LMU", frames, out lap, out error) && lap == null && !string.IsNullOrEmpty(error), message);
        }
        public static int Run()
        {
            assertions = 0;
            var frames = Frames(); var lap = CapturedLap.Create("lmu", frames);
            Check(lap.Game == "LMU" && lap.Validity == "valid" && lap.Complete, "complete valid capture exports");
            Check(lap.Samples[0].Time == 0 && lap.Samples[100].Time == 20, "session clock normalized without stretching");
            Check(lap.Samples[0].X == 0 && lap.Samples[0].Z == 5, "XZ plane selected instead of elevation Y");
            Check(lap.DistanceBasis == ReferenceLap.LmuDistanceBasis && lap.CoordinateFrame == ReferenceLap.LmuCoordinateFrame, "documented LMU frames");
            Check(lap.LateralPositionBasis == "lmu-scoring-path-lateral-m" && lap.Samples[0].PathLateralM == -1.5,
                "native LMU signed lateral path offset preserved without a world-position conversion");
            Check(lap.Car == frames[0].Car && lap.Conditions["sourceCarLabel"] == frames[0].Car, "livery label preserved without car alias guess");
            Check(lap.Track == frames[0].Track && lap.Layout == "unknown", "unknown layout not inferred from display name");
            Check(lap.RecordedAt == "2026-01-01T12:00:00.0000000Z", "recording timestamp preserved");
            ReferenceLap copy = ReferenceLap.LoadJson(lap.ToJson());
            Check(copy.Samples.Length == 101 && copy.Conditions["sourceCarLabel"] == frames[0].Car, "capture round trips through reference transport");
            Check(copy.LateralPositionBasis == lap.LateralPositionBasis && copy.Samples[0].PathLateralM == -1.5,
                "native lateral evidence round trips through reference transport");
            frames[10].Brake = .9; Check(lap.Samples[10].Brake == 0, "export owns independent samples");
            foreach (string game in new string[] { "ACC", "AC", "AMS2" }) {
                var known = CapturedLap.Create(game, Frames());
                Check(known.CoordinateFrame == game.ToLowerInvariant() + "-world-xz-m", game + " world position frame");
                Check(known.DistanceBasis == (game == "AMS2" ? CapturedLap.Ams2DistanceBasis : game == "AC" ? CapturedLap.AcDistanceBasis : CapturedLap.AccDistanceBasis), game + " distance basis");
                Check(known.LateralPositionBasis == "unknown" && double.IsNaN(known.Samples[0].PathLateralM),
                    game + " does not inherit LMU lateral convention");
            }
            ReferenceLap missing; string reason;
            Check(!CapturedLap.TryCreate("EA WRC", Frames(), out missing, out reason), "rally stage not falsely called complete circuit lap");
            Reject(null, "missing recording rejected");
            frames = Frames(); frames[0] = null; Reject(frames, "missing boundary rejected");
            frames = Frames(); frames[0].LapDistance = 3; Reject(frames, "near start cannot invent line crossing");
            frames = Frames(); frames[100].LapDistance = 998; Reject(frames, "near finish cannot invent line crossing");
            frames = Frames(); frames[60].Lap = 3; Reject(frames, "mixed lap numbers rejected");
            frames = Frames(); frames[60].Car = "Porsche 992 different livery"; Reject(frames, "context change rejected despite matching model substring");
            frames = Frames(); frames[60].Track = "Other circuit"; Reject(frames, "mixed tracks rejected");
            frames = Frames(); frames[60].GameVersion = "different"; Reject(frames, "mixed versions rejected");
            frames = Frames(); frames[60].TrackLength += .1; Reject(frames, "mixed metre scales rejected");
            frames = Frames(); frames[0].Car = "unknown"; Reject(frames, "unknown identity rejected");
            frames = Frames(); frames[30].IsValidLap = false; Reject(frames, "single invalid sample rejected");
            frames = Frames(); frames[30].ValidLapKnown = false; Reject(frames, "unknown validity including AC base reader rejected");
            frames = Frames(); frames[30].InPit = true; Reject(frames, "pit visit rejected");
            frames = Frames(); frames[30].IsReplay = true; Reject(frames, "replay rejected");
            frames = Frames(); frames[30].OnTrack = false; Reject(frames, "inactive session rejected");
            frames = Frames(); frames[30].TimeIsCaptureClock = true; Reject(frames, "capture clock cannot be a lap clock");
            frames = Frames(); frames[30].Time = frames[29].Time; Reject(frames, "duplicate timestamp rejected");
            frames = Frames(); for (int i = 30; i < frames.Length; i++) frames[i].Time += 1; Reject(frames, "long gap rejected");
            frames = Frames(); frames[30].LapDistance = frames[29].LapDistance - 1; Reject(frames, "reverse distance rejected");
            frames = Frames(); frames[30].LapDistance += 300; Reject(frames, "teleport rejected");
            frames = Frames(); frames[30].Brake = double.NaN; Reject(frames, "missing brake is not zero");
            frames = Frames(); frames[30].DistanceAgeSeconds = .6; Reject(frames, "stale position rejected");
            frames = Frames(); frames[30].X = double.NaN; lap = CapturedLap.Create("LMU", frames);
            Check(double.IsNaN(lap.Samples[30].X) && double.IsNaN(lap.Samples[30].Z), "half-missing position remains unavailable");
            frames = Frames(); frames[30].PathLateralM = double.NaN; lap = CapturedLap.Create("LMU", frames);
            Check(double.IsNaN(lap.Samples[30].PathLateralM) && lap.Samples[29].PathLateralM == frames[29].PathLateralM,
                "missing lateral sample remains missing while neighbours are preserved");
            frames = Frames(); foreach (var frame in frames) frame.PathLateralM = double.NaN;
            lap = CapturedLap.Create("LMU", frames);
            Check(lap.LateralPositionBasis == "unknown" && double.IsNaN(lap.Samples[0].PathLateralM),
                "missing native channel cannot invent a lateral path reference");
            frames = Frames(); foreach (var frame in frames) { frame.X = frame.Z = double.NaN; frame.CaptureUtc = DateTime.MinValue; frame.FuelLitres = double.NaN; }
            lap = CapturedLap.Create("LMU", frames);
            Check(lap.CoordinateFrame == "unknown" && lap.RecordedAt == "unknown" && lap.Conditions["fuelStartLitres"] == "unknown", "missing evidence stays unknown");
            return assertions;
        }
    }
}
