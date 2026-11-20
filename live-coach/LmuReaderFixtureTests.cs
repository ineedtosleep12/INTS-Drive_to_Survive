using System;
using System.IO;
using System.Text;

namespace IntsLiveCoach
{
    // Entirely synthetic bytes, independent of LMU being installed or running.
    // These verify decoding/guard rails; they DO NOT certify live-game integration.
    public static class LmuReaderFixtureTests
    {
        private static int checks;
        private const int Score = 2192;
        // Player uses telemetry slot 1 but scoring slot 0: IDs, not indices, join.
        private const int Player = 128468 + 1888;

        public static int Run()
        {
            checks = 0;
            TelemetryFrame frame;
            string status;
            byte[] valid = Fixture();
            Check(LmuReader.TryDecode(valid, out frame, out status), "valid synthetic native snapshot");
            Check(frame.Car == "Fixture GT3" && frame.Track == "Fixture Monza", "driver ID join across different slot order");
            Near(frame.SpeedKph, 250, "m/s to km/h");
            Near(frame.Brake, .83, "unfiltered brake, not filtered intervention");
            Near(frame.Throttle, .21, "unfiltered throttle");
            Near(frame.Steering, -.32, "SDK steering negative means left");
            Check(Double.IsNaN(frame.Clutch), "unverified clutch polarity must not trigger a pedal-action verdict");
            Near(frame.LapDistance, 600, "scoring lap distance");
            Near(frame.TrackLength, 5793, "scoring track length");
            Near(frame.DistanceAgeSeconds, .1, "session timestamp delta records scoring distance age");
            Near(frame.YawRate, -.7, "local Y angular velocity");
            Near(frame.LateralG, 1.2, "local X acceleration in G");
            Check(frame.ValidLapKnown && frame.IsValidLap && frame.OnTrack && !frame.IsReplay, "valid local live driving flags");
            Check(frame.Lap == 3 && frame.Gear == 6, "coherent scoring lap ordinal and gear offsets");
            byte[] telemetryFirst = Fixture();
            I32(telemetryFirst, Player + 20, 4);
            F64(telemetryFirst, Score + 104, 5790);
            Check(LmuReader.TryDecode(telemetryFirst, out frame, out status) && frame.Lap == 3 && frame.LapDistance == 5790,
                "telemetry counter advances first: scoring distance keeps old lap ordinal");
            byte[] scoringFirst = Fixture();
            I16(scoringFirst, Score + 100, 3);
            F64(scoringFirst, Score + 104, 5);
            Check(LmuReader.TryDecode(scoringFirst, out frame, out status) && frame.Lap == 4 && frame.LapDistance == 5,
                "scoring counter advances first: new distance uses new scoring lap ordinal");
            bool coherenceWait;
            byte[] newTelemetryEpoch = Fixture();
            F64(newTelemetryEpoch, Player + 24, 100);
            Check(!LmuReader.TryDecode(newTelemetryEpoch, out frame, out status, out coherenceWait) && coherenceWait && frame == null,
                "new telemetry lap epoch waits transiently for scoring");
            byte[] newScoringEpoch = Fixture();
            F64(newScoringEpoch, Score + 256, 100);
            newScoringEpoch[Player + 745] = 1;
            Check(!LmuReader.TryDecode(newScoringEpoch, out frame, out status, out coherenceWait) && coherenceWait && frame == null,
                "old telemetry invalid flag never poisons new scoring lap");
            F64(newScoringEpoch, Player + 24, 100);
            newScoringEpoch[Player + 745] = 0;
            Check(LmuReader.TryDecode(newScoringEpoch, out frame, out status, out coherenceWait) && !coherenceWait && frame.IsValidLap,
                "matching lap epochs resume with current validity");
            byte[] sameTimestamp = Fixture(); F64(sameTimestamp, 1632 + 68, 100.25);
            Check(LmuReader.TryDecode(sameTimestamp, out frame, out status) && frame.DistanceAgeSeconds == 0,
                "equal source timestamps have zero measured skew");
            byte[] scoringAhead = Fixture(); F64(scoringAhead, 1632 + 68, 100.65);
            Check(LmuReader.TryDecode(scoringAhead, out frame, out status), "small forward scoring timestamp is decodable");
            Near(frame.DistanceAgeSeconds, .4, "forward scoring timestamp must not falsely clamp uncertainty to zero");
            byte[] delayedScoring = Fixture(); F64(delayedScoring, 1632 + 68, 99.5);
            Check(LmuReader.TryDecode(delayedScoring, out frame, out status) && frame.DistanceAgeSeconds == .75,
                "age above coaching cutoff remains explicit so engine can reject it");

            Check(!LmuReader.TryDecode(new byte[100], out frame, out status), "reject truncated payload");
            Check(!LmuReader.TryDecode(new byte[324824], out frame, out status), "decoder consumes exact payload, not guessed layout");
            Reject(delegate(byte[] b) { b[128465] = 104; }, "out of range player index");
            Reject(delegate(byte[] b) { I32(b, 1632 + 104, 105); }, "too many scoring vehicles");
            Reject(delegate(byte[] b) { I32(b, Score, 999); }, "player ID missing from scoring");
            Reject(delegate(byte[] b) { I32(b, Score + 584, 42); }, "duplicate player ID in scoring");
            Reject(delegate(byte[] b) { b[Score + 196] = 0; }, "nonplayer scoring row");
            Reject(delegate(byte[] b) { b[Score + 197] = 1; }, "AI must not become driver reference");
            Reject(delegate(byte[] b) { b[Score + 197] = 3; }, "replay must not become driver reference");
            Reject(delegate(byte[] b) { b[1632 + 115] = 0; }, "monitor / non-realtime");
            Reject(delegate(byte[] b) { b[1632 + 108] = 9; }, "paused heartbeat");
            Reject(delegate(byte[] b) { b[96] = 2; }, "monitor application screen");
            Reject(delegate(byte[] b) { b[Score + 507] = 1; }, "garage stall");
            Reject(delegate(byte[] b) { F64(b, Player + 396, Double.NaN); }, "NaN brake must not become zero");
            Reject(delegate(byte[] b) { F64(b, Player + 388, 1.1); }, "out of bounds pedal");
            Reject(delegate(byte[] b) { F64(b, Player + 200, Double.PositiveInfinity); }, "infinite speed");
            Reject(delegate(byte[] b) { F64(b, 1632 + 68, 90); }, "stale scoring data");
            Reject(delegate(byte[] b) { F64(b, 1632 + 68, 102); }, "future scoring data beyond original skew gate");
            Reject(delegate(byte[] b) { F64(b, 1632 + 68, Double.NaN); }, "missing scoring timestamp cannot imply fresh distance");
            Reject(delegate(byte[] b) { F64(b, Player + 12, Double.PositiveInfinity); }, "infinite telemetry timestamp rejected");
            Reject(delegate(byte[] b) { CString(b, 1632, "Other layout"); }, "mismatched scoring and telemetry tracks");
            Reject(delegate(byte[] b) { b[Player + 32] = 0; }, "empty car identifier");
            Reject(delegate(byte[] b) { b[Player + 96] = 0; }, "empty track identifier");
            Reject(delegate(byte[] b) { b[Player + 745] = 2; }, "malformed one-byte bool");
            Reject(delegate(byte[] b) { I16(b, Score + 100, -1); }, "negative completed lap count");
            Reject(delegate(byte[] b) { F64(b, Player + 24, Double.NaN); }, "missing lap epoch cannot claim coherent lap validity");

            byte[] invalidLap = Fixture(); invalidLap[Player + 745] = 1;
            Check(LmuReader.TryDecode(invalidLap, out frame, out status) && frame.ValidLapKnown && !frame.IsValidLap,
                "invalidated lap continues live display but cannot become reference");
            byte[] noTime = Fixture(); noTime[Score + 506] = 1;
            Check(LmuReader.TryDecode(noTime, out frame, out status) && !frame.IsValidLap, "count lap without time is not clean reference");
            byte[] pits = Fixture(); pits[Score + 198] = 1;
            Check(LmuReader.TryDecode(pits, out frame, out status) && frame.InPit, "pit flag survives decode for coaching exclusion");
            byte[] optional = Fixture(); F64(optional, Player + 524, Double.NaN);
            Check(LmuReader.TryDecode(optional, out frame, out status) && Double.IsNaN(frame.FuelLitres), "unavailable optional sensor stays unknown");

            string temp = Path.Combine(Path.GetTempPath(), "ints-lmu-sdk-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                File.WriteAllText(Path.Combine(temp, "InternalsPlugin.hpp"), "SYNTHETIC UNKNOWN SDK - NOT GAME SOURCE");
                Check(!LmuReader.VerifySdk(temp, out status), "unknown SDK fails closed");
            }
            finally
            {
                File.Delete(Path.Combine(temp, "InternalsPlugin.hpp"));
                Directory.Delete(temp); // Only the exact empty fixture directory; no recursive deletion.
            }
            return checks;
        }

        private static byte[] Fixture()
        {
            byte[] bytes = new byte[324820];
            I32(bytes, 64, 12345); // synthetic game version; not claimed as an ABI schema
            bytes[96] = 3;
            CString(bytes, 1632, "Fixture Monza");
            F64(bytes, 1632 + 68, 100.15);
            F64(bytes, 1632 + 88, 5793);
            I32(bytes, 1632 + 104, 2);
            bytes[1632 + 108] = 5;
            bytes[1632 + 115] = 1;
            bytes[128464] = 2;
            bytes[128465] = 1;
            bytes[128466] = 1;
            I32(bytes, Score, 42);
            I32(bytes, Score + 584, 77);
            bytes[Score + 196] = 1;
            bytes[Score + 197] = 0;
            bytes[Score + 506] = 2;
            I16(bytes, Score + 100, 2);
            F64(bytes, Score + 104, 600);
            F64(bytes, Score + 256, 70);
            I32(bytes, Player, 42);
            F64(bytes, Player + 12, 100.25);
            F64(bytes, Player + 24, 70);
            I32(bytes, Player + 20, 3);
            CString(bytes, Player + 32, "Fixture GT3");
            CString(bytes, Player + 96, "Fixture Monza");
            F64(bytes, Player + 200, -250.0 / 3.6);
            F64(bytes, Player + 208, 1.2 * 9.80665);
            F64(bytes, Player + 312, -.7);
            I32(bytes, Player + 352, 6);
            F64(bytes, Player + 388, .21);
            F64(bytes, Player + 396, .83);
            F64(bytes, Player + 404, -.32);
            F64(bytes, Player + 412, .15);
            F64(bytes, Player + 420, .03); // filtered channels deliberately differ
            F64(bytes, Player + 428, .37);
            F64(bytes, Player + 524, 43.2);
            return bytes;
        }

        private static void Reject(Action<byte[]> edit, string name)
        {
            byte[] bytes = Fixture(); edit(bytes);
            TelemetryFrame frame; string status;
            Check(!LmuReader.TryDecode(bytes, out frame, out status) && frame == null, name);
        }
        private static void Check(bool condition, string name)
        { ++checks; if (!condition) throw new Exception("LMU synthetic fixture failed: " + name); }
        private static void Near(double actual, double expected, string name)
        { Check(!Double.IsNaN(actual) && Math.Abs(actual - expected) < 0.000001, name); }
        private static void I32(byte[] b, int offset, int value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, offset, 4); }
        private static void I16(byte[] b, int offset, short value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, offset, 2); }
        private static void F64(byte[] b, int offset, double value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, offset, 8); }
        private static void CString(byte[] b, int offset, string value)
        {
            byte[] str = Encoding.UTF8.GetBytes(value);
            Array.Clear(b, offset, 64);
            Buffer.BlockCopy(str, 0, b, offset, str.Length);
        }
    }
}
