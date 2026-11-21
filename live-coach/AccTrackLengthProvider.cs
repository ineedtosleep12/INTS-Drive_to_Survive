using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    // Only reads the local game's official Broadcasting v4 TrackData. It never
    // changes cameras, replays, game settings or the user's broadcasting.json.
    internal sealed class AccTrackLengthProvider : IDisposable
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly AccTrackDataState state = new AccTrackDataState();
        private UdpClient client;
        private double nextConnect, connectedAt, nextRequest;
        private string expectedTrack = "";
        private bool disposed;
        internal string Status { get; private set; }
        internal AccTrackLengthProvider() { Status = "ACC Broadcasting 코스 길이 대기"; }

        internal double GetLength(string track)
        {
            if (disposed || String.IsNullOrWhiteSpace(track)) return Double.NaN;
            double now = clock.Elapsed.TotalSeconds;
            if (!String.Equals(expectedTrack, track, StringComparison.OrdinalIgnoreCase))
            {
                expectedTrack = track;
                state.ForgetTrack();
                nextRequest = 0;
            }
            try
            {
                if (client == null)
                {
                    if (now < nextConnect) return Double.NaN;
                    nextConnect = now + 5;
                    int port; string password;
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Assetto Corsa Competizione", "Config", "broadcasting.json");
                    if (!TryReadConfiguration(path, out port, out password))
                    { Status = "ACC 입력 연결 · broadcasting.json의 UDP 포트 설정 필요"; return Double.NaN; }
                    client = new UdpClient(AddressFamily.InterNetwork);
                    client.Connect(IPAddress.Loopback, port);
                    client.Client.Blocking = false;
                    Send(Registration(password));
                    state.Reset(); connectedAt = now; nextRequest = now;
                    Status = "ACC 입력 연결 · Broadcasting 등록 응답 대기";
                }
                // The connected UDP socket only accepts this localhost endpoint.
                // Bounded draining keeps a noisy feed from blocking physics reads.
                for (int i = 0; i < 64 && client.Available > 0; ++i)
                {
                    IPEndPoint sender = null;
                    byte[] packet = client.Receive(ref sender);
                    if (packet.Length > 8192) continue;
                    bool wasRegistered = state.Registered;
                    state.Accept(packet, now);
                    if (!wasRegistered && state.Registered) nextRequest = 0;
                }
                if (!state.Registered && now - connectedAt > 3)
                { Status = "ACC 입력 연결 · Broadcasting 등록 실패/응답 없음, 설정 확인"; Reset(); return Double.NaN; }
                if (state.Registered && now >= nextRequest)
                {
                    Send(TrackRequest(state.ConnectionId));
                    nextRequest = now + 1;
                }
                double length = state.LengthFor(track, now);
                Status = Double.IsNaN(length) ? "ACC 입력 연결 · 현재 코스의 Broadcasting 길이 응답 대기" : "ACC 실시간 연결 · 공식 Broadcasting 코스 길이";
                if (state.Registered && now - Math.Max(connectedAt, state.LastTrackAt) > 8)
                { Reset(); return Double.NaN; }
                return length;
            }
            catch (SocketException) { Status = "ACC 입력 연결 · Broadcasting 연결 대기"; }
            catch (IOException) { Status = "ACC 입력 연결 · Broadcasting 설정 읽기 대기"; }
            catch (UnauthorizedAccessException) { Status = "ACC 입력 연결 · Broadcasting 설정 접근 불가"; }
            catch (ArgumentException) { Status = "ACC 입력 연결 · Broadcasting 설정 형식 확인"; }
            Reset(); return Double.NaN;
        }
        private void Send(byte[] bytes) { client.Send(bytes, bytes.Length); }
        internal void Reset()
        {
            if (client != null)
            {
                // Official disconnect is only the message byte; release our own
                // subscription without camera/replay/session control messages.
                if (state.Registered) try { Send(new byte[] { 9 }); } catch (SocketException) { } catch (ObjectDisposedException) { }
                client.Close(); client = null;
            }
            state.Reset(); nextRequest = 0;
        }
        public void Dispose() { if (disposed) return; Reset(); disposed = true; }

        internal static bool TryReadConfiguration(string path, out int port, out string password)
        {
            port = 0; password = "";
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return false;
            byte[] bytes = File.ReadAllBytes(path);
            string json;
            // ACC has shipped UTF-16LE config without a BOM as well as UTF-8.
            if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) json = new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) json = new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 2 && bytes[1] == 0) json = new UnicodeEncoding(false, false, true).GetString(bytes);
            else { int start = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0; json = new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start); }
            return TryConfigurationJson(json, out port, out password);
        }
        internal static bool TryConfigurationJson(string json, out int port, out string password)
        {
            port = 0; password = "";
            try
            {
                var data = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 8 }.DeserializeObject(json) as Dictionary<string, object>;
                object rawPort, rawPassword;
                if (data == null || !data.TryGetValue("updListenerPort", out rawPort) || !(rawPort is int) ||
                    !data.TryGetValue("connectionPassword", out rawPassword) || !(rawPassword is string)) return false;
                int configuredPort = (int)rawPort;
                string configuredPassword = (string)rawPassword;
                if (configuredPort < 1 || configuredPort > 65535 || Encoding.UTF8.GetByteCount(configuredPassword) > 1024) return false;
                port = configuredPort; password = configuredPassword; return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
        internal static byte[] Registration(string connectionPassword)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)1); writer.Write((byte)4);
                WriteString(writer, "INTS Live Coach (track metadata)"); WriteString(writer, connectionPassword);
                writer.Write(1000); WriteString(writer, ""); // No command password; no control commands exist in this client.
                return stream.ToArray();
            }
        }
        internal static byte[] TrackRequest(int connectionId)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            { writer.Write((byte)11); writer.Write(connectionId); return stream.ToArray(); }
        }
        private static void WriteString(BinaryWriter writer, string value)
        { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
    }

    // Independently testable parser. Data is accepted only after registration,
    // for that connection ID, as a complete v4 datagram and for the exact track.
    internal sealed class AccTrackDataState
    {
        internal bool Registered { get; private set; }
        internal int ConnectionId { get; private set; }
        internal double LastTrackAt { get; private set; }
        private string track = "";
        private double length = Double.NaN;
        internal AccTrackDataState() { Reset(); }
        internal void Reset() { Registered = false; ConnectionId = -1; ForgetTrack(); }
        internal void ForgetTrack() { track = ""; length = Double.NaN; LastTrackAt = Double.NegativeInfinity; }
        internal double LengthFor(string expectedTrack, double now)
        {
            return Registered && now >= LastTrackAt && now - LastTrackAt <= 3 && String.Equals(track, expectedTrack, StringComparison.OrdinalIgnoreCase)
                ? length : Double.NaN;
        }
        internal bool Accept(byte[] data, double now)
        {
            if (data == null || data.Length == 0 || data.Length > 8192 || Double.IsNaN(now) || Double.IsInfinity(now)) return false;
            try
            {
                using (var stream = new MemoryStream(data, false)) using (var reader = new BinaryReader(stream))
                {
                    byte kind = reader.ReadByte();
                    if (kind == 1)
                    {
                        int id = reader.ReadInt32(); byte success = reader.ReadByte(), writable = reader.ReadByte(); ReadString(reader, 2048);
                        if (stream.Position != stream.Length || success > 1 || writable > 1 || id < 0) return false;
                        if (Registered) return false; // A delayed duplicate must not switch the active connection.
                        if (success == 0) { Reset(); return true; }
                        ConnectionId = id; Registered = true; ForgetTrack(); return true;
                    }
                    if (kind != 5 || !Registered) return false;
                    if (reader.ReadInt32() != ConnectionId) return false;
                    string name = ReadString(reader, 128); int trackId = reader.ReadInt32(), metres = reader.ReadInt32();
                    if (String.IsNullOrWhiteSpace(name) || trackId < 0 || metres < 100 || metres > 100000) return false;
                    foreach (char c in name) if (Char.IsControl(c)) return false;
                    int sets = reader.ReadByte();
                    for (int i = 0; i < sets; ++i)
                    {
                        ReadString(reader, 512); int cameras = reader.ReadByte();
                        for (int c = 0; c < cameras; ++c) ReadString(reader, 512);
                    }
                    int pages = reader.ReadByte(); for (int i = 0; i < pages; ++i) ReadString(reader, 512);
                    if (stream.Position != stream.Length) return false;
                    track = name; length = metres; LastTrackAt = now; return true;
                }
            }
            catch (EndOfStreamException) { return false; }
            catch (DecoderFallbackException) { return false; }
            catch (InvalidDataException) { return false; }
        }
        private static string ReadString(BinaryReader reader, int limit)
        {
            int count = reader.ReadUInt16();
            if (count > limit || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException();
            return new UTF8Encoding(false, true).GetString(reader.ReadBytes(count));
        }
    }
}
