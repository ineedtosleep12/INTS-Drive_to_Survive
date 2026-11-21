using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    internal static class WrcPracticeSetup
    {
        const string StandardStream = "wrc/session_update";
        static readonly Regex StructureTokens = new Regex(@"""(?:\\.|[^""\\])*""|[{}]", RegexOptions.CultureInvariant);
        static readonly Regex Enable = new Regex(@"(?<=[{,])(?<key>\s*""bEnabled""\s*:\s*)(?:false|true)(?=\s*[,}])");
        static readonly Regex Frequency = new Regex(@"(?<=[{,])(?<key>\s*""frequencyHz""\s*:\s*)-?\d+(?=\s*[,}])");

        internal static bool TryEnable(out string message)
        {
            string directory = Environment.GetEnvironmentVariable("WRC_TELEMETRY_DIR") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "WRC", "telemetry");
            return TryEnable(directory, GameRunning, out message);
        }

        static bool GameRunning()
        {
            foreach (string name in new[] { "WRC", "WRC-Win64-Shipping" })
            {
                var processes = Process.GetProcessesByName(name);
                foreach (var process in processes) process.Dispose();
                if (processes.Length != 0) return true;
            }
            return false;
        }

        internal static bool TryEnable(string directory, Func<bool> running, out string message)
        {
            try
            {
                // Two coach windows must not race while selecting the backup/target.
                using (var mutex = new Mutex(false, @"Local\INTS-WRC-Practice-Setup"))
                {
                    bool acquired = false;
                    try
                    {
                        try { acquired = mutex.WaitOne(0); }
                        catch (AbandonedMutexException) { acquired = true; }
                        if (!acquired) { message = "다른 코치 창에서 설정을 저장 중입니다."; return false; }
                        return EnableFile(directory, running, out message);
                    }
                    finally { if (acquired) mutex.ReleaseMutex(); }
                }
            }
            catch (IOException) { message = "설정 파일이 없거나 다른 프로그램에서 사용 중입니다. 게임 종료 후 WRC-SETUP.md를 확인하세요."; }
            catch (UnauthorizedAccessException) { message = "EA WRC 설정 폴더의 쓰기 권한을 확인하세요."; }
            catch (ArgumentException) { message = "지원하는 UTF-8 JSON 설정 파일인지 확인하세요."; }
            catch (InvalidOperationException) { message = "EA WRC 설정 형식 또는 실행 상태를 확인할 수 없습니다."; }
            catch (Win32Exception) { message = "EA WRC 실행 상태를 확인할 수 없습니다."; }
            return false;
        }

        static bool EnableFile(string directory, Func<bool> running, out string message)
        {
            message = "EA WRC를 종료한 뒤 데이터 전송을 켜세요.";
            if (running()) return false;
            string target = Path.Combine(directory, "config.json");
            using (var original = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (original.Length > 1024 * 1024) { message = "설정 파일 크기를 확인하세요."; return false; }
                byte[] bytes;
                using (var reader = new BinaryReader(original, Encoding.UTF8, true))
                    bytes = reader.ReadBytes((int)original.Length);
                bool bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
                var encoding = new UTF8Encoding(bom, true);
                int skip = bom ? 3 : 0;
                string source = encoding.GetString(bytes, skip, bytes.Length - skip);
                string updated;
                if (!Prepare(source, out updated, out message)) return false;
                if (updated == null) return true;
                string token = Guid.NewGuid().ToString("N");
                string temp = Path.Combine(directory, "ints-wrc-" + token + ".tmp");
                string backup = Path.Combine(directory, "config.ints-" + token + ".bak");
                try
                {
                    File.WriteAllText(temp, updated, encoding);
                    if (running()) { message = "EA WRC를 종료한 뒤 다시 시도하세요."; return false; }
                    if (!File.ReadAllBytes(target).SequenceEqual(bytes))
                    { message = "다른 앱에서 설정을 변경했습니다. 현재 설정을 확인한 뒤 다시 시도하세요."; return false; }
                    original.Close(); // ReplaceFile requires the original reader to release its handle.
                    File.Replace(temp, target, backup);
                    message = "EA WRC 전송을 켰습니다. 게임을 시작하고 스테이지에 진입하세요.\n원래 설정 백업: " + backup;
                    return true;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }

        static object Field(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value : null;
        }

        static bool Prepare(string json, out string updated, out string message)
        {
            updated = null;
            message = "표준 " + StandardStream + " 항목 하나가 필요합니다. WRC-SETUP.md를 확인하세요.";
            var serializer = new JavaScriptSerializer();
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            var udp = Field(root, "udp") as Dictionary<string, object>;
            var packets = Field(udp, "packets") as object[];
            if (!Equals(Field(root, "schema"), 2) || packets == null) return false;
            Dictionary<string, object> stream = null;
            foreach (object item in packets)
            {
                var packet = item as Dictionary<string, object>;
                if (Equals(Field(packet, "structure"), "wrc") && Equals(Field(packet, "packet"), "session_update"))
                {
                    if (stream != null) return false;
                    stream = packet;
                }
                else if (Equals(Field(packet, "bEnabled"), true) && Equals(Field(packet, "port"), 20777))
                {
                    message = "다른 활성 패킷이 20777 포트를 사용하고 있습니다. WRC-SETUP.md를 확인하세요.";
                    return false;
                }
            }
            if (stream == null) return false;
            if (!Equals(Field(stream, "ip"), "127.0.0.1") || !Equals(Field(stream, "port"), 20777))
            {
                message = "기존 표준 패킷의 수신 주소가 다릅니다. WRC-SETUP.md에서 연결 설정을 확인하세요.";
                return false;
            }
            object rate = Field(stream, "frequencyHz");
            object enabled = Field(stream, "bEnabled");
            if (!(rate is int) || (int)rate < -1 || !(enabled is bool)) return false;
            if ((bool)enabled && (int)rate != 0)
            {
                message = "이미 EA WRC 데이터 전송이 켜져 있습니다.";
                return true;
            }
            if ((bool)enabled) return false;

            // Patch just two scalar tokens in the game's generated flat packet.
            // Leave all other settings, whitespace, encoding and BOM untouched.
            string expectedStream = serializer.Serialize(stream);
            int start = -1, matchedAt = -1, matchedLength = 0;
            foreach (Match token in StructureTokens.Matches(json))
            {
                if (token.Value == "{") start = token.Index;
                else if (token.Value == "}" && start >= 0)
                {
                    int length = token.Index + 1 - start;
                    var item = serializer.DeserializeObject(json.Substring(start, length)) as Dictionary<string, object>;
                    if (item != null && serializer.Serialize(item) == expectedStream)
                    {
                        if (matchedAt >= 0) return false;
                        matchedAt = start;
                        matchedLength = length;
                    }
                    start = -1;
                }
            }
            if (matchedAt < 0) return false;
            string original = json.Substring(matchedAt, matchedLength);
            if (Enable.Matches(original).Count != 1 || Frequency.Matches(original).Count != 1) return false;
            string patch = Enable.Replace(original, "${key}true");
            patch = Frequency.Replace(patch, "${key}60");
            string candidate = json.Remove(matchedAt, matchedLength).Insert(matchedAt, patch);
            stream["bEnabled"] = true;
            stream["frequencyHz"] = 60;
            if (serializer.Serialize(serializer.DeserializeObject(candidate)) != serializer.Serialize(root)) return false;
            updated = candidate;
            return true;
        }
    }
}
