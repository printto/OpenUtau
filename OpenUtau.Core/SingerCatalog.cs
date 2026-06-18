using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {
    public class CatalogSinger {
#pragma warning disable 0649
        public string id = string.Empty;
        public string name = string.Empty;
        public string author = string.Empty;
        public string category = string.Empty;
        public string type = string.Empty;
        public string version = string.Empty;
        public string image_url = string.Empty;
        public string description = string.Empty;
        public string download_url = string.Empty;
        public string page_url = string.Empty;
        public string encoding = string.Empty;
        public string sha256 = string.Empty;
        public long size;
#pragma warning restore 0649
    }

    public class InstalledSingerRecord {
        public string id = string.Empty;
        public string version = string.Empty;
        public string source = string.Empty;
    }

    public class SingerCatalog : SingletonBase<SingerCatalog> {
        public const string CatalogUrl = "https://www.printmov.com/json/singers.json";

        string StateFilePath => Path.Combine(PathManager.Inst.DataPath, "installed-singers.json");

        public async Task<List<CatalogSinger>> FetchCatalogAsync() {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "OpenUtau");
            client.Timeout = TimeSpan.FromSeconds(30);
            using var response = await client.GetAsync(CatalogUrl);
            response.EnsureSuccessStatusCode();
            string body = await response.Content.ReadAsStringAsync();

            var list = new List<CatalogSinger>();
            try {
                var token = JToken.Parse(body);
                var items = token.Type == JTokenType.Array ? token : token["singers"];
                if (items != null && items.Type == JTokenType.Array) {
                    list = items.ToObject<List<CatalogSinger>>() ?? new List<CatalogSinger>();
                }
            } catch (Exception e) {
                Log.Warning(e, "Failed to parse singers catalog JSON");
            }
            return list.Where(s => !string.IsNullOrEmpty(s.id)).ToList();
        }

        public async Task<string> DownloadToTempAsync(string url, IProgress<int>? progress, string? sha256) {
            string fileName = GetFileNameFromUrl(url);
            string cacheDir = PathManager.Inst.CachePath;
            Directory.CreateDirectory(cacheDir);
            string tempPath = Path.Combine(cacheDir, $"singer_{Guid.NewGuid():N}_{fileName}");

            using (var client = new HttpClient()) {
                client.DefaultRequestHeaders.Add("User-Agent", "OpenUtau");
                client.Timeout = TimeSpan.FromMinutes(10);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                var contentLength = response.Content.Headers.ContentLength;
                using var responseStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(tempPath);
                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                progress?.Report(0);
                while ((read = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;
                    if (contentLength.HasValue && progress != null) {
                        progress.Report((int)Math.Min(100, totalRead * 100 / contentLength.Value));
                    }
                }
                progress?.Report(100);
            }

            if (!string.IsNullOrEmpty(sha256)) {
                string expected = sha256.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? sha256.Substring("sha256:".Length) : sha256;
                string actual = GetSha256Hex(File.ReadAllBytes(tempPath));
                if (!string.Equals(expected.Trim(), actual, StringComparison.OrdinalIgnoreCase)) {
                    try { File.Delete(tempPath); } catch { }
                    throw new InvalidOperationException("Downloaded singer hash does not match expected value");
                }
            }
            return tempPath;
        }

        public async Task InstallDownloadedAsync(string filePath, string? type, string? encoding) {
            if (filePath.EndsWith(Vogen.VogenSingerInstaller.FileExt, StringComparison.OrdinalIgnoreCase)) {
                Vogen.VogenSingerInstaller.Install(filePath);
                return;
            }
            if (filePath.EndsWith(PackageManager.OudepExt, StringComparison.OrdinalIgnoreCase)) {
                await PackageManager.Inst.InstallFromFileAsync(filePath);
                return;
            }
            var enc = ResolveEncoding(encoding);
            var singerType = string.IsNullOrEmpty(type) ? "utau" : type;
            var basePath = PathManager.Inst.SingersInstallPath;
            await Task.Run(() => {
                var installer = new global::OpenUtau.Classic.VoicebankInstaller(basePath, (p, info) => {
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(p, info));
                }, enc, enc);
                installer.Install(filePath, singerType);
            });
            DocManager.Inst.ExecuteCmd(new SingersChangedNotification());
        }

        public static bool NeedsWizard(CatalogSinger singer, string downloadUrl) {
            if (downloadUrl.EndsWith(Vogen.VogenSingerInstaller.FileExt, StringComparison.OrdinalIgnoreCase) ||
                downloadUrl.EndsWith(PackageManager.OudepExt, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            return string.IsNullOrEmpty(singer.type) || string.IsNullOrEmpty(singer.encoding);
        }

        static Encoding ResolveEncoding(string? encoding) {
            if (!string.IsNullOrEmpty(encoding)) {
                try {
                    return Encoding.GetEncoding(encoding);
                } catch (Exception e) {
                    Log.Warning(e, $"Unknown encoding '{encoding}', falling back to shift_jis");
                }
            }
            return Encoding.GetEncoding("shift_jis");
        }

        static string GetFileNameFromUrl(string url) {
            try {
                var name = Path.GetFileName(new Uri(url).LocalPath);
                return string.IsNullOrEmpty(name) ? "singer.zip" : name;
            } catch {
                return "singer.zip";
            }
        }

        static string GetSha256Hex(byte[] data) {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }

        public Dictionary<string, InstalledSingerRecord> GetInstalledRecords() {
            try {
                if (File.Exists(StateFilePath)) {
                    var list = JsonConvert.DeserializeObject<List<InstalledSingerRecord>>(
                        File.ReadAllText(StateFilePath, Encoding.UTF8));
                    if (list != null) {
                        return list.Where(r => !string.IsNullOrEmpty(r.id))
                            .GroupBy(r => r.id).Select(g => g.Last())
                            .ToDictionary(r => r.id, r => r);
                    }
                }
            } catch (Exception e) {
                Log.Warning(e, "Failed to read installed-singers.json");
            }
            return new Dictionary<string, InstalledSingerRecord>();
        }

        public void SetInstalled(string id, string version, string source) {
            if (string.IsNullOrEmpty(id)) {
                return;
            }
            var records = GetInstalledRecords();
            records[id] = new InstalledSingerRecord { id = id, version = version, source = source };
            SaveRecords(records);
        }

        public void RemoveInstalled(string id) {
            var records = GetInstalledRecords();
            if (records.Remove(id)) {
                SaveRecords(records);
            }
        }

        void SaveRecords(Dictionary<string, InstalledSingerRecord> records) {
            try {
                File.WriteAllText(StateFilePath,
                    JsonConvert.SerializeObject(records.Values.ToList(), Formatting.Indented),
                    Encoding.UTF8);
            } catch (Exception e) {
                Log.Error(e, "Failed to save installed-singers.json");
            }
        }
    }
}
