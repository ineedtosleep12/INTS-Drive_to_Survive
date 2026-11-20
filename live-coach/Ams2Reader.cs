using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace IntsLiveCoach
{
    // Independently implemented from the installed AMS2 v14 SDK. No SDK source
    // is redistributed. Open-existing/read-only; the game's seqlock is never written.
    public sealed class Ams2Reader : ITelemetryReader
    {
        internal const int PayloadBytes = 20700, SequenceOffset = 7320;
        const string ExpectedHash = "2ABAF09901883F80C6393AC065577FB6229FA0DB6784DABCDC502EEDA1A3674E";
        readonly byte[] bytes = new byte[PayloadBytes];
        readonly string sdkDirectory;
        MemoryMappedFile mapping;
        MemoryMappedViewAccessor view;
        DateTime nextConnect, connected, lastGood;
        uint lastSequence;
        bool hasSequence, disposed;
        string connectionStatus = "AMS2 대기 · Options → System → Shared Memory: Project CARS 2";
        readonly LapClock clock = new LapClock();
        public bool LastReadTransient { get; private set; }
        public Ams2Reader() : this(null) { }
        public Ams2Reader(string sdkDirectory) { this.sdkDirectory = sdkDirectory; }

        public bool TryRead(out TelemetryFrame frame, out string status)
        {
            frame = null; LastReadTransient = false; status = connectionStatus;
            if (disposed) { status = "AMS2 연결 종료"; return false; }
            DateTime now = DateTime.UtcNow;
            if (view == null)
            {
                if (now < nextConnect) return false;
                nextConnect = now.AddSeconds(1);
                if (!Connect(out status)) { connectionStatus = status; return false; }
            }
            try
            {
                uint before = view.ReadUInt32(SequenceOffset);
                if ((before & 1) != 0) return Waiting(now, out status);
                Thread.MemoryBarrier();
                if (view.ReadArray(0, bytes, 0, bytes.Length) != bytes.Length)
                { Close(); status = "AMS2 메모리 길이 불일치"; return false; }
                Thread.MemoryBarrier();
                uint after = view.ReadUInt32(SequenceOffset);
                if (before != after || U32(bytes, SequenceOffset) != before || (after & 1) != 0)
                    return Waiting(now, out status);
                if (hasSequence && before == lastSequence) return Waiting(now, out status);
                lastSequence = before; hasSequence = true;
                double lastLap;
                if (!TryDecode(bytes, out frame, out lastLap, out status))
                {
                    clock.Reset();
                    if ((now - lastGood).TotalSeconds > 2) Close();
                    return false;
                }
                clock.Apply(frame, lastLap);
                frame.CaptureUtc = now; lastGood = now;
                status = "AMS2 실시간 · PCARS2 공유 메모리";
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException) && !(ex is ObjectDisposedException) && !(ex is ArgumentException)) throw;
                Close(); status = "AMS2 연결 대기 · " + ex.GetType().Name; connectionStatus = status; return false;
            }
        }
        bool Waiting(DateTime now, out string status)
        {
            DateTime since = lastGood > connected ? lastGood : connected;
            LastReadTransient = (now - since).TotalSeconds < 1.5;
            status = LastReadTransient ? "AMS2 새 프레임 대기" : "AMS2 데이터 정지 · 메뉴 / 일시정지 / 게임 종료 확인";
            if (!LastReadTransient) { Close(); nextConnect = now.AddSeconds(1); connectionStatus = status; }
            return false;
        }
        bool Connect(out string status)
        {
            status = "AMS2 SDK 확인 필요";
            if (!BitConverter.IsLittleEndian) return false;
            string directory = FindSdk(sdkDirectory);
            if (directory == null) { status = "AMS2 SDK 없음 · AMS2_SDK_DIR에 SharedMemory.h 폴더 지정"; return false; }
            if (!VerifySdk(directory, out status)) return false;
            try
            {
                mapping = MemoryMappedFile.OpenExisting("$pcars2$", MemoryMappedFileRights.Read);
                view = mapping.CreateViewAccessor(0, PayloadBytes, MemoryMappedFileAccess.Read);
                connected = DateTime.UtcNow; clock.Reset(); return true;
            }
            catch (FileNotFoundException) { status = "AMS2 대기 · Options → System → Shared Memory: Project CARS 2"; }
            catch (UnauthorizedAccessException) { status = "AMS2와 코치를 같은 사용자 권한으로 실행하세요."; }
            catch (IOException) { status = "AMS2 공유 메모리 형식 확인 필요"; }
            catch (ArgumentException) { status = "AMS2 공유 메모리 크기 확인 필요"; }
            Close(); return false;
        }
        internal static bool TryDecode(byte[] data, out TelemetryFrame frame, out double lastLapTime, out string status)
        {
            frame = null; lastLapTime = double.NaN; status = "AMS2 데이터 형식 검증 실패";
            if (data == null || data.Length != PayloadBytes || U32(data, 0) != 14 || (U32(data, SequenceOffset) & 1) != 0) return false;
            int game = I32(data, 8), session = I32(data, 12), race = I32(data, 16);
            if (game != 2) { status = game == 6 || game == 7 ? "AMS2 리플레이 · 코칭 제외" : "AMS2 직접 주행 대기 · 메뉴 / 일시정지 제외"; return false; }
            if (session < 1 || session > 6 || session == 4 || race != 2)
            { status = "AMS2 주행 세션 대기 · 대기 / 포메이션 / 종료 제외"; return false; }
            int index = I32(data, 20), count = I32(data, 24);
            if (count < 1 || count > 64 || index < 0 || index >= count) return false;
            int p = 28 + index * 100;
            if (data[p] != 1 || data[6712] > 1) return false;
            int pit = I32(data, 6808), lap = I32(data, p + 92), gear = I32(data, 6876);
            double distance = F32(data, p + 80), length = F32(data, 6704), lapTime = F32(data, 6724);
            double speed = F32(data, 6848) * 3.6, throttle = F32(data, 6428), brake = F32(data, 6432), steer = F32(data, 6436), clutch = F32(data, 6440);
            string car = Text(data, 6444), location = Text(data, 6576), variation = Text(data, 6640);
            if (pit < 0 || pit > 5 || lap < 0 || lap > 100000 || gear < -1 || gear > 12 ||
                !Range(distance, 0, length + 1) || !Range(length, 100, 100000) || !Range(lapTime, 0, 86400) ||
                !Range(speed, 0, 550) || !Range(throttle, 0, 1) || !Range(brake, 0, 1) || !Range(steer, -1, 1) || !Range(clutch, 0, 1) || car.Length == 0 || location.Length == 0) return false;
            double fuel = F32(data, 6840), capacity = F32(data, 6844);
            frame = new TelemetryFrame { Time = lapTime, DistanceAgeSeconds = 0, Lap = lap, LapDistance = distance, TrackLength = length,
                SpeedKph = speed, Throttle = throttle, Brake = brake, Steering = steer, Clutch = double.NaN, Gear = gear,
                Handbrake = Optional(F32(data, 7404), 0, 1), FuelLitres = Range(fuel, 0, 1) && Range(capacity, 0, 1000) ? fuel * capacity : double.NaN,
                X = Optional(F32(data, p + 68), -1e7, 1e7), Y = Optional(F32(data, p + 72), -1e7, 1e7), Z = Optional(F32(data, p + 76), -1e7, 1e7),
                Track = location + (variation.Length == 0 ? "" : " / " + variation), Car = car, GameVersion = "AMS2-v14-" + U32(data, 4).ToString(CultureInfo.InvariantCulture),
                InPit = pit != 0, IsReplay = false, OnTrack = true, ValidLapKnown = true, IsValidLap = data[6712] == 0 };
            // The SDK does not define local-axis signs or absolute steering lock.
            // Preserve native steering, and leave directional dynamics unknown.
            // Clutch range is documented but engagement direction is not: leave
            // it unknown rather than teaching an inverted clutch-kick sequence.
            lastLapTime = Optional(F32(data, 6720), 0, 86400);
            status = "AMS2 주행 데이터"; return true;
        }
        internal sealed class LapClock
        {
            int lap = -1; double lapTime, offset; string key = "";
            public void Reset() { lap = -1; lapTime = offset = 0; key = ""; }
            public void Apply(TelemetryFrame frame, double completedTime)
            {
                string nextKey = frame.Track + "|" + frame.Car + "|" + frame.GameVersion;
                bool knownBoundary = nextKey == key && lap >= 0 && frame.Lap == lap + 1 && Range(completedTime, lapTime, lapTime + 2);
                if (frame.Time <= 0 && !knownBoundary)
                {
                    Reset(); frame.TimeIsCaptureClock = true;
                    frame.Time = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
                    return;
                }
                frame.TimeIsCaptureClock = false;
                if (nextKey != key || lap < 0) { Reset(); key = nextKey; }
                else if (frame.Lap == lap + 1 && Range(completedTime, lapTime, lapTime + 2)) offset += completedTime;
                else if (frame.Lap != lap || frame.Time < lapTime) offset = 0;
                lap = frame.Lap; lapTime = frame.Time; frame.Time += offset;
            }
        }
        internal static bool VerifySdk(string directory, out string status)
        {
            try
            {
                using (var sha = SHA256.Create()) using (var file = File.OpenRead(Path.Combine(directory, "SharedMemory.h")))
                    if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "") != ExpectedHash)
                    { status = "지원하지 않는 AMS2 SDK · 업데이트 형식 검증 필요"; return false; }
                status = "AMS2 SDK v14 확인됨"; return true;
            }
            catch (IOException) { status = "AMS2 SDK 파일을 찾을 수 없습니다."; return false; }
            catch (UnauthorizedAccessException) { status = "AMS2 SDK 파일 접근 권한 확인 필요"; return false; }
        }
        static string FindSdk(string explicitDirectory)
        {
            if (!string.IsNullOrWhiteSpace(explicitDirectory)) return explicitDirectory;
            string configured = Environment.GetEnvironmentVariable("AMS2_SDK_DIR");
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            string steam = null;
            try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) if (key != null) steam = key.GetValue("SteamPath") as string; } catch (System.Security.SecurityException) { }
            if (string.IsNullOrWhiteSpace(steam)) steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            string direct = SdkIn(steam); if (direct != null) return direct;
            try { foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf")), "\"path\"\\s*\"([^\"]+)\"")) { string found = SdkIn(m.Groups[1].Value.Replace("\\\\", "\\")); if (found != null) return found; } }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
        static string SdkIn(string library)
        {
            string dir = Path.Combine(library, "steamapps", "common", "Automobilista 2", "Support", "SharedMemory", "AMS2_SharedMemoryExampleApp");
            return File.Exists(Path.Combine(dir, "SharedMemory.h")) ? dir : null;
        }
        static uint U32(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }
        static int I32(byte[] b, int o) { return BitConverter.ToInt32(b, o); }
        static double F32(byte[] b, int o) { return BitConverter.ToSingle(b, o); }
        static bool Range(double v, double min, double max) { return !double.IsNaN(v) && !double.IsInfinity(v) && v >= min && v <= max; }
        static double Optional(double v, double min, double max) { return Range(v, min, max) ? v : double.NaN; }
        static string Text(byte[] b, int o) { int n = 0; while (n < 64 && b[o + n] != 0) n++; return n == 64 ? "" : Encoding.UTF8.GetString(b, o, n).Trim(); }
        void Close() { if (view != null) { view.Dispose(); view = null; } if (mapping != null) { mapping.Dispose(); mapping = null; } clock.Reset(); }
        public void Dispose() { if (disposed) return; disposed = true; Close(); }
    }
}
