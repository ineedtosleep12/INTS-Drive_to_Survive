using System;
using System.IO;

namespace IntsLiveCoach
{
    public static class WrcReaderFixtureTests
    {
        static int checks;
        public static int Run()
        {
            checks = 0; TelemetryFrame f; ulong packet, count; string status;
            Check(WrcReader.TryDecode(Fixture(), out f, out packet, out count, out status), "current237-byte standard packet");
            Check(packet == 1234 && count == 5678, "64-bit packet/frame counters, not obsolete fourCC");
            Near(f.Time, 100, "game boot timestamp"); Near(f.SpeedKph, 180, "car body speed not wheel speed");
            Near(f.Brake, .7, "post-assist brake"); Near(f.Steering, -.25, "negative left input");
            Near(f.Clutch, .2, "clutch"); Near(f.Handbrake, .8, "handbrake"); Near(f.LapDistance, 1234.5, "64-bit stage distance");
            Check(f.Gear == 3 && f.OnTrack && !f.ValidLapKnown && !f.IsValidLap, "no invented valid lap");
            Check(f.Car.Contains("미제공") && f.Track.Contains("미제공"), "missing car and stage identity explicit");
            Near(f.LocalVelocityRight, -4, "right is opposite supplied left axis"); Near(f.LocalVelocityForward, 50, "forward velocity projection");
            Near(f.LateralG, -1, "world acceleration projected to local lateral G"); Near(f.LongitudinalG, .5, "forward acceleration");
            Check(double.IsNaN(f.FuelLitres) && double.IsNaN(f.YawRate), "missing signals stay unknown");
            Reject(delegate(byte[] b) { F32(b, 197, float.NaN); }, "missing throttle rejected");
            Reject(delegate(byte[] b) { F32(b, 209, 1.1f); }, "steer range");
            Reject(delegate(byte[] b) { F32(b, 213, -1); }, "handbrake range");
            Reject(delegate(byte[] b) { F64(b, 229, double.PositiveInfinity); }, "length not infinite");
            Reject(delegate(byte[] b) { F64(b, 221, -200); }, "out of range position");
            Reject(delegate(byte[] b) { b[36] = 2; }, "malformed bool");
            Reject(delegate(byte[] b) { b[37] = 8; }, "impossible gear");
            Check(!WrcReader.TryDecode(new byte[246], out f, out packet, out count, out status), "wrong legacy/guessed size rejected");
            Check(!WrcReader.TryDecode(new byte[236], out f, out packet, out count, out status), "truncated packet rejected");
            byte[] reverse = Fixture(); reverse[37] = 255;
            Check(WrcReader.TryDecode(reverse, out f, out packet, out count, out status) && f.Gear == -1, "reverse index from packet metadata");
            byte[] prerace = Fixture(); F32(prerace, 217, 0);
            Check(WrcReader.TryDecode(prerace, out f, out packet, out count, out status) && !f.OnTrack, "before-start excluded from coaching");
            byte[] badAxes = Fixture(); F32(badAxes, 85, 0);
            Check(WrcReader.TryDecode(badAxes, out f, out packet, out count, out status) && double.IsNaN(f.LocalVelocityRight), "invalid basis leaves derived dynamics unknown");
            byte[] rotated = Fixture(); F32(rotated, 85, 0); F32(rotated, 93, -1); F32(rotated, 97, 1); F32(rotated, 105, 0); F32(rotated, 61, 50); F32(rotated, 69, -4);
            Check(WrcReader.TryDecode(rotated, out f, out packet, out count, out status), "rotated vehicle basis accepted");
            Near(f.LocalVelocityRight, -4, "projection independent of world heading"); Near(f.LocalVelocityForward, 50, "rotated forward projection");
            string directory = Path.Combine(Path.GetTempPath(), "ints-wrc-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); string file = Path.Combine(directory, "config.json");
            try
            {
                File.WriteAllText(file, Config("false", "127.0.0.1"));
                Check(!WrcReader.VerifyConfiguration(directory, 20777, out status), "disabled stream cannot claim configured");
                File.WriteAllText(file, Config("true", "127.0.0.1"));
                Check(WrcReader.VerifyConfiguration(directory, 20777, out status), "enabled local standard stream");
                Check(!WrcReader.VerifyConfiguration(directory, 20778, out status), "port must match");
                File.WriteAllText(file, Config("true", "192.168.1.5"));
                Check(!WrcReader.VerifyConfiguration(directory, 20777, out status), "remote target not accepted as localhost");
                Check(!WrcReader.VerifySchema(directory, out status), "missing official schema fails closed");
            }
            finally { File.Delete(file); Directory.Delete(directory); }
            return checks;
        }
        static string Config(string enabled, string ip) { return "{\"schema\":2,\"udp\":{\"packets\":[{\"structure\":\"wrc\",\"packet\":\"session_update\",\"ip\":\"" + ip + "\",\"port\":20777,\"frequencyHz\":60,\"bEnabled\":" + enabled + "}]}}"; }
        static byte[] Fixture()
        {
            byte[] b = new byte[237]; U64(b, 0, 1234); F32(b, 8, 100); F32(b, 12, .016f); U64(b, 16, 5678);
            b[36] = 1; b[37] = 3; b[38] = 0; b[39] = 255; b[40] = 6; F32(b, 41, 50); F32(b, 45, 60);
            F32(b, 61, 4); F32(b, 69, 50); F32(b, 73, 9.80665f); F32(b, 81, 4.903325f); F32(b, 85, 1); F32(b, 105, 1); F32(b, 113, 1);
            F32(b, 197, .3f); F32(b, 201, .7f); F32(b, 205, .2f); F32(b, 209, -.25f); F32(b, 213, .8f);
            F32(b, 217, 40); F64(b, 221, 1234.5); F64(b, 229, 8000); return b;
        }
        static void Reject(Action<byte[]> edit, string message) { byte[] b = Fixture(); edit(b); TelemetryFrame f; ulong p, c; string status; Check(!WrcReader.TryDecode(b, out f, out p, out c, out status) && f == null, message); }
        static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("WRC fixture: " + message); }
        static void Near(double actual, double expected, string message) { Check(Math.Abs(actual - expected) < .0001, message); }
        static void F32(byte[] b, int o, float v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 4); }
        static void F64(byte[] b, int o, double v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 8); }
        static void U64(byte[] b, int o, ulong v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 8); }
    }
}
