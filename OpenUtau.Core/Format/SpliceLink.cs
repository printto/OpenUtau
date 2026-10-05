using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Format {
    public static class SpliceLink {
        const string DefaultSpliceUrl = "https://printmov.com/web-video/";

        public static string SpliceUrl {
            get {
                string? from = Environment.GetEnvironmentVariable("PRINTMOV_SPLICE_URL");
                if (string.IsNullOrWhiteSpace(from)) {
                    return DefaultSpliceUrl;
                }
                from = from.Trim();
                return from.EndsWith('/') ? from : from + "/";
            }
        }

        static readonly string[] AllowedOrigins = {
            "https://printmov.com",
            "https://www.printmov.com",
        };

        static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

        static Handoff? current;

        public static async Task<Handoff> StartAsync(UProject project) {
            if (Pmws.VoiceParts(project).Count == 0) {
                throw new FileFormatException("No voice parts with notes to send.");
            }

            string name = SafeName(project.name);
            var folder = NewFolder();
            string ustxPath = Path.Combine(folder.FullName, name + ".ustx");
            string ustxText = SerializeUstx(project, ustxPath);
            File.WriteAllText(ustxPath, ustxText, new UTF8Encoding(false));

            string voices = Path.Combine(folder.FullName, $"{name} (voices).wav");
            await PlaybackManager.Inst.RenderMixdownTo(project, voices, includeWaveParts: false);

            return Serve(name, folder.FullName, ustxText, voices, AudioParts(project));
        }

        public static Handoff Serve(
                string name, string folder, string ustxText,
                string? voicesPath, IEnumerable<AudioPart> audio) {
            var handoff = new Handoff(name, folder, ustxText);
            if (voicesPath != null) {
                handoff.AddVoices(voicesPath);
            }
            foreach (var part in audio) {
                handoff.AddAudio(part);
            }
            handoff.Start();
            Interlocked.Exchange(ref current, handoff)?.Dispose();
            return handoff;
        }

        static string SerializeUstx(UProject project, string asFilePath) {
            string filePath = project.FilePath;
            try {
                project.ustxVersion = Ustx.kUstxVersion;
                project.FilePath = asFilePath;
                project.BeforeSave();
                return Yaml.DefaultSerializer.Serialize(project);
            } finally {
                project.AfterSave();
                project.FilePath = filePath;
            }
        }

        static IEnumerable<AudioPart> AudioParts(UProject project) {
            foreach (var part in project.parts.OfType<UWavePart>()) {
                if (string.IsNullOrEmpty(part.FilePath) || !File.Exists(part.FilePath)) {
                    Log.Warning($"Splice hand-off skipped a missing audio part: {part.name}");
                    continue;
                }
                var track = project.tracks.ElementAtOrDefault(part.trackNo);
                yield return new AudioPart {
                    Path = part.FilePath,
                    Name = Path.GetFileName(part.FilePath),
                    OffsetSeconds =
                        project.timeAxis.TickPosToMsPos(part.position - part.skip) / 1000.0,
                    Volume = track == null || track.Muted
                        ? 0 : PlaybackManager.DecibelToVolume(track.Volume),
                };
            }
        }

        public class AudioPart {
            public string Path = string.Empty;
            public string Name = string.Empty;
            public double OffsetSeconds;
            public double Volume = 1;
        }

        public class Handoff : IDisposable {
            public string Folder { get; }

            public string Url => $"{SpliceUrl}#handoff=local-{port}-{token}";

            readonly string name;
            readonly int port;
            readonly string token;
            readonly HttpListener listener = new HttpListener();
            readonly Dictionary<string, string> files = new Dictionary<string, string>();
            readonly List<JObject> audio = new List<JObject>();
            readonly CancellationTokenSource stopping = new CancellationTokenSource();
            readonly string projectText;
            JObject? voices;
            int disposed;

            internal Handoff(string name, string folder, string projectText) {
                this.name = name;
                this.projectText = projectText;
                Folder = folder;
                port = FreePort();
                token = Token();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            }

            internal void AddVoices(string path) {
                string key = "a/voices.wav";
                files[key] = path;
                voices = new JObject {
                    ["path"] = key,
                    ["name"] = $"{name} (voices).wav",
                };
            }

            internal void AddAudio(AudioPart part) {
                string key = $"a/{audio.Count}{Path.GetExtension(part.Name)}";
                files[key] = part.Path;
                audio.Add(new JObject {
                    ["path"] = key,
                    ["name"] = part.Name,
                    ["offset"] = part.OffsetSeconds,
                    ["volume"] = part.Volume,
                });
            }

            public void OpenInBrowser() {
                try {
                    Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
                } catch (Exception ex) {
                    Log.Warning(ex, $"Falling back to the default opener for {Url}");
                    OS.OpenWeb(Url);
                }
            }

            internal void Start() {
                listener.Start();
                Task.Run(Listen);
                Task.Delay(Lifetime, stopping.Token)
                    .ContinueWith(_ => Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
            }

            string Manifest() {
                var body = new JObject {
                    ["v"] = 1,
                    ["from"] = "openutau",
                    ["name"] = name,
                    ["projectFormat"] = "ustx",
                    ["project"] = projectText,
                    ["vocal"] = voices,
                    ["audioTracks"] = new JArray(audio),
                };
                return body.ToString(Formatting.None);
            }

            async Task Listen() {
                while (listener.IsListening) {
                    HttpListenerContext context;
                    try {
                        context = await listener.GetContextAsync();
                    } catch (Exception) {
                        return;
                    }
                    try {
                        Answer(context);
                    } catch (Exception ex) {
                        Log.Warning(ex, "Splice hand-off request failed.");
                        try { context.Response.Abort(); } catch { }
                    }
                }
            }

            void Answer(HttpListenerContext context) {
                var request = context.Request;
                var response = context.Response;
                string? origin = request.Headers["Origin"];
                if (Allowed(origin)) {
                    response.Headers["Access-Control-Allow-Origin"] = origin;
                    response.Headers["Access-Control-Allow-Private-Network"] = "true";
                }
                response.Headers["Cache-Control"] = "no-store";

                if (request.HttpMethod == "OPTIONS") {
                    response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
                    response.Headers["Access-Control-Allow-Headers"] = "*";
                    response.Headers["Access-Control-Max-Age"] = "600";
                    response.StatusCode = 204;
                    response.Close();
                    return;
                }
                if (request.HttpMethod != "GET") {
                    Empty(response, 405);
                    return;
                }

                string path = (request.Url?.AbsolutePath ?? "/").TrimStart('/');
                if (!path.StartsWith(token + "/", StringComparison.Ordinal)) {
                    Empty(response, 404);
                    return;
                }
                string rest = path.Substring(token.Length + 1);

                if (rest == "package.json") {
                    var bytes = Encoding.UTF8.GetBytes(Manifest());
                    response.ContentType = "application/json; charset=utf-8";
                    Send(response, bytes);
                    return;
                }
                if (rest == "done") {
                    Empty(response, 204);
                    Task.Delay(TimeSpan.FromSeconds(30), stopping.Token)
                        .ContinueWith(_ => Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
                    return;
                }
                if (files.TryGetValue(rest, out string? file) && File.Exists(file)) {
                    response.ContentType = MediaType(file);
                    using var stream = File.OpenRead(file);
                    response.ContentLength64 = stream.Length;
                    stream.CopyTo(response.OutputStream);
                    response.Close();
                    return;
                }
                Empty(response, 404);
            }

            static bool Allowed(string? origin) {
                if (string.IsNullOrEmpty(origin)) {
                    return false;
                }
                if (AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) {
                    return true;
                }
                return Uri.TryCreate(origin, UriKind.Absolute, out var url)
                    && url.Scheme == "http"
                    && (url.Host == "localhost" || url.Host == "127.0.0.1");
            }

            static void Send(HttpListenerResponse response, byte[] bytes) {
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes, 0, bytes.Length);
                response.Close();
            }

            static void Empty(HttpListenerResponse response, int status) {
                response.StatusCode = status;
                response.ContentLength64 = 0;
                response.Close();
            }

            public void Dispose() {
                if (Interlocked.Exchange(ref disposed, 1) != 0) {
                    return;
                }
                stopping.Cancel();
                try { listener.Close(); } catch { }
                stopping.Dispose();
            }
        }

        static readonly Dictionary<string, string> MediaTypes = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase) {
            [".wav"] = "audio/wav", [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4",
            [".aac"] = "audio/aac", [".ogg"] = "audio/ogg", [".opus"] = "audio/ogg",
            [".flac"] = "audio/flac", [".webm"] = "audio/webm", [".aiff"] = "audio/aiff",
            [".aif"] = "audio/aiff",
        };

        static string MediaType(string path) {
            return MediaTypes.TryGetValue(Path.GetExtension(path), out string? type)
                ? type : "application/octet-stream";
        }

        static int FreePort() {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            try {
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            } finally {
                probe.Stop();
            }
        }

        static string Token() {
            var bytes = new byte[16];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        static DirectoryInfo NewFolder() {
            string root = Path.Combine(PathManager.Inst.CachePath, "splice");
            CleanStale(root);
            var folder = new DirectoryInfo(
                Path.Combine(root, $"send-{DateTime.Now:yyyyMMdd-HHmmss}"));
            folder.Create();
            return folder;
        }

        static void CleanStale(string root) {
            try {
                if (!Directory.Exists(root)) {
                    return;
                }
                foreach (var folder in new DirectoryInfo(root).GetDirectories("send-*")) {
                    if (folder.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)) {
                        folder.Delete(true);
                    }
                }
            } catch (Exception ex) {
                Log.Warning(ex, "Failed to clean old PRINTmov Splice hand-offs.");
            }
        }

        static string SafeName(string? name) {
            string trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0) {
                trimmed = "Untitled";
            }
            foreach (char bad in Path.GetInvalidFileNameChars()) {
                trimmed = trimmed.Replace(bad, '_');
            }
            return trimmed.Length > 60 ? trimmed.Substring(0, 60) : trimmed;
        }
    }
}
