using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace IntsLiveCoach
{
    // ACR is a separate source. Read only the prefix independently used by two
    // ACR recorders; do not reinterpret the incompatible AC/ACC graphics tails.
    public sealed class AcrReader : ITelemetryReader
    {
        internal const int PhysicsBytes = 420, GraphicsBytes = 252, StaticBytes = 200;
        readonly byte[] physics = new byte[PhysicsBytes], physicsCheck = new byte[PhysicsBytes];
        readonly byte[] graphics = new byte[GraphicsBytes], graphicsCheck = new byte[GraphicsBytes];
        readonly byte[] statics = new byte[StaticBytes], staticsCheck = new byte[StaticBytes];
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly AcrCaptureClock capture = new AcrCaptureClock();
        MemoryMappedFile pMap, gMap, sMap;
        MemoryMappedViewAccessor pView, gView, sView;
        bool disposed, allowed, hasPhysics, hasGraphics;
        int processId = -1, lastPhysics, lastGraphics;
        double nextProcess, nextConnect, connectedAt, lastGood = -1, graphicsAt;
        string processStatus = "AC Rally 실행 대기", pendingStatus = "AC Rally 스테이지 주행 대기";
        public bool LastReadTransient { get; private set; }

        public bool TryRead(out TelemetryFrame frame, out string status)
        {
            frame = null; LastReadTransient = false; status = pendingStatus;
            if (disposed) { status = "AC Rally 연결 종료"; return false; }
            if (!BitConverter.IsLittleEndian) { status = "Windows little-endian 환경이 필요합니다."; return false; }
            double now = clock.Elapsed.TotalSeconds;
            if (now >= nextProcess)
            {
                nextProcess = now + 1;
                int found; allowed = CheckProcess(out found, out processStatus);
                if (!allowed || found != processId) { Close(); capture.Reset(); processId = found; }
            }
            if (!allowed) { status = processStatus; return false; }
            try
            {
                if (pView == null)
                {
                    if (now < nextConnect) return false;
                    nextConnect = now + 1;
                    if (!Open(out status)) { pendingStatus = status; return false; }
                    connectedAt = graphicsAt = now; lastGood = -1; hasPhysics = hasGraphics = false;
                }
                bool stable = false;
                for (int attempt = 0; attempt < 3; ++attempt)
                {
                    Read(pView, physics); Read(gView, graphics); Read(sView, statics);
                    Read(pView, physicsCheck); Read(gView, graphicsCheck); Read(sView, staticsCheck);
                    if (Same(physics, physicsCheck) && Same(graphics, graphicsCheck) && Same(statics, staticsCheck)) { stable = true; break; }
                }
                if (!stable) return Wait(now, "공유 메모리 갱신 중", out status);
                // Explicit menu/pause/replay/AI/pit state wins over a held
                // physics counter. Short pauses must not count as input time.
                if (!AllowsLiveState(physics, graphics, out status)) { Close(); pendingStatus = status; return false; }
                int p = I32(physics, 0), g = I32(graphics, 0);
                if (!hasGraphics || g != lastGraphics) { graphicsAt = now; lastGraphics = g; hasGraphics = true; }
                if (!hasPhysics) { lastPhysics = p; hasPhysics = true; return Wait(now, "새 물리 프레임 확인 중", out status); }
                if (p == lastPhysics || now - graphicsAt > .5) return Wait(now, "새 주행 데이터 대기", out status);
                if (!TryDecode(physics, graphics, statics, out frame, out status))
                {
                    // A stage/menu transition can replace the named mappings.
                    // Do not pin an invalid old view forever waiting for a
                    // packet that will only arrive in the newly created map.
                    Close(); pendingStatus = status; return false;
                }
                lastPhysics = p;
                if (!capture.Apply(frame, I32(graphics, 140), I32(graphics, 8), now))
                { frame = null; status = "AC Rally 스테이지 재시작 · 이전 관찰 초기화"; return false; }
                frame.CaptureUtc = DateTime.UtcNow; lastGood = now;
                status = "AC Rally 입력 연결 · 실험적 공통 형식 · 수집 시계";
                return true;
            }
            catch (IOException) { status = "AC Rally 공유 메모리 대기"; }
            catch (UnauthorizedAccessException) { status = "AC Rally와 코치를 같은 사용자 권한으로 실행하세요."; }
            catch (ArgumentException) { status = "AC Rally 메모리 크기 / 형식 확인 필요"; }
            catch (ObjectDisposedException) { status = "AC Rally 연결 종료"; }
            Close(); pendingStatus = status; return false;
        }

        bool Open(out string status)
        {
            Close();
            try
            {
                pMap = MemoryMappedFile.OpenExisting("acpmf_physics", MemoryMappedFileRights.Read);
                gMap = MemoryMappedFile.OpenExisting("acpmf_graphics", MemoryMappedFileRights.Read);
                sMap = MemoryMappedFile.OpenExisting("acpmf_static", MemoryMappedFileRights.Read);
                pView = pMap.CreateViewAccessor(0, PhysicsBytes, MemoryMappedFileAccess.Read);
                gView = gMap.CreateViewAccessor(0, GraphicsBytes, MemoryMappedFileAccess.Read);
                sView = sMap.CreateViewAccessor(0, StaticBytes, MemoryMappedFileAccess.Read);
                status = "AC Rally 메모리 연결 · 새 프레임 대기"; return true;
            }
            catch (FileNotFoundException) { status = "AC Rally 스테이지에 진입해 직접 주행하세요."; }
            catch (IOException) { status = "AC Rally 메모리 크기 / 연결 확인 필요"; }
            catch (ArgumentException) { status = "AC Rally 메모리 형식 확인 필요"; }
            Close(); return false;
        }
        bool Wait(double now, string message, out string status)
        {
            LastReadTransient = now - Math.Max(connectedAt, lastGood) < 1.5;
            status = LastReadTransient ? message : "AC Rally 데이터 정지 · 메뉴 / 일시정지 / 종료 확인";
            if (!LastReadTransient) Close();
            return false;
        }

        internal static bool TryDecode(byte[] p, byte[] g, byte[] s, out TelemetryFrame frame, out string status)
        {
            frame = null; status = "AC Rally 공통 데이터 형식 검증 실패 · 코칭 보류";
            if (p == null || g == null || s == null || p.Length != PhysicsBytes || g.Length != GraphicsBytes || s.Length != StaticBytes) return false;
            string schema = Wide(s, 0, 15), version = Wide(s, 30, 15), car = Wide(s, 68, 33), track = Wide(s, 134, 33);
            // A public authoritative ACR ABI version table has not been found.
            // Record actual versions; do not invent a "certified" whitelist.
            if (!Label(schema) || !Label(version) || !Label(car) || track == null || (track.Length > 0 && !Label(track))) return false;
            if (!AllowsLiveState(p, g, out status)) return false;
            int session = I32(g, 8), pit = I32(g, 160);
            int gear = I32(p, 16), rpm = I32(p, 20), currentMs = I32(g, 140);
            double speed = F32(p, 28), gas = F32(p, 4), brake = F32(p, 8), steer = F32(p, 24);
            if (session < 0 || session > 8 || (pit != 0 && pit != 1) || currentMs < 0 || gear < -1 || gear > 12 || rpm < 0 || rpm > 40000 ||
                !Range(speed, 0, 550) || !Range(gas, 0, 1) || !Range(brake, 0, 1) || !Range(steer, -1.01, 1.01)) return false;
            frame = new TelemetryFrame { Time = currentMs / 1000.0, TimeIsCaptureClock = true,
                SpeedKph = speed, Throttle = gas, Brake = brake, Steering = Math.Max(-1, Math.Min(1, steer)), Gear = gear,
                Car = car, Track = track.Length == 0 ? "AC Rally · 스테이지 이름 미제공" : track,
                GameVersion = "ACR " + version + " (SM " + schema + "; prefix experimental)",
                OnTrack = true, InPit = pit == 1, IsReplay = false, ValidLapKnown = false, IsValidLap = false };
            // No circuit lap, stage-validity or metre-based reference is inferred.
            // Handbrake, clutch polarity and body axes have insufficient evidence.
            // They remain NaN, never fake zero / original-AC axis conventions.
            status = "AC Rally 입력 데이터 · 원리 관찰"; return true;
        }

        internal static bool AllowsLiveState(byte[] p, byte[] g, out string status)
        {
            status = "AC Rally 주행 상태 확인 필요";
            if (p == null || g == null || p.Length < PhysicsBytes || g.Length < GraphicsBytes) return false;
            int state = I32(g, 4), ai = I32(p, 416), pit = I32(g, 160);
            if (state != 2) { status = state == 1 ? "AC Rally 리플레이 제외" : state == 3 ? "AC Rally 일시정지" : "AC Rally 메뉴 / 로딩 대기"; return false; }
            if (ai != 0) { status = ai == 1 ? "AC Rally AI 제어 제외 · 직접 운전하세요." : status; return false; }
            if (pit != 0) { status = pit == 1 ? "AC Rally 피트 / 서비스 구간 · 관찰 중단" : status; return false; }
            return true;
        }

        internal static bool IsGamePath(string path)
        { return path != null && path.Replace('\\', '/').EndsWith("/acr/binaries/win64/acr.exe", StringComparison.OrdinalIgnoreCase); }
        static bool CheckProcess(out int id, out string status)
        {
            id = -1; status = "AC Rally 실행 / 스테이지 주행 대기";
            string[] conflicts = { "acs", "acs_x86", "AC2-Win64-Shipping", "AC2-Win64-Test", "acc", "AssettoCorsaEVO", "ACE", "ACE-Win64-Shipping" };
            foreach (string name in conflicts)
            {
                Process[] found = Process.GetProcessesByName(name);
                bool running = false;
                foreach (Process p in found) using (p) { try { if (!p.HasExited) running = true; } catch (InvalidOperationException) { } }
                if (running) { status = "AC / ACC / EVO를 닫고 AC Rally만 실행하세요 · 같은 메모리 이름 충돌"; return false; }
            }
            Process[] candidates = Process.GetProcessesByName("acr"); bool ambiguous = false, inaccessible = false;
            foreach (Process p in candidates) using (p)
            {
                try
                {
                    if (p.HasExited) continue;
                    if (!IsGamePath(p.MainModule.FileName)) { ambiguous = true; continue; }
                    if (id >= 0) ambiguous = true;
                    id = p.Id;
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { inaccessible = true; }
            }
            if (ambiguous || inaccessible) { id = -1; status = "AC Rally 실행 파일 / 단일 인스턴스 확인 필요 · 같은 사용자 권한으로 실행하세요."; return false; }
            return id >= 0;
        }
        static void Read(MemoryMappedViewAccessor v, byte[] b) { if (v.ReadArray(0, b, 0, b.Length) != b.Length) throw new IOException("Short memory prefix"); }
        static bool Same(byte[] a, byte[] b) { for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        static int I32(byte[] b, int i) { return BitConverter.ToInt32(b, i); }
        static double F32(byte[] b, int i) { return BitConverter.ToSingle(b, i); }
        static bool Range(double v, double lo, double hi) { return !double.IsNaN(v) && !double.IsInfinity(v) && v >= lo && v <= hi; }
        static bool Label(string s) { if (String.IsNullOrWhiteSpace(s)) return false; foreach (char c in s) if (Char.IsControl(c)) return false; return true; }
        static string Wide(byte[] b, int at, int count)
        {
            int n = 0; while (n < count && (b[at + n * 2] != 0 || b[at + n * 2 + 1] != 0)) n++;
            if (n == count) return null;
            try { return new UnicodeEncoding(false, false, true).GetString(b, at, n * 2).Trim(); }
            catch (DecoderFallbackException) { return null; }
        }
        void Close()
        {
            if (pView != null) { pView.Dispose(); pView = null; } if (gView != null) { gView.Dispose(); gView = null; } if (sView != null) { sView.Dispose(); sView = null; }
            if (pMap != null) { pMap.Dispose(); pMap = null; } if (gMap != null) { gMap.Dispose(); gMap = null; } if (sMap != null) { sMap.Dispose(); sMap = null; }
            hasPhysics = hasGraphics = false;
            capture.Reset();
        }
        public void Dispose() { if (disposed) return; disposed = true; Close(); capture.Reset(); }
    }

    // Duration of observed inputs only. Never a stage/lap performance clock.
    internal sealed class AcrCaptureClock
    {
        string context = ""; int previousMs = -1, session = -1; double epoch;
        internal void Reset() { context = ""; previousMs = session = -1; epoch = 0; }
        internal bool Apply(TelemetryFrame f, int stageMs, int stageSession, double receivedAt)
        {
            string key = f.GameVersion + "|" + f.Car + "|" + f.Track;
            bool changed = context != key || session != stageSession || (previousMs >= 0 && stageMs < previousMs - 250);
            bool discontinuity = changed && previousMs >= 0;
            if (changed) { epoch = receivedAt; context = key; session = stageSession; }
            previousMs = stageMs; f.Time = Math.Max(0, receivedAt - epoch); f.TimeIsCaptureClock = true;
            return !discontinuity;
        }
    }
}
