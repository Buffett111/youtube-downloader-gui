using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using SimpleDownloader;

// Uses official installed tools against an in-process loopback HTTP server.
// All source media is generated locally; there are no YouTube requests.
internal static class LocalMediaIntegration
{
    private static int assertions;

    private sealed class ProgressCollector : IProgress<DownloadUpdate>
    {
        public string ResultPath;

        public void Report(DownloadUpdate value)
        {
            if (!String.IsNullOrEmpty(value.FilePath)) ResultPath = value.FilePath;
            Console.WriteLine("  " + (value.Status ?? "") + " | " + (value.Detail ?? ""));
        }
    }

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            bool mp4Only = args.Length > 2 && args[2] == "--mp4-only";
            Run(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), mp4Only).GetAwaiter().GetResult();
            Console.WriteLine("PASS: " + (mp4Only ? "MP4 preview and download" : "six real format downloads") + "; " + assertions + " media assertions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.ToString());
            return 1;
        }
    }

    private static async Task Run(string toolsDirectory, string workDirectory, bool mp4Only)
    {
        Directory.CreateDirectory(workDirectory);
        string video = Path.Combine(workDirectory, "fixture.mp4");
        string audio = Path.Combine(workDirectory, "fixture.m4a");
        await Tool(toolsDirectory, "ffmpeg.exe", new[] {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc=size=320x180:rate=12",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100",
            "-t", "1.5", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac",
            "-shortest", "-movflags", "+faststart", video
        });
        await Tool(toolsDirectory, "ffmpeg.exe", new[] {
            "-hide_banner", "-loglevel", "error", "-y", "-i", video, "-vn", "-c:a", "copy", audio
        });
        Check(File.Exists(video) && new FileInfo(video).Length > 1000, "generated local H.264/AAC fixture");
        Check(File.Exists(audio) && new FileInfo(audio).Length > 1000, "generated local AAC fixture");

        using (var server = new FixtureServer(video, audio))
        {
            foreach (MediaFormat format in Enum.GetValues(typeof(MediaFormat)))
            {
                if (mp4Only && format != MediaFormat.Mp4) continue;
                string directory = Path.Combine(workDirectory, "輸出 100% 完成", format.ToString());
                string url = server.BaseUrl + (format == MediaFormat.OriginalAudio ? "fixture.m4a" : "fixture.mp4");
                var request = new DownloadRequest { Url = url, OutputDirectory = directory, Format = format, MaxHeight = 1080 };
                var progress = new ProgressCollector();
                Console.WriteLine("FORMAT " + format + " FROM " + url);
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    var service = new DownloadService(toolsDirectory);
                    DownloadPreview preview = await service.PreviewAsync(request, progress, cancellation.Token);
                    Check(preview.Count == 1 && preview.Items[0].Title == "fixture", format + " resolves local media title before confirmation");
                    Check(!Directory.Exists(directory), format + " metadata inspection creates no destination or media output");
                    request.Url = preview.Items[0].Url;
                    request.ExpectedId = preview.Items[0].Id;
                    await service.DownloadAsync(request, progress, cancellation.Token);
                }
                string result = progress.ResultPath;
                Check(!String.IsNullOrEmpty(result) && File.Exists(result), format + " reports an existing result");
                Check(Path.GetDirectoryName(Path.GetFullPath(result)) == Path.GetFullPath(directory), format + " preserves Unicode, spaces, and literal percent in output path");
                string extension = format == MediaFormat.OriginalAudio ? "m4a" : format.ToString().ToLowerInvariant();
                Check(Path.GetExtension(result) == "." + extension, format + " output extension is ." + extension);
                string json = await Tool(toolsDirectory, "ffprobe.exe", new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", result });
                File.WriteAllText(Path.Combine(directory, "probe.json"), json, new UTF8Encoding(false));
                var data = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
                var streams = ((IEnumerable)data["streams"]).Cast<Dictionary<string, object>>().ToArray();
                var videos = streams.Where(stream => Text(stream, "codec_type") == "video").ToArray();
                var audios = streams.Where(stream => Text(stream, "codec_type") == "audio").ToArray();
                Check(audios.Length == 1, format + " contains one audio stream");
                bool isVideo = format == MediaFormat.Mp4 || format == MediaFormat.Mkv;
                Check(videos.Length == (isVideo ? 1 : 0), format + (isVideo ? " retains video" : " contains no video"));
                if (isVideo)
                {
                    Check(Text(videos[0], "codec_name") == "h264", format + " video codec is H.264");
                    Check(Convert.ToInt32(videos[0]["height"], CultureInfo.InvariantCulture) <= 1080, format + " is within the resolution limit");
                }
                string codec = Text(audios[0], "codec_name");
                if (format == MediaFormat.Mp3) Check(codec == "mp3", "MP3 uses the MP3 codec");
                else if (format == MediaFormat.Wav) Check(codec.StartsWith("pcm_", StringComparison.Ordinal), "WAV uses PCM audio");
                else Check(codec == "aac", format + " retains AAC audio");
                var media = (Dictionary<string, object>)data["format"];
                double duration = Double.Parse(Text(media, "duration"), CultureInfo.InvariantCulture);
                Check(duration > 1 && duration < 2, format + " has the complete generated duration");
                if (format == MediaFormat.OriginalAudio)
                    Check(Hash(result) == Hash(audio), "original audio preserves source bytes without conversion");
            }
        }
    }

    private static async Task<string> Tool(string directory, string name, string[] arguments)
    {
        using (var process = new Process())
        {
            process.StartInfo = new ProcessStartInfo(Path.Combine(directory, name), String.Join(" ", arguments.Select(DownloadService.QuoteArgument)))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            if (!process.Start()) throw new IOException("Could not start " + name);
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> errors = process.StandardError.ReadToEndAsync();
            await Task.Run(delegate { process.WaitForExit(); });
            string result = await output;
            string error = await errors;
            if (process.ExitCode != 0) throw new IOException(name + " failed: " + error);
            return result;
        }
    }

    private static string Text(Dictionary<string, object> values, string key)
    {
        object value;
        return values.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
    }

    private static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(stream));
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        assertions++;
        Console.WriteLine("  OK " + description);
    }

    private sealed class FixtureServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly string video;
        private readonly string audio;
        private readonly Task acceptLoop;
        private volatile bool stopped;
        public readonly string BaseUrl;

        public FixtureServer(string video, string audio)
        {
            this.video = video;
            this.audio = audio;
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture) + "/";
            acceptLoop = Task.Run((Func<Task>)AcceptClients);
        }

        private async Task AcceptClients()
        {
            while (!stopped)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch (ObjectDisposedException) { if (stopped) return; throw; }
                catch (SocketException) { if (stopped) return; throw; }
                // Responses are tiny, so sequential handling keeps fixture lifetime deterministic.
                using (client) await Respond(client);
            }
        }

        private async Task Respond(TcpClient client)
        {
            using (NetworkStream stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
            {
                string first = await reader.ReadLineAsync();
                if (String.IsNullOrEmpty(first)) return;
                string[] request = first.Split(' ');
                string range = null;
                string line;
                while (!String.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                    if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = line.Substring(6).Trim();
                string path = request[1].Split('?')[0];
                string file = path == "/fixture.mp4" ? video : path == "/fixture.m4a" ? audio : null;
                if (file == null)
                {
                    byte[] missing = Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(missing, 0, missing.Length);
                    return;
                }
                byte[] content = File.ReadAllBytes(file);
                int start = 0, end = content.Length - 1;
                bool partial = false;
                if (!String.IsNullOrEmpty(range) && range.StartsWith("bytes=", StringComparison.Ordinal))
                {
                    string[] bounds = range.Substring(6).Split('-');
                    if (bounds.Length == 2 && Int32.TryParse(bounds[0], out start))
                    {
                        if (!String.IsNullOrEmpty(bounds[1])) Int32.TryParse(bounds[1], out end);
                        end = Math.Min(end, content.Length - 1);
                        partial = true;
                    }
                }
                if (start < 0 || end < start || start >= content.Length) throw new InvalidDataException("Invalid local fixture byte range.");
                var header = new StringBuilder(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n");
                header.Append("Content-Type: ").Append(file == audio ? "audio/mp4" : "video/mp4").Append("\r\n");
                header.Append("Content-Length: ").Append(end - start + 1).Append("\r\nAccept-Ranges: bytes\r\nConnection: close\r\n");
                if (partial) header.Append("Content-Range: bytes ").Append(start).Append('-').Append(end).Append('/').Append(content.Length).Append("\r\n");
                header.Append("\r\n");
                byte[] headers = Encoding.ASCII.GetBytes(header.ToString());
                Console.WriteLine("  HTTP " + first + (partial ? " " + range : ""));
                await stream.WriteAsync(headers, 0, headers.Length);
                if (request[0] != "HEAD") await stream.WriteAsync(content, start, end - start + 1);
            }
        }

        public void Dispose()
        {
            stopped = true;
            listener.Stop();
            acceptLoop.GetAwaiter().GetResult();
        }
    }
}
