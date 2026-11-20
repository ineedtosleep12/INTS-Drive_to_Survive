using System;
using System.Text;

namespace IntsLiveCoach
{
    public static class AcrReaderTests
    {
        static int assertions;
        static void Check(bool ok, string message) { assertions++; if (!ok) throw new Exception("ACR fixture: " + message); }
        sealed class Fixture
        {
            internal byte[] P = new byte[AcrReader.PhysicsBytes], G = new byte[AcrReader.GraphicsBytes], S = new byte[AcrReader.StaticBytes];
            internal Fixture()
            {
                W(S, 0, "1.8"); W(S, 30, "0.3.0"); W(S, 68, "TestRallyCar"); W(S, 134, "TestStage");
                I(P, 0, 100); F(P, 4, .7f); F(P, 8, .25f); I(P, 16, 3); I(P, 20, 4500); F(P, 24, -.4f); F(P, 28, 80); I(P, 416, 0);
                I(G, 0, 10); I(G, 4, 2); I(G, 8, 3); I(G, 140, 10000); I(G, 160, 0);
            }
            internal bool Read(out TelemetryFrame f) { string status; return AcrReader.TryDecode(P, G, S, out f, out status); }
        }
        static void I(byte[] b, int o, int v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 4); }
        static void F(byte[] b, int o, float v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 4); }
        static void W(byte[] b, int o, string s) { byte[] v = Encoding.Unicode.GetBytes(s + "\0"); Buffer.BlockCopy(v, 0, b, o, v.Length); }
        static TelemetryFrame ClockFrame(string car, string track) { return new TelemetryFrame { GameVersion = "ACR test", Car = car, Track = track }; }
        public static int Run()
        {
            assertions = 0; TelemetryFrame f; string status; Fixture x = new Fixture();
            Check(x.Read(out f), "known synthetic prefix decodes");
            Check(Math.Abs(f.SpeedKph - 80) < .001 && Math.Abs(f.Throttle - .7) < .001 && Math.Abs(f.Brake - .25) < .001, "speed and pedal offsets");
            Check(Math.Abs(f.Steering + .4) < .001, "signed normalized steering is preserved");
            Check(f.Gear == 3, "ACR native gear is not shifted by original AC convention");
            Check(f.Car == "TestRallyCar" && f.Track == "TestStage", "car and stage labels");
            Check(f.GameVersion.Contains("0.3.0") && f.GameVersion.Contains("1.8") && f.GameVersion.Contains("experimental"), "actual schema version displayed without certification claim");
            Check(f.OnTrack && !f.IsReplay && !f.InPit, "live driving state");
            Check(!f.ValidLapKnown && !f.IsValidLap && f.Lap == -1, "no invented valid circuit lap");
            Check(Double.IsNaN(f.TrackLength) && Double.IsNaN(f.LapDistance), "no made-up course metre scale");
            Check(Double.IsNaN(f.Handbrake) && Double.IsNaN(f.Clutch), "unverified handbrake/clutch stay unknown");
            Check(Double.IsNaN(f.YawRate) && Double.IsNaN(f.LocalVelocityForward) && Double.IsNaN(f.LocalVelocitySideAbs) && Double.IsNaN(f.LateralG), "unverified motion axes stay unknown");
            Check(f.TimeIsCaptureClock && Double.IsNaN(f.FuelLitres), "no game lap clock or assumed fuel units");
            foreach (int state in new int[] { 0, 1, 3, 4, -1 }) { x = new Fixture(); I(x.G, 4, state); Check(!x.Read(out f), "non-live/replay/paused status rejected " + state); }
            x = new Fixture(); I(x.P, 416, 1); Check(!x.Read(out f), "AI controlled frames rejected");
            x = new Fixture(); I(x.P, 416, 7); Check(!x.Read(out f), "invalid AI bool rejected");
            x = new Fixture(); I(x.G, 160, 1); Check(!x.Read(out f), "pit frame excluded before duplicate physics handling");
            x = new Fixture(); I(x.G, 160, 2); Check(!x.Read(out f), "invalid pit bool rejected");
            foreach (float value in new float[] { float.NaN, float.PositiveInfinity, -.1f, 1.1f }) { x = new Fixture(); F(x.P, 4, value); Check(!x.Read(out f), "invalid throttle rejected"); }
            x = new Fixture(); F(x.P, 8, -1); Check(!x.Read(out f), "invalid brake rejected");
            x = new Fixture(); F(x.P, 24, 35); Check(!x.Read(out f), "possible steering degrees rejected, not silently divided or clamped");
            x = new Fixture(); F(x.P, 24, float.NaN); Check(!x.Read(out f), "NaN steering rejected");
            x = new Fixture(); F(x.P, 28, 1000); Check(!x.Read(out f), "unreasonable speed rejected");
            x = new Fixture(); I(x.P, 16, -1); Check(x.Read(out f) && f.Gear == -1, "reverse remains native minus one");
            x = new Fixture(); I(x.P, 16, 0); Check(x.Read(out f) && f.Gear == 0, "neutral remains native zero");
            x = new Fixture(); I(x.P, 16, 99); Check(!x.Read(out f), "invalid gear rejected");
            x = new Fixture(); I(x.G, 140, -100); Check(!x.Read(out f), "negative stage timer held");
            x = new Fixture(); I(x.G, 8, 99); Check(!x.Read(out f), "invalid session enum rejected");
            x = new Fixture(); W(x.S, 134, ""); Check(x.Read(out f) && f.Track.Contains("미제공"), "missing stage label explicitly indicated");
            x = new Fixture(); W(x.S, 68, ""); Check(!x.Read(out f), "empty car rejects uninitialized static page");
            x = new Fixture(); W(x.S, 0, ""); Check(!x.Read(out f), "empty schema rejects uninitialized static page");
            x = new Fixture(); W(x.S, 30, "9.2.1"); Check(x.Read(out f) && f.GameVersion.Contains("9.2.1"), "game version is reported, not guessed from release number");
            x = new Fixture(); for (int i = 0; i < 30; i++) x.S[i] = 0x41; Check(!x.Read(out f), "unterminated schema rejected");
            x = new Fixture(); x.S[68] = 0; x.S[69] = 0xD8; Check(!x.Read(out f), "malformed UTF16 rejected");
            x = new Fixture(); Check(!AcrReader.TryDecode(null, x.G, x.S, out f, out status), "null payload rejected");
            Check(!AcrReader.TryDecode(new byte[AcrReader.PhysicsBytes - 1], x.G, x.S, out f, out status), "short physics rejected");
            Check(!AcrReader.TryDecode(x.P, new byte[AcrReader.GraphicsBytes - 1], x.S, out f, out status), "short graphics rejected");
            Check(!AcrReader.TryDecode(x.P, x.G, new byte[AcrReader.StaticBytes - 1], out f, out status), "short static rejected");
            Check(AcrReader.IsGamePath("C:\\Steam\\Assetto Corsa Rally\\acr\\Binaries\\Win64\\acr.exe"), "installed process path accepted");
            Check(!AcrReader.IsGamePath("C:\\OtherApp\\acr.exe") && !AcrReader.IsGamePath(null), "unrelated same-name process rejected");
            x = new Fixture(); int originalPacket = BitConverter.ToInt32(x.P, 0);
            foreach (int state in new int[] { 0, 1, 3 })
            {
                I(x.G, 4, state);
                Check(BitConverter.ToInt32(x.P, 0) == originalPacket && !AcrReader.AllowsLiveState(x.P, x.G, out status), "explicit inactive state rejects even with held physics counter " + state);
            }
            I(x.G, 4, 2); Check(AcrReader.AllowsLiveState(x.P, x.G, out status), "live state resumes after mapping reconnect");

            AcrCaptureClock timer = new AcrCaptureClock(); f = ClockFrame("car", "stage");
            Check(timer.Apply(f, 10000, 3, 100) && f.Time == 0 && f.TimeIsCaptureClock, "capture epoch starts at first observed frame");
            Check(timer.Apply(f, 10050, 3, 100.05) && Math.Abs(f.Time - .05) < .00001, "input observation time follows receive clock");
            Check(timer.Apply(f, 10050, 3, 100.1) && Math.Abs(f.Time - .1) < .00001, "live packets with held stage clock remain explicitly capture timed");
            Check(!timer.Apply(f, 0, 3, 101) && f.Time == 0, "stage restart forces nontransient boundary");
            Check(timer.Apply(f, 50, 3, 101.05), "first new-stage packet resumes after boundary");
            f = ClockFrame("car", "new stage"); Check(!timer.Apply(f, 50, 3, 102), "stage identity change forces boundary");
            f = ClockFrame("new car", "new stage"); Check(!timer.Apply(f, 50, 3, 103), "car change forces boundary");
            Check(!timer.Apply(f, 50, 4, 104), "session change forces boundary");
            timer.Reset(); Check(timer.Apply(f, 5000, 4, 200) && f.Time == 0, "reconnection starts clean capture epoch");
            using (var reader = new AcrReader()) { reader.Dispose(); Check(!reader.TryRead(out f, out status) && !reader.LastReadTransient, "disposed reader never reads maps"); }
            return assertions;
        }
    }
}
