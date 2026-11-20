using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace IntsLiveCoach
{
    public enum AssettoGame { Ac, Acc }

    // Native shared-memory adapter. No DLLs, plugins, process-memory reads,
    // game controls or network calls. See Assetto-NOTICES.txt for source detail.
    public sealed class AssettoReader : ITelemetryReader
    {
        internal const int PhysicsBytes = 580; // Common documented prefix only.
        internal const int AcGraphicsBytes = 296;
        internal const int AccGraphicsBytes = 1412; // Through isValidLap.
        internal const int StaticBytes = 592; // Through trackConfiguration.
        private readonly AssettoGame game;
        private readonly Func<string, double> verifiedTrackLength;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly AssettoLapClock lapClock = new AssettoLapClock();
        private readonly byte[] physics = new byte[PhysicsBytes], physicsCheck = new byte[PhysicsBytes];
        private readonly byte[] statics = new byte[StaticBytes], staticsCheck = new byte[StaticBytes];
        private readonly byte[] graphics, graphicsCheck;
        private MemoryMappedFile pMap, gMap, sMap;
        private MemoryMappedViewAccessor pView, gView, sView;
        private int processId = -1, previousPhysics, previousGraphics;
        private bool hasPhysics, hasGraphics, disposed, processAllowed;
        private double nextConnect, nextProcessCheck, connectedAt, graphicsChangedAt, lastGoodAt = -1;
        private string processStatus = "", waitingStatus = "게임 실행 대기";

        public bool LastReadTransient { get; private set; }
        public DateTime LastGoodUtc { get; private set; }
        public AssettoReader(AssettoGame game) : this(game, null) { }
        public AssettoReader(AssettoGame game, Func<string, double> verifiedTrackLength)
        {
            if (game != AssettoGame.Ac && game != AssettoGame.Acc) throw new ArgumentOutOfRangeException("game");
            this.game = game;
            this.verifiedTrackLength = verifiedTrackLength;
            graphics = new byte[game == AssettoGame.Acc ? AccGraphicsBytes : AcGraphicsBytes];
            graphicsCheck = new byte[graphics.Length];
        }

        public bool TryRead(out TelemetryFrame frame, out string status)
        {
            frame = null; LastReadTransient = false;
            if (disposed) { status = "연결이 닫혔습니다."; return false; }
            if (!BitConverter.IsLittleEndian) { status = "Windows little-endian 환경이 필요합니다."; return false; }
            double now = clock.Elapsed.TotalSeconds;
            if (now >= nextProcessCheck)
            {
                nextProcessCheck = now + 1;
                int found;
                processAllowed = CheckGameProcess(game, out found, out processStatus);
                if (!processAllowed || found != processId)
                {
                    ClosePages(); lapClock.Reset(); hasPhysics = hasGraphics = false;
                    processId = found;
                }
            }
            if (!processAllowed) { status = processStatus; return false; }
            try
            {
                if (pView == null)
                {
                    if (now < nextConnect) { status = waitingStatus; return false; }
                    nextConnect = now + 1;
                    if (!OpenPages(out status)) { waitingStatus = status; return false; }
                    connectedAt = now; graphicsChangedAt = now; hasGraphics = false;
                }

                bool stable = false;
                for (int attempt = 0; attempt < 3; ++attempt)
                {
                    // Packet counters are update indices, not a documented
                    // seqlock. Compare two entire snapshots as well as counters;
                    // this detects concurrent writes but cannot promise atomicity
                    // across the independent game pages.
                    Read(pView, physics); Read(gView, graphics); Read(sView, statics);
                    Read(pView, physicsCheck); Read(gView, graphicsCheck); Read(sView, staticsCheck);
                    if (Equal(physics, physicsCheck) && Equal(graphics, graphicsCheck) && Equal(statics, staticsCheck))
                    { stable = true; break; }
                }
                if (!stable) return Waiting(now, "데이터 갱신 중 · 다음 프레임 대기", out status);
                int pId = I32(physics, 0), gId = I32(graphics, 0);
                if (!hasGraphics || gId != previousGraphics)
                { graphicsChangedAt = now; previousGraphics = gId; hasGraphics = true; }
                if (hasPhysics && pId == previousPhysics)
                    return Waiting(now, "새 물리 프레임 대기 · 일시정지 여부 확인", out status);
                if (now - graphicsChangedAt > .5)
                    return Waiting(now, "주행 상태 데이터 갱신 대기", out status);

                string rawTrack = Wide(statics, 134, 33);
                double suppliedLength = double.NaN;
                if (game == AssettoGame.Acc && verifiedTrackLength != null)
                    suppliedLength = verifiedTrackLength(rawTrack);
                if (!TryDecode(game, physics, graphics, statics, suppliedLength, out frame, out status))
                {
                    if (now - Math.Max(connectedAt, lastGoodAt) > 2) ClosePages();
                    return false;
                }
                int completed = I32(graphics, 132), currentMs = I32(graphics, 140), lastMs = I32(graphics, 144);
                int session = I32(graphics, 8);
                if (game == AssettoGame.Acc) session = session * 10000 + I32(graphics, 1320);
                lapClock.Apply(frame, completed, currentMs, lastMs, session, now);
                // The pages have no common source timestamp. Unknown is honest;
                // the core applies its conservative unknown-age margin.
                frame.DistanceAgeSeconds = double.NaN;
                frame.CaptureUtc = DateTime.UtcNow;
                previousPhysics = pId; hasPhysics = true;
                lastGoodAt = now; LastGoodUtc = frame.CaptureUtc;
                status = game == AssettoGame.Acc
                    ? (Double.IsNaN(frame.TrackLength) ? "ACC 입력 연결 · 서킷 길이 미제공, m 단위 비교 대기" : "ACC 실시간 연결")
                    : "AC 입력 / 거동 연결 · 기본 API에 유효 랩 신호 없음";
                return true;
            }
            catch (IOException) { status = "공유 메모리 연결 대기 · 게임에서 직접 주행을 시작해 주세요"; }
            catch (UnauthorizedAccessException) { status = "게임과 코치를 같은 사용자 권한으로 실행해 주세요"; }
            catch (ArgumentException) { status = "지원 공유 메모리 크기 / 형식과 맞지 않습니다"; }
            catch (ObjectDisposedException) { status = "공유 메모리 연결이 닫혔습니다"; }
            ClosePages(); waitingStatus = status; return false;
        }

        private bool Waiting(double now, string pending, out string status)
        {
            LastReadTransient = now - Math.Max(connectedAt, lastGoodAt) < 1.5;
            status = LastReadTransient ? pending : "주행 데이터 정지 · 메뉴 / 일시정지 / 게임 종료 확인";
            if (!LastReadTransient) ClosePages();
            return false;
        }

        private bool OpenPages(out string status)
        {
            ClosePages();
            try
            {
                pMap = MemoryMappedFile.OpenExisting("acpmf_physics", MemoryMappedFileRights.Read);
                gMap = MemoryMappedFile.OpenExisting("acpmf_graphics", MemoryMappedFileRights.Read);
                sMap = MemoryMappedFile.OpenExisting("acpmf_static", MemoryMappedFileRights.Read);
                pView = pMap.CreateViewAccessor(0, PhysicsBytes, MemoryMappedFileAccess.Read);
                gView = gMap.CreateViewAccessor(0, graphics.Length, MemoryMappedFileAccess.Read);
                sView = sMap.CreateViewAccessor(0, StaticBytes, MemoryMappedFileAccess.Read);
                status = "공유 메모리 연결됨 · 주행 데이터 확인";
                return true;
            }
            catch (FileNotFoundException) { status = "공유 메모리 대기 · 차량에 탑승해 주행을 시작해 주세요"; }
            catch (IOException) { status = "공유 메모리 연결 / 크기를 확인해 주세요"; }
            catch (ArgumentException) { status = "공유 메모리 형식이 지원 범위와 다릅니다"; }
            ClosePages(); return false;
        }

        internal static bool TryDecode(AssettoGame game, byte[] p, byte[] g, byte[] s,
            double verifiedLength, out TelemetryFrame frame, out string status)
        {
            frame = null; status = "공유 메모리 형식 검증 실패 · 코칭 대기";
            int gBytes = game == AssettoGame.Acc ? AccGraphicsBytes : AcGraphicsBytes;
            if (p == null || g == null || s == null || p.Length != PhysicsBytes || g.Length != gBytes || s.Length != StaticBytes) return false;
            string schema = Wide(s, 0, 15), version = Wide(s, 30, 15);
            Version parsed;
            if (!Version.TryParse(schema, out parsed) || parsed.Major != 1 ||
                parsed.Minor != (game == AssettoGame.Acc ? 8 : 7))
            { status = "선택한 게임과 공유 메모리 ABI가 다릅니다 · AC 1.7 / ACC 1.8 계열 지원"; return false; }
            string car = Wide(s, 68, 33), track = Wide(s, 134, 33), layout = Wide(s, 524, 33);
            if (!Identifier(car) || !Identifier(track) || !Identifier(version)) return false;
            int state = I32(g, 4), session = I32(g, 8);
            if (state != 2)
            { status = state == 1 ? "리플레이 제외 · 직접 주행 대기" : state == 3 ? "게임 일시정지 · 코칭 대기" : "메뉴 / 세션 로딩 · 주행 대기"; return false; }
            if (session < 0 || session > (game == AssettoGame.Acc ? 8 : 6)) return false;
            int ai = I32(p, 416);
            if (ai != 0) { status = ai == 1 ? "AI 주행 제외 · 직접 운전해 주세요" : status; return false; }
            int completed = I32(g, 132), currentMs = I32(g, 140), lastMs = I32(g, 144);
            int pit = I32(g, 160), pitLane = I32(g, game == AssettoGame.Acc ? 1236 : 276);
            int gearIndex = I32(p, 16), rpm = I32(p, 20), sectors = I32(s, 400);
            double gas = F32(p, 4), brake = F32(p, 8), steer = F32(p, 24), speed = F32(p, 28);
            double norm = F32(g, 248);
            if (completed < 0 || completed > 100000 || currentMs < 0 || lastMs < 0 ||
                !Bool(pit) || !Bool(pitLane) || gearIndex < 0 || gearIndex > 15 || rpm < 0 || rpm > 40000 ||
                sectors < 0 || sectors > 20 || !Between(gas, 0, 1) || !Between(brake, 0, 1) ||
                !Between(steer, -1.01, 1.01) || !Between(speed, 0, 600)) return false;

            bool validKnown = false, valid = false;
            int positionOffset = 252;
            if (game == AssettoGame.Acc)
            {
                int cars = I32(g, 252), playerId = I32(g, 1216), setup = I32(g, 1256), validLap = I32(g, 1408);
                if (cars < 1 || cars > 60 || playerId < 0 || !Bool(setup) || !Bool(validLap)) return false;
                if (setup == 1) { status = "ACC 셋업 메뉴 · 직접 주행 대기"; return false; }
                positionOffset = -1;
                for (int i = 0; i < cars; ++i)
                {
                    if (I32(g, 976 + i * 4) != playerId) continue;
                    if (positionOffset >= 0) return false;
                    positionOffset = 256 + i * 12;
                }
                if (positionOffset < 0) return false;
                validKnown = true; valid = validLap == 1;
            }
            frame = new TelemetryFrame();
            frame.Time = currentMs / 1000.0;
            frame.Lap = completed + 1;
            frame.Throttle = gas; frame.Brake = brake; frame.Steering = Math.Max(-1, Math.Min(1, steer));
            frame.SpeedKph = speed; frame.Gear = gearIndex - 1;
            frame.Car = car;
            frame.Track = game == AssettoGame.Ac && Identifier(layout) ? track + "/" + layout : track;
            frame.GameVersion = (game == AssettoGame.Acc ? "ACC " : "AC ") + version + " (SM " + schema + ")";
            frame.OnTrack = true; frame.InPit = pit == 1 || pitLane == 1;
            frame.IsReplay = false; frame.ValidLapKnown = validKnown; frame.IsValidLap = valid;
            frame.X = Optional(F32(g, positionOffset), -1e7, 1e7);
            frame.Y = Optional(F32(g, positionOffset + 4), -1e7, 1e7);
            frame.Z = Optional(F32(g, positionOffset + 8), -1e7, 1e7);

            double length = game == AssettoGame.Ac ? F32(s, 520) : verifiedLength;
            if (Between(length, 100, 100000) && Between(norm, 0, 1))
            { frame.TrackLength = length; frame.LapDistance = norm * length; }
            // ACC docs label native fuel kg and accG global. Do not silently
            // relabel these as litres or local acceleration.
            if (game == AssettoGame.Ac)
            {
                frame.FuelLitres = Optional(F32(p, 12), 0, 1000);
                frame.LateralG = Optional(F32(p, 44), -30, 30);
                frame.LongitudinalG = Optional(F32(p, 52), -30, 30);
                frame.YawRate = Optional(F32(p, 300), -100, 100);
                double side = F32(p, 568), up = F32(p, 572), forward = F32(p, 576);
                double magnitudeKph = Math.Sqrt(side * side + up * up + forward * forward) * 3.6;
                if (Between(magnitudeKph, 0, 600) && Math.Abs(magnitudeKph - speed) < Math.Max(5, speed * .15))
                {
                    // Documented X lateral / Z forward. X right-sign has not
                    // been established; preserve magnitude only, never fake it.
                    frame.LocalVelocitySideAbs = Math.Abs(side);
                    frame.LocalVelocityForward = forward;
                }
            }
            // Native clutch polarity has conflicting documentation across these
            // APIs; absent live calibration, do not pretend it is pedal travel.
            // Base pages contain no handbrake input. Both stay NaN.
            status = game == AssettoGame.Acc ? "ACC 공유 메모리" : "AC 공유 메모리";
            return true;
        }

        private static bool CheckGameProcess(AssettoGame game, out int id, out string status)
        {
            // Both titles reuse the same acpmf_* names. Process identity plus
            // explicit smVersion family prevents interpreting the wrong game.
            int ac = FindProcess(new string[] { "acs", "acs_x86" });
            int acc = FindProcess(new string[] { "AC2-Win64-Shipping", "AC2-Win64-Test", "acc" });
            int other = FindProcess(new string[] { "acr", "ACR-Win64-Shipping", "AssettoCorsaEVO" });
            id = game == AssettoGame.Acc ? acc : ac;
            if (ac != -1 && acc != -1 || other != -1 || ac == -2 || acc == -2)
            { status = "AC 계열 게임을 하나만 실행해 주세요 · 공유 메모리 충돌 방지"; return false; }
            if (id < 0) { status = game == AssettoGame.Acc ? "ACC 실행 / 차량 탑승 대기" : "AC 주행 실행 대기 · Content Manager 메뉴만으로는 연결되지 않습니다"; return false; }
            status = "게임 프로세스 확인됨"; return true;
        }
        private static int FindProcess(string[] names)
        {
            int id = -1; bool ambiguous = false;
            foreach (string name in names)
            {
                Process[] processes = Process.GetProcessesByName(name);
                foreach (Process process in processes)
                {
                    using (process)
                    {
                        try { if (!process.HasExited) { if (id >= 0 && id != process.Id) ambiguous = true; id = process.Id; } }
                        catch (InvalidOperationException) { }
                    }
                }
            }
            return ambiguous ? -2 : id;
        }
        private static void Read(MemoryMappedViewAccessor view, byte[] buffer)
        { if (view.ReadArray(0, buffer, 0, buffer.Length) != buffer.Length) throw new IOException("Short memory page"); }
        private static bool Equal(byte[] a, byte[] b)
        { for (int i = 0; i < a.Length; ++i) if (a[i] != b[i]) return false; return true; }
        private static int I32(byte[] bytes, int offset) { return BitConverter.ToInt32(bytes, offset); }
        private static double F32(byte[] bytes, int offset) { return BitConverter.ToSingle(bytes, offset); }
        private static bool Bool(int value) { return value == 0 || value == 1; }
        private static bool Between(double v, double low, double high)
        { return !Double.IsNaN(v) && !Double.IsInfinity(v) && v >= low && v <= high; }
        private static double Optional(double v, double low, double high) { return Between(v, low, high) ? v : Double.NaN; }
        private static bool Identifier(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            foreach (char c in value) if (Char.IsControl(c)) return false;
            return true;
        }
        private static string Wide(byte[] b, int start, int chars)
        {
            int length = 0;
            while (length < chars && (b[start + length * 2] != 0 || b[start + length * 2 + 1] != 0)) ++length;
            if (length == chars) return "";
            try { return new UnicodeEncoding(false, false, true).GetString(b, start, length * 2).Trim(); }
            catch (DecoderFallbackException) { return ""; }
        }
        private void ClosePages()
        {
            if (pView != null) { pView.Dispose(); pView = null; }
            if (gView != null) { gView.Dispose(); gView = null; }
            if (sView != null) { sView.Dispose(); sView = null; }
            if (pMap != null) { pMap.Dispose(); pMap = null; }
            if (gMap != null) { gMap.Dispose(); gMap = null; }
            if (sMap != null) { sMap.Dispose(); sMap = null; }
        }
        public void Dispose() { if (disposed) return; ClosePages(); disposed = true; }
    }

    internal sealed class AssettoLapClock
    {
        private string identity = "";
        private int previousLap = -1, previousMs = -1, previousSession = -1;
        private double lapOffset, timerChangedAt;
        private bool incompleteClock;
        internal void Reset() { identity = ""; previousLap = previousMs = previousSession = -1; lapOffset = 0; incompleteClock = false; }
        internal void Apply(TelemetryFrame frame, int completedLaps, int currentMs, int lastMs, int session, double captureSeconds)
        {
            string key = frame.Track + "\n" + frame.Car;
            if (identity != key || previousSession != session || completedLaps < previousLap ||
                completedLaps == previousLap && currentMs < previousMs - 250)
            { Reset(); identity = key; previousSession = session; timerChangedAt = captureSeconds; }
            if (previousLap >= 0 && completedLaps > previousLap)
            {
                if (completedLaps == previousLap + 1 && lastMs > 0) lapOffset += lastMs / 1000.0;
                else incompleteClock = true;
            }
            if (currentMs != previousMs) timerChangedAt = captureSeconds;
            frame.TimeIsCaptureClock = incompleteClock || currentMs == 0 || captureSeconds - timerChangedAt > .5;
            frame.Time = frame.TimeIsCaptureClock ? captureSeconds : lapOffset + currentMs / 1000.0;
            previousLap = completedLaps; previousMs = currentMs;
        }
    }
}
