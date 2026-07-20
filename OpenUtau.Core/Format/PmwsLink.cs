using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Format {
    // Hands a project to PRINTmov Vocal, as a url fragment or an uploaded share code.
    public static class PmwsLink {
        public const string WebSynthUrl = "https://printmov.com/web-synth/";
        public const string ShareApiUrl = "https://printto-diff-svc-register.onrender.com";

        const int MaxShellUrl = 8000;

        public static string BuildUrl(UProject project) {
            return BuildUrl(project, null);
        }

        public static string BuildUrl(UProject project, UVoicePart? part) {
            string json = Pmws.SerializePart(project, part);
            return WebSynthUrl + "#p=" + Base64Url(Gzip(json));
        }

        public static async Task<ShareResult> ShareAsync(UProject project) {
            string payload = Base64Url(Gzip(Pmws.SerializeBundle(project)));
            var body = new JObject {
                ["payload"] = payload,
                ["name"] = project.name ?? string.Empty,
            };
            using var client = NewClient();
            using var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"{ShareApiUrl}/api/share", content);
            string text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) {
                throw new Exception(ErrorFrom(response.StatusCode, text));
            }
            var json = JObject.Parse(text);
            return new ShareResult {
                Code = json.Value<string>("code") ?? string.Empty,
                Url = json.Value<string>("url") ?? string.Empty,
                ExpiresInHours = json.Value<int?>("expiresInHours") ?? 24,
            };
        }

        public static async Task<UProject> OpenShareAsync(string code) {
            code = (code ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length == 0) {
                throw new Exception("Enter a share code.");
            }
            using var client = NewClient();
            using var response = await client.GetAsync(
                $"{ShareApiUrl}/api/share/{Uri.EscapeDataString(code)}");
            string text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) {
                throw new Exception(ErrorFrom(response.StatusCode, text));
            }
            var body = JObject.Parse(text);
            string payload = body.Value<string>("payload") ?? string.Empty;
            string name = body.Value<string>("name") ?? code;
            return Pmws.LoadJson(Gunzip(FromBase64Url(payload)), name);
        }

        public class ShareResult {
            public string Code = string.Empty;
            public string Url = string.Empty;
            public int ExpiresInHours = 24;
        }

        static HttpClient NewClient() {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "OpenUtau-PRINTmov");
            // Render free tier sleeps; cold start can take ~1 minute.
            client.Timeout = TimeSpan.FromSeconds(90);
            return client;
        }

        static string ErrorFrom(HttpStatusCode status, string body) {
            string detail = string.Empty;
            try {
                detail = JObject.Parse(body).Value<string>("error") ?? string.Empty;
            } catch { /* not json */ }
            switch (status) {
                case HttpStatusCode.NotFound: return "That share code was not found.";
                case HttpStatusCode.Gone: return "That share code has expired.";
                case (HttpStatusCode)429: return "Too many attempts. Wait a minute and try again.";
                case HttpStatusCode.RequestEntityTooLarge: return "This project is too large to share.";
                default:
                    return string.IsNullOrEmpty(detail)
                        ? $"The server returned {(int)status}." : detail;
            }
        }

        // A local redirect page when the url is too long for the shell, else the url.
        public static string BuildHandoff(string url) {
            if (url.Length <= MaxShellUrl) {
                return url;
            }
            string dir = Path.Combine(PathManager.Inst.CachePath, "websynth");
            Directory.CreateDirectory(dir);
            CleanStaleRedirects(dir);
            string path = Path.Combine(dir, $"send-{DateTime.Now:yyyyMMdd-HHmmss}.html");
            string html = "<!doctype html><meta charset=\"utf-8\"><title>PRINTmov Vocal</title>"
                + "<body style=\"font-family:sans-serif;padding:2rem\">Opening PRINTmov Vocal…"
                + "<script>location.replace(" + JsString(url) + ")</script></body>";
            File.WriteAllText(path, html, new UTF8Encoding(false));
            return path;
        }

        static void CleanStaleRedirects(string dir) {
            try {
                foreach (var file in Directory.GetFiles(dir, "send-*.html")) {
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-1)) {
                        File.Delete(file);
                    }
                }
            } catch (Exception ex) {
                Log.Warning(ex, "Failed to clean web synth redirect pages.");
            }
        }

        static string JsString(string value) {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("<", "\\u003c").Replace(">", "\\u003e") + "\"";
        }

        static byte[] Gzip(string text) {
            var bytes = Encoding.UTF8.GetBytes(text);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true)) {
                gzip.Write(bytes, 0, bytes.Length);
            }
            return output.ToArray();
        }

        static string Gunzip(byte[] data) {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        // base64url, unpadded.
        static string Base64Url(byte[] bytes) {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        static byte[] FromBase64Url(string value) {
            string b64 = value.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(b64.PadRight((b64.Length + 3) / 4 * 4, '='));
        }
    }
}
