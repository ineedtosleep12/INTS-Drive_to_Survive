using System;
using System.Text;

namespace IntsLiveCoach
{
    // Synthetic ABI fixtures only; no assertion claims a running-game test.
    public static class AssettoReaderTests
    {
        private static int checks;
        public static int Run()
        {
            checks = 0; TelemetryFrame frame; string status;
            byte[][] ac = Fixture(AssettoGame.Ac), acc = Fixture(AssettoGame.Acc);
            Check(Decode(AssettoGame.Ac, ac, Double.NaN, out frame, out status), "AC 1.7 snapshot");
            Near(frame.Throttle, .7, "AC throttle float offset");
            Near(frame.Brake, .3, "AC brake float offset");
            Near(frame.Steering, -.25, "AC signed normalized steering");
            Near(frame.TrackLength, 5000, "AC native spline length");
            Near(frame.LapDistance, 1250, "AC progress converted using real native length");
            Near(frame.LocalVelocityForward, 20, "AC forward Z velocity");
            Near(frame.LocalVelocitySideAbs, 5, "AC sideways magnitude without unproven right sign");
            Check(Double.IsNaN(frame.LocalVelocityRight), "unknown sideways polarity is not invented");
            Check(Double.IsNaN(frame.Handbrake) && Double.IsNaN(frame.Clutch), "unsupported / uncalibrated pedal channels remain unknown");
            Check(!frame.ValidLapKnown && !frame.IsValidLap, "AC native API cannot certify lap validity");
            Check(frame.Track == "fixture_track/layout" && frame.Car == "fixture_car", "track layout identity retained");
            Check(frame.Gear == 2 && frame.Lap == 3, "native gear and lap ordinal conversion");
            Near(frame.YawRate, .4, "local Y yaw rate");
            Near(frame.LateralG, .6, "AC lateral acceleration");
            Near(frame.LongitudinalG, -.8, "AC longitudinal acceleration");
            Check(Decode(AssettoGame.Acc, acc, Double.NaN, out frame, out status), "ACC 1.8 snapshot");
            Check(frame.ValidLapKnown && frame.IsValidLap, "ACC explicit isValidLap");
            Check(Double.IsNaN(frame.TrackLength) && Double.IsNaN(frame.LapDistance), "ACC unused native length is not a fake metre scale");
            Check(Double.IsNaN(frame.FuelLitres) && Double.IsNaN(frame.LateralG), "ACC kg and global G are not mislabeled");
            Near(frame.X, 123, "ACC player ID joins coordinate row, not row zero");
            Near(frame.Z, 789, "ACC player coordinate third component");
            Check(Decode(AssettoGame.Acc, acc, 5000, out frame, out status), "ACC verified length provider value");
            Near(frame.LapDistance, 1250, "ACC verified metres conversion");
            byte[][] noPosition = Fixture(AssettoGame.Acc); F32(noPosition[1], 248, Single.NaN);
            Check(Decode(AssettoGame.Acc,noPosition,5137,out frame,out status)&&Double.IsNaN(frame.LapDistance),"length alone never fabricates course position");
            Check(Decode(AssettoGame.Acc,acc,Double.PositiveInfinity,out frame,out status)&&Double.IsNaN(frame.TrackLength),"invalid provider length fails closed");
            Check(Decode(AssettoGame.Acc,acc,5137,out frame,out status)&&frame.X==123&&frame.Y==456&&frame.Z==789,"metadata length does not alter original player world coordinates");
            var observedGraphics = new AssettoGraphicsObservation();
            observedGraphics.Observe(20, 10); observedGraphics.Apply(frame,10);
            Check(Double.IsNaN(frame.DistanceAgeSeconds)&&Double.IsNaN(frame.DistanceObservationAgeSeconds)&&Double.IsNaN(frame.DistanceObservationIntervalSeconds),"first ACC snapshot cannot assert observed cadence or source age");
            observedGraphics.Observe(21,10.03); observedGraphics.Apply(frame,10.04);
            Near(frame.DistanceObservationAgeSeconds,.01,"host-observed packet age is separate from source age");
            Near(frame.DistanceObservationIntervalSeconds,.03,"observed graphics packet interval");
            Check(Double.IsNaN(frame.DistanceAgeSeconds),"observation never fabricates native source timestamp");
            observedGraphics.Observe(21,11); observedGraphics.Apply(frame,11);
            Near(frame.DistanceObservationAgeSeconds,.97,"duplicate graphics packet ages instead of becoming fresh");
            observedGraphics.Observe(1,11.1); observedGraphics.Apply(frame,11.1);
            Check(Double.IsNaN(frame.DistanceObservationIntervalSeconds),"packet rollback requires two fresh observations");
            observedGraphics.Observe(2,11.2); observedGraphics.Observe(3,10); observedGraphics.Apply(frame,10);
            Check(Double.IsNaN(frame.DistanceObservationIntervalSeconds),"host clock rollback clears cadence");
            observedGraphics.Reset(); observedGraphics.Apply(frame,12);
            Check(Double.IsNaN(frame.DistanceObservationAgeSeconds),"disconnect clears observed freshness");

            Reject(AssettoGame.Ac, delegate(byte[][] x) { Wide(x[2], 0, 15, "1.8"); }, "reject ACC page under AC selection");
            Reject(AssettoGame.Acc, delegate(byte[][] x) { Wide(x[2], 0, 15, "1.7"); }, "reject AC page under ACC selection");
            Reject(AssettoGame.Acc, delegate(byte[][] x) { Wide(x[2], 0, 15, "2.0"); }, "unknown ABI major fails closed");
            byte[][] gameUpdate = Fixture(AssettoGame.Acc); Wide(gameUpdate[2], 30, 15, "1.99.23");
            Check(Decode(AssettoGame.Acc, gameUpdate, Double.NaN, out frame, out status), "game-version updates do not arbitrarily block stable ABI");
            Reject(AssettoGame.Ac, delegate(byte[][] x) { x[0] = new byte[32]; }, "short physics page");
            Reject(AssettoGame.Acc, delegate(byte[][] x) { x[1] = new byte[296]; }, "short ACC graphics cannot supply fake zero validity");
            Reject(AssettoGame.Ac, delegate(byte[][] x) { I32(x[1], 4, 1); }, "replay excluded");
            Reject(AssettoGame.Ac, delegate(byte[][] x) { I32(x[1], 4, 3); }, "paused excluded");
            Reject(AssettoGame.Ac, delegate(byte[][] x) { I32(x[0], 416, 1); }, "AI excluded");
            Reject(AssettoGame.Ac, delegate(byte[][] x) { F32(x[0], 4, Single.NaN); }, "NaN throttle excluded");
            Reject(AssettoGame.Acc, delegate(byte[][] x) { F32(x[0], 24, 2); }, "unnormalized steering rejected");
            Reject(AssettoGame.Acc, delegate(byte[][] x) { I32(x[1], 252, 61); }, "ACC active-car bounds");
            Reject(AssettoGame.Acc, delegate(byte[][] x) { I32(x[1], 1216, 999); }, "ACC missing player ID");
            Reject(AssettoGame.Acc, delegate(byte[][] x) { I32(x[1], 976, 42); }, "ACC duplicate player ID");
            Reject(AssettoGame.Acc, delegate(byte[][] x) { I32(x[1], 1408, 2); }, "invalid ACC validity flag encoding");
            Reject(AssettoGame.Acc, delegate(byte[][] x) { I32(x[1], 1256, 1); }, "setup menu excluded");
            Reject(AssettoGame.Ac, delegate(byte[][] x) { Wide(x[2], 68, 33, ""); }, "empty model rejected");
            Reject(AssettoGame.Ac, delegate(byte[][] x) { Wide(x[2], 134, 33, ""); }, "empty track rejected");
            Reject(AssettoGame.Ac, delegate(byte[][] x) { I32(x[1], 132, -1); }, "negative laps rejected");
            Reject(AssettoGame.Ac, delegate(byte[][] x) { I32(x[1], 140, -1); }, "negative lap timer rejected");
            byte[][] badVelocity = Fixture(AssettoGame.Ac); F32(badVelocity[0], 576, 200);
            Check(Decode(AssettoGame.Ac, badVelocity, Double.NaN, out frame, out status) && Double.IsNaN(frame.LocalVelocityForward),
                "inconsistent optional velocity is unknown, not a bogus drift angle");
            byte[][] invalid = Fixture(AssettoGame.Acc); I32(invalid[1], 1408, 0);
            Check(Decode(AssettoGame.Acc, invalid, Double.NaN, out frame, out status) && frame.ValidLapKnown && !frame.IsValidLap,
                "invalid ACC lap still displays live data");
            byte[][] pits = Fixture(AssettoGame.Ac); I32(pits[1], 276, 1);
            Check(Decode(AssettoGame.Ac, pits, Double.NaN, out frame, out status) && frame.InPit, "AC pitlane exclusion signal");
            byte[][] noSpline = Fixture(AssettoGame.Ac); F32(noSpline[2], 520, 0);
            Check(Decode(AssettoGame.Ac, noSpline, Double.NaN, out frame, out status) && Double.IsNaN(frame.TrackLength),
                "drift playground without valid spline still exposes inputs");

            AssettoLapClock clock = new AssettoLapClock();
            frame = new TelemetryFrame { Track = "fixture", Car = "fixture" };
            clock.Apply(frame, 2, 89950, 89000, 0, 10); Near(frame.Time, 89.95, "game clock starts from observed lap timer");
            clock.Apply(frame, 3, 50, 90000, 0, 10.1); Near(frame.Time, 90.05, "game clock joins completed lap at finish");
            Check(!frame.TimeIsCaptureClock, "complete lap timing remains simulation-derived");
            clock.Apply(frame, 3, 1000, 90000, 0, 11); Near(frame.Time, 91, "subsequent timer progress");
            clock.Apply(frame, 3, 1000, 90000, 0, 12);
            Check(frame.TimeIsCaptureClock && frame.Time == 12, "frozen lap timer explicitly falls back to capture clock");
            clock.Apply(frame, 0, 20, 0, 0, 13);
            Check(!frame.TimeIsCaptureClock && frame.Time == .02, "restart resets lap accumulation");
            clock.Apply(frame, 1, 20, 0, 0, 14);
            Check(frame.TimeIsCaptureClock, "missing finished lap duration cannot fabricate sim elapsed time");
            clock.Reset(); clock.Apply(frame, 0, 0, 0, 5, 20);
            Check(frame.TimeIsCaptureClock && frame.Time == 20, "untimed drift session uses explicitly marked observed time");
            return checks;
        }

        internal static byte[][] Fixture(AssettoGame game)
        {
            byte[] p = new byte[580], g = new byte[game == AssettoGame.Ac ? 296 : 1412], s = new byte[592];
            Wide(s, 0, 15, game == AssettoGame.Ac ? "1.7" : "1.8");
            Wide(s, 30, 15, "1.16.4"); Wide(s, 68, 33, "fixture_car"); Wide(s, 134, 33, "fixture_track");
            Wide(s, 524, 33, "layout"); I32(s, 400, 3); F32(s, 520, 5000);
            I32(p, 0, 100); F32(p, 4, .7f); F32(p, 8, .3f); F32(p, 12, 40); I32(p, 16, 3); I32(p, 20, 6000);
            F32(p, 24, -.25f); F32(p, 28, (float)(Math.Sqrt(425) * 3.6));
            F32(p, 44, .6f); F32(p, 52, -.8f); F32(p, 300, .4f);
            F32(p, 568, -5); F32(p, 576, 20);
            I32(g, 0, 50); I32(g, 4, 2); I32(g, 8, 0); I32(g, 132, 2); I32(g, 140, 30000); I32(g, 144, 90000);
            F32(g, 248, .25f);
            if (game == AssettoGame.Acc)
            {
                I32(g, 252, 2); I32(g, 976, 77); I32(g, 980, 42); I32(g, 1216, 42); I32(g, 1408, 1);
                F32(g, 268, 123); F32(g, 272, 456); F32(g, 276, 789);
            }
            else { F32(g, 252, 123); F32(g, 256, 456); F32(g, 260, 789); }
            return new byte[][] { p, g, s };
        }
        private static bool Decode(AssettoGame game, byte[][] x, double length, out TelemetryFrame f, out string status)
        { return AssettoReader.TryDecode(game, x[0], x[1], x[2], length, out f, out status); }
        private static void Reject(AssettoGame game, Action<byte[][]> edit, string name)
        { byte[][] x = Fixture(game); edit(x); TelemetryFrame frame; string status; Check(!Decode(game, x, Double.NaN, out frame, out status) && frame == null, name); }
        private static void Check(bool condition, string name)
        { ++checks; if (!condition) throw new Exception("Assetto synthetic fixture failed: " + name); }
        private static void Near(double value, double expected, string name)
        { Check(!Double.IsNaN(value) && Math.Abs(value - expected) < .0001, name); }
        private static void I32(byte[] b, int offset, int value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, offset, 4); }
        private static void F32(byte[] b, int offset, float value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, offset, 4); }
        private static void Wide(byte[] b, int offset, int chars, string value)
        { Array.Clear(b, offset, chars * 2); byte[] text = Encoding.Unicode.GetBytes(value); Buffer.BlockCopy(text, 0, b, offset, text.Length); }
    }
}
