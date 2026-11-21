using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    // Standard EA WRC "wrc" packet, from the installed generated channel schema.
    // localhost only, receive only; never changes config or the SimHub endpoints.
    public sealed class WrcReader : ITelemetryReader
    {
        internal const int PacketBytes = 237;
        readonly int port;
        readonly string telemetryDirectory;
        UdpClient receiver;
        DateTime nextConnect, connected, lastGood;
        bool disposed, hasPrevious;
        ulong previousPacket, previousFrame;
        double previousTime;
        string connectionStatus = "EA WRC 대기 · wrc/session_update → 127.0.0.1:20777 활성화 필요";
        public bool LastReadTransient { get; private set; }
        public WrcReader() : this(20777, null) { }
        public WrcReader(int port) : this(port, null) { }
        public WrcReader(int port, string telemetryDirectory)
        {
            if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException("port");
            this.port = port;
            this.telemetryDirectory = telemetryDirectory ?? Environment.GetEnvironmentVariable("WRC_TELEMETRY_DIR") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "WRC", "telemetry");
        }
        public bool TryRead(out TelemetryFrame frame, out string status)
        {
            frame = null; LastReadTransient = false; status = connectionStatus;
            if (disposed) { status = "EA WRC 연결 종료"; return false; }
            DateTime now = DateTime.UtcNow;
            if (receiver == null)
            {
                if (now < nextConnect) return false;
                nextConnect = now.AddSeconds(1);
                if (!Connect(out status)) { connectionStatus = status; return false; }
            }
            try
            {
                bool malformed = false;
                // Bound work per UI poll; retain the newest accepted packet.
                for (int i = 0; i < 64 && receiver.Available > 0; i++)
                {
                    IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = receiver.Receive(ref sender);
                    if (!IPAddress.IsLoopback(sender.Address)) continue;
                    TelemetryFrame candidate; ulong packetId, frameCount;
                    if (!TryDecode(data, out candidate, out packetId, out frameCount, out status)) { malformed = true; continue; }
                    // A restart resets boot time and counters together. Ordinary
                    // UDP duplicates and out-of-order datagrams never rewind time.
                    bool restart = hasPrevious && candidate.Time + 1 < previousTime && packetId < previousPacket && frameCount < previousFrame && (now - lastGood).TotalSeconds > 1.5;
                    if (hasPrevious && !restart && (packetId <= previousPacket || frameCount <= previousFrame || candidate.Time <= previousTime)) continue;
                    previousPacket = packetId; previousFrame = frameCount; previousTime = candidate.Time; hasPrevious = true;
                    frame = candidate;
                }
                if (frame != null)
                {
                    frame.CaptureUtc = now; lastGood = now;
                    status = "EA WRC 수신 · 보조장치 적용 후 입력 / 스테이지·차종 ID 미제공";
                    return true;
                }
                DateTime since = lastGood > connected ? lastGood : connected;
                LastReadTransient = !malformed && (now - since).TotalSeconds < 1.5;
                status = malformed ? "EA WRC 패킷 불일치 · standard wrc 형식만 지원" : LastReadTransient ? "EA WRC 새 UDP 프레임 대기" : "EA WRC 데이터 대기 · 주행 / UDP 설정 / 다른 앱의 포트 사용 확인";
                if (!LastReadTransient && (now - since).TotalSeconds > 2) { Close(); nextConnect = now.AddSeconds(1); connectionStatus = status; }
                return false;
            }
            catch (SocketException ex) { Close(); status = "EA WRC UDP 연결 확인 · " + ex.SocketErrorCode; connectionStatus = status; return false; }
            catch (ObjectDisposedException) { status = "EA WRC UDP 연결 종료"; return false; }
        }
        bool Connect(out string status)
        {
            if (!VerifySchema(telemetryDirectory, out status) || !VerifyConfiguration(telemetryDirectory, port, out status)) return false;
            try
            {
                receiver = new UdpClient(AddressFamily.InterNetwork);
                receiver.Client.ExclusiveAddressUse = true;
                receiver.Client.ReceiveBufferSize = 65536;
                receiver.Client.Bind(new IPEndPoint(IPAddress.Loopback, port));
                connected = DateTime.UtcNow; status = "EA WRC UDP 수신 대기"; return true;
            }
            catch (SocketException ex) { Close(); status = "EA WRC 포트 " + port + " 사용 불가 · " + ex.SocketErrorCode; return false; }
        }
        internal static bool TryDecode(byte[] data, out TelemetryFrame frame, out ulong packetId, out ulong frameCount, out string status)
        {
            frame = null; packetId = frameCount = 0; status = "EA WRC 표준 UDP 형식 검증 실패";
            if (!BitConverter.IsLittleEndian || data == null || data.Length != PacketBytes) return false;
            double time = F32(data, 8), delta = F32(data, 12), speed = F32(data, 41) * 3.6;
            double throttle = F32(data, 197), brake = F32(data, 201), clutch = F32(data, 205), steer = F32(data, 209), handbrake = F32(data, 213);
            double stageTime = F32(data, 217), distance = F64(data, 221), length = F64(data, 229);
            int gearIndex = data[37], neutral = data[38], reverse = data[39], maxGear = data[40];
            if (!Range(time, 0, 1e8) || !Range(delta, 0, 1) || !Range(speed, 0, 400) ||
                !Range(throttle, 0, 1) || !Range(brake, 0, 1) || !Range(clutch, 0, 1) || !Range(steer, -1, 1) || !Range(handbrake, 0, 1) ||
                !Range(stageTime, 0, 86400) || !Range(length, 100, 100000) || !Range(distance, -100, length + 1000) ||
                data[36] > 1 || maxGear < 1 || maxGear > 12 || neutral == reverse || (gearIndex != neutral && gearIndex != reverse && (gearIndex < 1 || gearIndex > maxGear))) return false;
            int gear = gearIndex == neutral ? 0 : gearIndex == reverse ? -1 : gearIndex;
            packetId = BitConverter.ToUInt64(data, 0); frameCount = BitConverter.ToUInt64(data, 16);
            frame = new TelemetryFrame { Time = time, StageElapsedSeconds = stageTime, DistanceAgeSeconds = 0, Lap = 0, LapDistance = distance, TrackLength = length,
                SpeedKph = speed, Throttle = throttle, Brake = brake, Clutch = clutch, Steering = steer, Handbrake = handbrake, Gear = gear,
                X = Optional(F32(data, 49), -1e7, 1e7), Y = Optional(F32(data, 53), -1e7, 1e7), Z = Optional(F32(data, 57), -1e7, 1e7),
                Track = "EA WRC 스테이지 (ID 미제공)", Car = "EA WRC 차량 (ID 미제공)", GameVersion = "EA-WRC-wrc-schema1-data3",
                OnTrack = stageTime > 0 && distance >= 0 && distance <= length, ValidLapKnown = false, IsValidLap = false };
            // Official directions are world-space unit vectors. Project world
            // motion onto the supplied axes; never assume world X is car-local X.
            double lx = F32(data, 85), ly = F32(data, 89), lz = F32(data, 93);
            double fx = F32(data, 97), fy = F32(data, 101), fz = F32(data, 105);
            if (Unit(lx, ly, lz) && Unit(fx, fy, fz) && Math.Abs(lx * fx + ly * fy + lz * fz) < .05)
            {
                double vx = F32(data, 61), vy = F32(data, 65), vz = F32(data, 69);
                double ax = F32(data, 73), ay = F32(data, 77), az = F32(data, 81);
                if (Range(vx, -200, 200) && Range(vy, -200, 200) && Range(vz, -200, 200))
                { frame.LocalVelocityRight = -(vx * lx + vy * ly + vz * lz); frame.LocalVelocityForward = vx * fx + vy * fy + vz * fz; }
                if (Range(ax, -1000, 1000) && Range(ay, -1000, 1000) && Range(az, -1000, 1000))
                { frame.LateralG = -(ax * lx + ay * ly + az * lz) / 9.80665; frame.LongitudinalG = (ax * fx + ay * fy + az * fz) / 9.80665; }
            }
            // No replay, human-control, valid-stage, car or route identity is in
            // this standard packet. Never qualify it for circuit-lap baselines.
            status = "EA WRC 표준 UDP 데이터"; return true;
        }
        internal static bool VerifySchema(string directory, out string status)
        {
            string[] names = { "channels.json", Path.Combine("udp", "wrc.json") };
            string[] hashes = { "1738C8E6F1E4FDA698A7BA6A077CA31915D9EC179C0ED20EAF80A3E92FACE86C", "624193B1B4257F02E5285C6568849A67391D2C449074F864E1815FCD01128F2E" };
            try
            {
                for (int i = 0; i < names.Length; i++) using (var sha = SHA256.Create()) using (var file = File.OpenRead(Path.Combine(directory, "readme", names[i])))
                    if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "") != hashes[i])
                    { status = "지원하지 않는 EA WRC UDP 스키마 · 업데이트 형식 검증 필요"; return false; }
                status = "EA WRC 생성 스키마 확인됨"; return true;
            }
            catch (IOException) { status = "EA WRC telemetry/readme 없음 · 게임을 한 번 실행한 뒤 종료하세요."; return false; }
            catch (UnauthorizedAccessException) { status = "EA WRC 스키마 파일 접근 권한 확인 필요"; return false; }
        }
        internal static bool VerifyConfiguration(string directory, int port, out string status)
        {
            status = "EA WRC config.json: wrc/session_update, 127.0.0.1:" + port + ", bEnabled:true 설정 후 게임 재시작 필요";
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(directory, "config.json"))) as Dictionary<string, object>;
                object value; if (root == null || !root.TryGetValue("schema", out value) || Convert.ToInt32(value) != 2) return false;
                if (!root.TryGetValue("udp", out value)) return false;
                var udp = value as Dictionary<string, object>; if (udp == null || !udp.TryGetValue("packets", out value)) return false;
                var packets = value as object[]; if (packets == null) return false;
                foreach (object item in packets)
                {
                    var p = item as Dictionary<string, object>;
                    if (p == null || Get(p, "structure") != "wrc" || Get(p, "packet") != "session_update" || Get(p, "ip") != "127.0.0.1" || Get(p, "port") != port.ToString(CultureInfo.InvariantCulture) || Get(p, "bEnabled") != "True") continue;
                    int frequency; if (!int.TryParse(Get(p, "frequencyHz"), out frequency) || (frequency != -1 && frequency < 1)) continue;
                    status = "EA WRC localhost UDP 설정 확인됨"; return true;
                }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { } catch (InvalidCastException) { } catch (FormatException) { } catch (OverflowException) { }
            return false;
        }
        static string Get(Dictionary<string, object> values, string key) { object v; return values.TryGetValue(key, out v) ? Convert.ToString(v, CultureInfo.InvariantCulture) : ""; }
        static double F32(byte[] b, int o) { return BitConverter.ToSingle(b, o); }
        static double F64(byte[] b, int o) { return BitConverter.ToDouble(b, o); }
        static bool Range(double v, double min, double max) { return !double.IsNaN(v) && !double.IsInfinity(v) && v >= min && v <= max; }
        static double Optional(double v, double min, double max) { return Range(v, min, max) ? v : double.NaN; }
        static bool Unit(double x, double y, double z) { return Range(x, -1.01, 1.01) && Range(y, -1.01, 1.01) && Range(z, -1.01, 1.01) && Math.Abs(x * x + y * y + z * z - 1) < .05; }
        void Close() { if (receiver != null) { receiver.Close(); receiver = null; } }
        public void Dispose() { if (disposed) return; disposed = true; Close(); }
    }
}
