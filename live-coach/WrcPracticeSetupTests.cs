using System;
using System.IO;
using System.Linq;
using System.Text;

namespace IntsLiveCoach
{
    public static class WrcPracticeSetupTests
    {
        static int checks;
        const string Packet = "{\"structure\": \"wrc\", \"packet\": \"session_update\", \"ip\": \"127.0.0.1\", \"port\": 20777, \"frequencyHz\": -1, \"bEnabled\": false}";
        const string Other = "{\"structure\": \"wrc_simhub\", \"port\": 29888, \"bEnabled\": true, \"note\": \"keep {this} verbatim\"}";

        public static int Run()
        {
            checks = 0;
            string name = "ints-wrc-practice-test-" + Guid.NewGuid().ToString("N");
            // Use the checks binary's volume to exercise a real same-volume atomic replace.
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            try { Directory.CreateDirectory(folder); }
            catch (UnauthorizedAccessException)
            {
                folder = Path.Combine(Path.GetTempPath(), name);
                Directory.CreateDirectory(folder);
            }
            string path = Path.Combine(folder, "config.json");
            try
            {
                string original = Document(Packet);
                File.WriteAllText(path, original, new UTF8Encoding(true));
                byte[] originalBytes = File.ReadAllBytes(path);
                string message;
                Check(WrcPracticeSetup.TryEnable(folder, delegate { return false; }, out message), "enabled test fixture: " + message);
                string expected = Document(Packet.Replace("-1", "60").Replace("false", "true"));
                Check(File.ReadAllText(path) == expected, "only selected scalars changed");
                Check(File.ReadAllBytes(path).Take(3).SequenceEqual(originalBytes.Take(3)), "UTF-8 BOM survives");
                string[] backups = Directory.GetFiles(folder, "*.bak");
                Check(backups.Length == 1 && File.ReadAllBytes(backups[0]).SequenceEqual(originalBytes), "backup contains exact pre-edit bytes");
                Check(WrcReader.VerifyConfiguration(folder, 20777, out message), "existing receiver accepts saved stream");
                Check(WrcPracticeSetup.TryEnable(folder, delegate { return false; }, out message) &&
                      Directory.GetFiles(folder, "*.bak").Length == 1, "retry creates no backup and makes no edit");
                Reject(folder, path, Document(Packet + "," + Packet), false, "ambiguous streams");
                Reject(folder, path, Document(Packet.Replace("127.0.0.1", "192.0.2.1")), false, "existing destination reserved");
                Reject(folder, path, Document(Packet).Replace("29888", "20777"), false, "another stream uses port");
                Reject(folder, path, Document(Packet).Replace("\"schema\":2", "\"schema\":3"), false, "unsupported schema");
                Reject(folder, path, Document(Packet), true, "game running");
                Reject(folder, path, "{", false, "malformed JSON");
                Check(Directory.GetFiles(folder, "*.tmp").Length == 0, "no staging files left");
                return checks;
            }
            finally
            {
                foreach (string ownedFile in Directory.GetFiles(folder)) File.Delete(ownedFile);
                Directory.Delete(folder);
            }
        }

        static string Document(string stream)
        {
            return "{\r\n \"schema\":2, \"udp\":{\"packets\":[" + Other + "," + stream + "]}, \"lcd\":{\"state\":1.23}}";
        }

        static void Reject(string folder, string path, string json, bool gameRunning, string label)
        {
            File.WriteAllText(path, json);
            int before = Directory.GetFiles(folder, "*.bak").Length;
            string message;
            Check(!WrcPracticeSetup.TryEnable(folder, delegate { return gameRunning; }, out message) &&
                  File.ReadAllText(path) == json && Directory.GetFiles(folder, "*.bak").Length == before, label);
        }

        static void Check(bool ok, string message)
        {
            checks++;
            if (!ok) throw new Exception("WRC setup fixture: " + message);
        }
    }
}
