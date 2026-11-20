using System;
using System.Text;

namespace IntsLiveCoach
{
    public static class Ams2ReaderFixtureTests
    {
        static int checks;
        public static int Run()
        {
            checks = 0; TelemetryFrame f; double previous; string status;
            Check(Ams2Reader.TryDecode(Fixture(), out f, out previous, out status), "valid v14 bytes");
            Check(f.Car == "Fixture GT3" && f.Track == "Fixture Circuit / GP", "car and exact layout identity");
            Near(f.SpeedKph, 252, "speed unit"); Near(f.LapDistance, 1234, "viewed participant stride and distance");
            Near(f.Brake, .7, "raw brake rather than filtered brake"); Near(f.Steering, -.3, "native signed steering preserved");
            Check(double.IsNaN(f.Clutch), "undocumented clutch engagement direction stays unknown"); Near(f.Handbrake, .1, "handbrake"); Near(f.FuelLitres, 40, "fraction times capacity");
            Check(f.Lap == 3 && f.Gear == 5 && f.ValidLapKnown && f.IsValidLap, "lap gear validity");
            Check(double.IsNaN(f.LocalVelocityRight) && double.IsNaN(f.YawRate), "undocumented axis signs not guessed");
            Check(f.DistanceAgeSeconds == 0, "single coherent frame coordinate age");
            Reject(delegate(byte[] b) { I32(b, 0, 13); }, "unknown SDK version");
            Reject(delegate(byte[] b) { I32(b, 7320, 3); }, "write in progress");
            Reject(delegate(byte[] b) { I32(b, 8, 6); }, "replay excluded");
            Reject(delegate(byte[] b) { I32(b, 8, 3); }, "pause excluded");
            Reject(delegate(byte[] b) { I32(b, 8, 4); }, "menu while time ticking excluded");
            Reject(delegate(byte[] b) { I32(b, 12, 4); }, "formation excluded");
            Reject(delegate(byte[] b) { I32(b, 16, 3); }, "finished excluded");
            Reject(delegate(byte[] b) { I32(b, 20, 64); }, "invalid participant index");
            Reject(delegate(byte[] b) { I32(b, 24, 65); }, "invalid participant count");
            Reject(delegate(byte[] b) { b[128] = 0; }, "inactive participant");
            Reject(delegate(byte[] b) { F32(b, 6432, float.NaN); }, "unknown pedal not zero");
            Reject(delegate(byte[] b) { F32(b, 6848, float.PositiveInfinity); }, "invalid speed");
            Reject(delegate(byte[] b) { b[6712] = 2; }, "malformed bool");
            Reject(delegate(byte[] b) { b[6444] = 0; }, "empty car");
            Check(!Ams2Reader.TryDecode(new byte[20699], out f, out previous, out status), "truncated frame rejected");
            byte[] invalid = Fixture(); invalid[6712] = 1;
            Check(Ams2Reader.TryDecode(invalid, out f, out previous, out status) && !f.IsValidLap, "invalid lap remains displayable");
            byte[] pit = Fixture(); I32(pit, 6808, 3);
            Check(Ams2Reader.TryDecode(pit, out f, out previous, out status) && f.InPit, "pit exit still excluded from baselines");
            var clock = new Ams2Reader.LapClock();
            f = Frame(3, 89.95); clock.Apply(f, 91); Near(f.Time, 89.95, "partial lap source clock");
            f = Frame(4, .05); clock.Apply(f, 90); Near(f.Time, 90.05, "lap wrap uses recorded prior lap time");
            f = Frame(4, 30); clock.Apply(f, 90); Near(f.Time, 120, "same lap cumulative clock");
            f = Frame(4, 1); clock.Apply(f, 90); Near(f.Time, 1, "same-lap restart resets cumulative clock");
            f = Frame(7, 10); clock.Apply(f, 90); Near(f.Time, 10, "jumped laps cannot invent elapsed laps");
            f = Frame(8, 0); clock.Apply(f, 10.05); Check(!f.TimeIsCaptureClock && Math.Abs(f.Time - 10.05) < .001, "exact zero at known finish line remains official continuous clock");
            f = Frame(0, 0); clock.Apply(f, 0); Check(f.TimeIsCaptureClock && f.Time > 0, "untimed outlap supplies explicitly marked receive clock");
            f = Frame(1, .1); clock.Apply(f, 0); Check(!f.TimeIsCaptureClock && Math.Abs(f.Time - .1) < .001, "official lap timer resumes without wall-clock offset");
            return checks;
        }
        static TelemetryFrame Frame(int lap, double time) { return new TelemetryFrame { Lap = lap, Time = time, Track = "T", Car = "C", GameVersion = "14" }; }
        static byte[] Fixture()
        {
            byte[] b = new byte[20700]; I32(b, 0, 14); I32(b, 4, 12345); I32(b, 8, 2); I32(b, 12, 1); I32(b, 16, 2);
            I32(b, 20, 1); I32(b, 24, 2); b[128] = 1; F32(b, 128 + 80, 1234); I32(b, 128 + 92, 3);
            F32(b, 6428, .4f); F32(b, 6432, .7f); F32(b, 6436, -.3f); F32(b, 6440, .2f);
            Text(b, 6444, "Fixture GT3"); Text(b, 6576, "Fixture Circuit"); Text(b, 6640, "GP"); F32(b, 6704, 5793);
            F32(b, 6720, 91); F32(b, 6724, 30); F32(b, 6840, .5f); F32(b, 6844, 80); F32(b, 6848, 70);
            F32(b, 6860, .2f); I32(b, 6876, 5); I32(b, 7320, 2); F32(b, 7404, .1f); return b;
        }
        static void Reject(Action<byte[]> edit, string message) { byte[] b = Fixture(); edit(b); TelemetryFrame f; double t; string status; Check(!Ams2Reader.TryDecode(b, out f, out t, out status) && f == null, message); }
        static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("AMS2 fixture: " + message); }
        static void Near(double actual, double expected, string message) { Check(Math.Abs(actual - expected) < .0001, message); }
        static void I32(byte[] b, int o, int v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 4); }
        static void F32(byte[] b, int o, float v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 4); }
        static void Text(byte[] b, int o, string s) { byte[] text = Encoding.UTF8.GetBytes(s); Buffer.BlockCopy(text, 0, b, o, text.Length); }
    }
}
