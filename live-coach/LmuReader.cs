using System;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace IntsLiveCoach
{
    // Compatible reader, not a redistributed SDK. See THIRD_PARTY_NOTICES.txt.
    // LMU_Data is opened READ-ONLY. Only the separate SDK synchronization lock
    // is updated. No process access, game controls, plugins, or network requests.
    public sealed class LmuReader : ITelemetryReader
    {
        internal const int PayloadBytes = 324820;
        internal const int ScoringOffset = 1632;
        internal const int ScoringCarsOffset = 2192;
        internal const int ScoringCarBytes = 584;
        internal const int TelemetryOffset = 128464;
        internal const int TelemetryCarsOffset = 128468;
        internal const int TelemetryCarBytes = 1888;
        internal const int MaxCars = 104;

        private const uint Synchronize = 0x00100000;
        private const uint EventModifyState = 0x0002;
        private const uint FileMapReadWrite = 0x0006;
        private const uint WaitObject0 = 0;
        private readonly string explicitSdkDirectory;
        private readonly byte[] snapshot = new byte[PayloadBytes];
        private MemoryMappedFile dataMap;
        private MemoryMappedViewAccessor dataView;
        private IntPtr holdEvent, dataEvent, lockEvent, lockMap, lockAddress;
        private DateTime nextConnectUtc, connectedUtc;
        private double previousTime = double.NaN;
        private string previousIdentity = "";
        private string disconnectedStatus = "LMU 실행 대기 · 게임 설정에서 Enable Plugins를 켜 주세요.";
        private bool disposed, holdGatePassed;

        public bool LastReadTransient { get; private set; }
        public DateTime LastGoodUtc { get; private set; }
        public string SdkDirectory { get; private set; }

        public LmuReader() : this(null) { }
        public LmuReader(string sdkDirectory) { explicitSdkDirectory = sdkDirectory; }

        public bool TryRead(out TelemetryFrame frame, out string status)
        {
            frame = null;
            LastReadTransient = false;
            if (disposed) { status = "LMU 연결이 닫혔습니다."; return false; }
            DateTime now = DateTime.UtcNow;
            if (dataView == null)
            {
                if (now < nextConnectUtc) { status = disconnectedStatus; return false; }
                nextConnectUtc = now.AddSeconds(1);
                if (!TryConnect(out status)) { disconnectedStatus = status; return false; }
            }
            try
            {
                // The installed SDK requires Hold THEN Data; neither event is
                // created/reset/signalled by this reader. Polling never blocks UI.
                if (!holdGatePassed) holdGatePassed = WaitForSingleObject(holdEvent, 0) == WaitObject0;
                if (!holdGatePassed || WaitForSingleObject(dataEvent, 0) != WaitObject0)
                    return NoFreshFrame(now, "LMU 새 프레임 대기", out status);
                holdGatePassed = false;

                if (!TryAcquireLock())
                    return NoFreshFrame(now, "LMU 데이터 동기화 대기", out status);
                try
                {
                    // Copy under the SDK lock, then release BEFORE validation or
                    // coaching. A failed copy also releases in the finally block.
                    if (dataView.ReadArray(0, snapshot, 0, snapshot.Length) != snapshot.Length)
                    { status = "LMU 데이터 크기가 맞지 않습니다. SDK 확인 필요."; return false; }
                }
                finally { ReleaseLock(); }

                bool coherenceWait;
                if (!TryDecode(snapshot, out frame, out status, out coherenceWait))
                {
                    if (coherenceWait) return NoFreshFrame(now, status, out status);
                    // Do not keep an orphaned mapping alive indefinitely when
                    // the game exits on a paused/menu/invalid snapshot.
                    DateTime since = LastGoodUtc > connectedUtc ? LastGoodUtc : connectedUtc;
                    if ((now - since).TotalSeconds >= 2)
                    {
                        CloseConnection();
                        disconnectedStatus = status;
                        nextConnectUtc = now.AddSeconds(1);
                    }
                    return false;
                }
                string identity = frame.Track + "\n" + frame.Car;
                if (frame.Time == previousTime && identity == previousIdentity)
                {
                    frame = null;
                    return NoFreshFrame(now, "LMU 시간이 멈췄습니다 · 일시정지 / 새 프레임 대기", out status);
                }
                previousTime = frame.Time;
                previousIdentity = identity;
                LastGoodUtc = now;
                frame.CaptureUtc = now;
                status = "LMU 실시간 연결";
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException) &&
                    !(ex is ObjectDisposedException) && !(ex is ArgumentException)) throw;
                CloseConnection();
                status = "LMU 연결을 다시 기다립니다 · " + ex.GetType().Name;
                disconnectedStatus = status;
                return false;
            }
        }

        private bool NoFreshFrame(DateTime now, string pending, out string status)
        {
            DateTime since = LastGoodUtc > connectedUtc ? LastGoodUtc : connectedUtc;
            LastReadTransient = (now - since).TotalSeconds < 2;
            status = LastReadTransient ? pending : "LMU 데이터 정지 · 일시정지 / 메뉴 / 게임 종료 확인";
            if (!LastReadTransient)
            {
                CloseConnection();
                disconnectedStatus = status;
                nextConnectUtc = now.AddSeconds(1);
            }
            return false;
        }

        private bool TryConnect(out string status)
        {
            CloseConnection();
            if (IntPtr.Size != 8 || !BitConverter.IsLittleEndian)
            { status = "LMU 연결에는 Windows x64 실행 파일이 필요합니다."; return false; }
            SdkDirectory = FindSdkDirectory(explicitSdkDirectory);
            if (SdkDirectory == null)
            { status = "LMU SDK를 찾지 못했습니다 · LMU_SDK_DIR에 Support\\SharedMemoryInterface 경로 지정"; return false; }
            if (!VerifySdk(SdkDirectory, out status)) return false;
            try
            {
                // OpenExisting is essential: never fabricate an empty LMU_Data.
                dataMap = MemoryMappedFile.OpenExisting("LMU_Data", MemoryMappedFileRights.Read);
                dataView = dataMap.CreateViewAccessor(0, PayloadBytes, MemoryMappedFileAccess.Read);
                // Windows rounds mappings to pages. Capacity is a lower-bound
                // check, not an invented ABI version or exact sizeof assertion.
                if (dataView.Capacity < PayloadBytes) throw new IOException("Short LMU mapping");
                holdEvent = OpenEvent(Synchronize, false, "LMU_Data_HoldEvent");
                dataEvent = OpenEvent(Synchronize, false, "LMU_Data_DataEvent");
                lockEvent = OpenEvent(EventModifyState, false, "LMU_SharedMemoryLockEvent");
                lockMap = OpenFileMapping(FileMapReadWrite, false, "LMU_SharedMemoryLockData");
                if (holdEvent == IntPtr.Zero || dataEvent == IntPtr.Zero ||
                    lockEvent == IntPtr.Zero || lockMap == IntPtr.Zero)
                { CloseConnection(); status = "LMU 동기화 인터페이스 없음 · 최신 게임 / Enable Plugins 확인"; return false; }
                lockAddress = MapViewOfFile(lockMap, FileMapReadWrite, 0, 0, new UIntPtr(8));
                if (lockAddress == IntPtr.Zero)
                { CloseConnection(); status = "LMU 동기화 연결 실패"; return false; }
                connectedUtc = DateTime.UtcNow;
                status = "LMU 주행 데이터 대기";
                return true;
            }
            catch (FileNotFoundException) { status = "LMU 실행 대기 · 게임 설정에서 Enable Plugins를 켜 주세요."; }
            catch (WaitHandleCannotBeOpenedException) { status = "LMU 동기화 데이터 대기"; }
            catch (UnauthorizedAccessException) { status = "LMU 데이터 접근 권한이 다릅니다 · 두 앱을 같은 사용자 권한으로 실행"; }
            catch (IOException) { status = "LMU 데이터 형식 / 연결 확인 필요"; }
            catch (ArgumentException) { status = "LMU 공유 메모리 크기 불일치 · 지원 SDK 확인 필요"; }
            CloseConnection();
            return false;
        }

        private unsafe bool TryAcquireLock()
        {
            // SDK LockData is { int waiters; int busy; }. This bounded spin-only
            // reader never registers as a waiter and never resets the game lock.
            int* state = (int*)lockAddress.ToPointer();
            for (int attempt = 0; attempt < 32; ++attempt)
            {
                if (Interlocked.CompareExchange(ref state[1], 1, 0) == 0) return true;
                Thread.SpinWait(4);
            }
            return false;
        }

        private unsafe void ReleaseLock()
        {
            int* state = (int*)lockAddress.ToPointer();
            Interlocked.Exchange(ref state[1], 0);
            if (Thread.VolatileRead(ref state[0]) > 0) SetEvent(lockEvent);
        }

        internal static bool TryDecode(byte[] bytes, out TelemetryFrame frame, out string status)
        {
            bool coherenceWait;
            return TryDecode(bytes, out frame, out status, out coherenceWait);
        }

        internal static bool TryDecode(byte[] bytes, out TelemetryFrame frame, out string status, out bool coherenceWait)
        {
            frame = null;
            coherenceWait = false;
            status = "LMU 데이터 형식 검증 실패 · 코칭 대기";
            if (bytes == null || bytes.Length != PayloadBytes) return false;
            int version = I32(bytes, 64);
            int location = bytes[96];
            int count = I32(bytes, ScoringOffset + 104);
            int active = bytes[TelemetryOffset];
            int player = bytes[TelemetryOffset + 1];
            int hasPlayer = bytes[TelemetryOffset + 2];
            int phase = bytes[ScoringOffset + 108];
            int realtime = bytes[ScoringOffset + 115];
            if (version <= 0 || location > 3 || phase > 9 || realtime > 1 || hasPlayer > 1 ||
                count < 0 || count > MaxCars || active > MaxCars) return false;
            if (phase == 9) { status = "LMU 일시정지 · 코칭도 대기합니다"; return false; }
            if (location != 3 || realtime != 1)
            { status = "LMU 메뉴 / 모니터 화면 · 직접 주행을 시작해 주세요"; return false; }
            if (hasPlayer == 0 || active == 0 || count == 0)
            { status = "LMU 플레이어 차량 대기"; return false; }
            if (player >= active) return false;
            int t = TelemetryCarsOffset + player * TelemetryCarBytes;
            int vehicleId = I32(bytes, t);
            if (vehicleId < 0) return false;
            int scoring = -1;
            for (int i = 0; i < count; ++i)
            {
                int candidate = ScoringCarsOffset + i * ScoringCarBytes;
                if (I32(bytes, candidate) != vehicleId) continue;
                if (scoring >= 0) return false; // Duplicate IDs cannot identify the driver safely.
                scoring = candidate;
            }
            if (scoring < 0 || bytes[scoring + 196] != 1) return false;
            int control = unchecked((sbyte)bytes[scoring + 197]);
            if (control != 0)
            { status = control == 3 ? "LMU 리플레이 · 코칭 대기" : "LMU 직접 운전 대기 · AI / 다른 차량 제외"; return false; }
            if (bytes[scoring + 198] > 1 || bytes[scoring + 507] > 1 ||
                bytes[t + 745] > 1 || bytes[scoring + 506] > 2) return false;
            if (bytes[scoring + 507] == 1)
            { status = "LMU 차고 안 · 주행 시작 대기"; return false; }

            double time = F64(bytes, t + 12);
            double scoreTime = F64(bytes, ScoringOffset + 68);
            double telemetryLapStart = F64(bytes, t + 24);
            double scoringLapStart = F64(bytes, scoring + 256);
            double length = F64(bytes, ScoringOffset + 88);
            double distance = F64(bytes, scoring + 104);
            double vx = F64(bytes, t + 184), vy = F64(bytes, t + 192), vz = F64(bytes, t + 200);
            double speed = Math.Sqrt(vx * vx + vy * vy + vz * vz) * 3.6;
            double throttle = F64(bytes, t + 388), brake = F64(bytes, t + 396);
            double steering = F64(bytes, t + 404), clutch = F64(bytes, t + 412);
            // mTotalLaps and mLapDist belong to the same scoring update. Pairing
            // distance with the faster telemetry lap counter breaks finish-line
            // detection whenever one channel advances before the other.
            int lap = BitConverter.ToInt16(bytes, scoring + 100) + 1;
            int gear = I32(bytes, t + 352);
            string car = CString(bytes, t + 32, 64), track = CString(bytes, t + 96, 64);
            string scoringTrack = CString(bytes, ScoringOffset, 64);
            if (!Between(time, 0, 1e9) || !Between(scoreTime, 0, 1e9) || Math.Abs(time - scoreTime) > 1.5 ||
                !Between(telemetryLapStart, 0, time + 1) || !Between(scoringLapStart, 0, time + 1) ||
                !Between(length, 100, 100000) || !Between(distance, -1000, length + 1000) ||
                !Between(speed, 0, 600) || !Between(throttle, 0, 1) || !Between(brake, 0, 1) ||
                !Between(steering, -1, 1) || !Between(clutch, 0, 1) || lap < 1 ||
                gear < -1 || gear > 12 || String.IsNullOrWhiteSpace(car) || String.IsNullOrWhiteSpace(track) ||
                !String.Equals(track, scoringTrack, StringComparison.Ordinal)) return false;

            // Input/validity is telemetry-rate, while distance/lap is scoring-rate.
            // A crossing may reach either channel first. Wait for the same lap
            // epoch so the previous lap's invalid flag cannot poison the next.
            // Compare timestamps instead of assuming telemetry lap-number origin.
            if (Math.Abs(telemetryLapStart - scoringLapStart) > 0.05)
            {
                coherenceWait = true;
                status = "LMU 출발선 데이터 동기화 대기";
                return false;
            }

            frame = new TelemetryFrame();
            frame.Time = time;
            // Both SDK clocks are session elapsed time. Keep absolute skew:
            // scoring can publish ahead of telemetry, and that is not zero age.
            // This is a measured timestamp difference, not a latency guarantee.
            frame.DistanceAgeSeconds = Math.Abs(time - scoreTime);
            frame.Lap = lap;
            // Distance is the game's scoring channel, not a fabricated high-rate
            // estimate. It may update less frequently than pedals / speed.
            frame.LapDistance = distance;
            frame.PathLateralM = Optional(F64(bytes, scoring + 112), -1000, 1000);
            frame.TrackLength = length;
            frame.SpeedKph = speed;
            frame.Throttle = throttle;
            frame.Brake = brake;
            frame.Steering = steering;
            // SDK specifies only 0..1, not pedal endpoint polarity. The shared
            // contract requires 1 = pressed/disengaged; leave it unknown until
            // that interpretation is verified, preventing false clutch-kick cues.
            frame.Clutch = Double.NaN;
            frame.Gear = gear;
            frame.FuelLitres = Optional(F64(bytes, t + 524), 0, 1000);
            frame.X = Optional(F64(bytes, t + 160), -1e7, 1e7);
            frame.Y = Optional(F64(bytes, t + 168), -1e7, 1e7);
            frame.Z = Optional(F64(bytes, t + 176), -1e7, 1e7);
            frame.YawRate = Optional(F64(bytes, t + 312), -100, 100);
            frame.LateralG = Optional(F64(bytes, t + 208) / 9.80665, -100, 100);
            frame.Track = track;
            frame.Car = car;
            frame.GameVersion = version.ToString(CultureInfo.InvariantCulture);
            frame.InPit = bytes[scoring + 198] == 1;
            frame.IsReplay = false; // Control == 0 and realtime == 1 were required above.
            frame.OnTrack = true;
            frame.ValidLapKnown = true;
            frame.IsValidLap = bytes[t + 745] == 0 && bytes[scoring + 506] == 2;
            status = "LMU 실시간 데이터";
            return true;
        }

        private static int I32(byte[] bytes, int offset) { return BitConverter.ToInt32(bytes, offset); }
        private static double F64(byte[] bytes, int offset) { return BitConverter.ToDouble(bytes, offset); }
        private static bool Between(double value, double min, double max)
        { return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= min && value <= max; }
        private static double Optional(double value, double min, double max)
        { return Between(value, min, max) ? value : Double.NaN; }
        private static string CString(byte[] bytes, int offset, int max)
        {
            int length = 0;
            while (length < max && bytes[offset + length] != 0) ++length;
            if (length == max) return "";
            return Encoding.UTF8.GetString(bytes, offset, length).Trim();
        }

        internal static bool VerifySdk(string directory, out string status)
        {
            // gameVersion is a game build value, NOT a documented ABI version.
            // Fail closed on unknown installed headers; do not guess layouts.
            string[] names = { "InternalsPlugin.hpp", "PluginObjects.hpp", "SharedMemoryInterface.hpp" };
            string[] hashes = {
                "9B6EE8CF610FA5049B18DF580A9A9BC9EBB91346FC466584D576A6442ABCF68F",
                "F65F1D2226AF1ACB277F8337FB10D8955DB384118FC36EEF95EA446BE058E247",
                "A82833D5E9E277A7C3AF8802518E35EA62F4EC7C7C7C9083650282E4EDF1F8BF"
            };
            try
            {
                for (int i = 0; i < names.Length; ++i)
                {
                    string path = Path.Combine(directory, names[i]);
                    using (SHA256 sha = SHA256.Create())
                    using (FileStream stream = File.OpenRead(path))
                    {
                        string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                        if (!String.Equals(hash, hashes[i], StringComparison.Ordinal))
                        { status = "지원하지 않는 LMU SDK · 업데이트된 형식 검증 전 코칭 중지 (" + names[i] + ")"; return false; }
                    }
                }
                status = "LMU 설치 SDK 형식 확인됨";
                return true;
            }
            catch (IOException) { status = "LMU SDK 파일을 읽을 수 없습니다."; return false; }
            catch (UnauthorizedAccessException) { status = "LMU SDK 파일 접근 권한을 확인해 주세요."; return false; }
        }

        private static string FindSdkDirectory(string explicitPath)
        {
            if (!String.IsNullOrWhiteSpace(explicitPath)) return explicitPath;
            string configured = Environment.GetEnvironmentVariable("LMU_SDK_DIR");
            if (!String.IsNullOrWhiteSpace(configured)) return configured;
            string steam = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (key != null) steam = key.GetValue("SteamPath") as string;
            }
            catch (System.Security.SecurityException) { }
            if (String.IsNullOrWhiteSpace(steam))
                steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            string direct = SdkUnderLibrary(steam);
            if (direct != null) return direct;
            try
            {
                string libraries = File.ReadAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"));
                foreach (Match match in Regex.Matches(libraries, "\"path\"\\s*\"([^\"]+)\""))
                {
                    string found = SdkUnderLibrary(match.Groups[1].Value.Replace("\\\\", "\\"));
                    if (found != null) return found;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        private static string SdkUnderLibrary(string library)
        {
            string directory = Path.Combine(library, "steamapps", "common", "Le Mans Ultimate", "Support", "SharedMemoryInterface");
            return File.Exists(Path.Combine(directory, "SharedMemoryInterface.hpp")) ? directory : null;
        }

        private void CloseConnection()
        {
            // Called on the same thread as TryRead; no lock is held here.
            holdGatePassed = false;
            if (lockAddress != IntPtr.Zero) { UnmapViewOfFile(lockAddress); lockAddress = IntPtr.Zero; }
            Close(ref lockMap); Close(ref lockEvent); Close(ref dataEvent); Close(ref holdEvent);
            if (dataView != null) { dataView.Dispose(); dataView = null; }
            if (dataMap != null) { dataMap.Dispose(); dataMap = null; }
        }

        private static void Close(ref IntPtr handle)
        { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
        public void Dispose() { if (disposed) return; CloseConnection(); disposed = true; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenEvent(uint access, bool inherit, string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);
        [DllImport("kernel32.dll")]
        private static extern bool UnmapViewOfFile(IntPtr address);
        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll")]
        private static extern bool SetEvent(IntPtr handle);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
