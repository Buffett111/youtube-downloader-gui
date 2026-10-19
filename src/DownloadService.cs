using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace SimpleDownloader
{
    public enum MediaFormat { Mp4, Mkv, Mp3, M4a, Wav, OriginalAudio }

    public sealed class DownloadRequest
    {
        public string Url;
        public string OutputDirectory;
        public MediaFormat Format;
        // Zero means the best available resolution; a positive value is a ceiling.
        public int MaxHeight;
        // Set after the user confirms a preview, so a changing URL cannot select another item.
        public string ExpectedId;
    }

    public sealed class PreviewItem
    {
        public string Title;
        public string Url;
        public string Id;
        public bool IsAvailable = true;
        public string UnavailableReason;

        public override string ToString() { return Title; }
    }

    public sealed class DownloadPreview
    {
        public List<PreviewItem> Items = new List<PreviewItem>();
        public bool IsPlaylist;
        public string PlaylistTitle;
        public string CurrentVideoId;
        public int Count { get { return Items.Count; } }
        public string Title { get { return Count > 0 ? Items[0].Title : null; } }
        public string Url { get { return Count > 0 ? Items[0].Url : null; } }
        public string Id { get { return Count > 0 ? Items[0].Id : null; } }
    }

    public sealed class DownloadUpdate
    {
        public string Status;
        public string Detail;
        public string FilePath;
        public double? Percent;
    }

    public sealed class DownloadService
    {
        private readonly string toolsDirectory;
        private const string ProgressPrefix = "__SD_PROGRESS__\t";
        private const string PostprocessPrefix = "__SD_POST__\t";
        private const string FilePrefix = "__SD_FILE__";
        private const int MaxPreviewJsonLength = 64 * 1024 * 1024;

        public DownloadService(string toolsDirectory)
        {
            if (String.IsNullOrWhiteSpace(toolsDirectory))
                throw new ArgumentException("工具資料夾不可空白。", "toolsDirectory");
            this.toolsDirectory = Path.GetFullPath(toolsDirectory);
        }

        public static string GetDownloadsDirectory()
        {
            IntPtr value = IntPtr.Zero;
            try
            {
                Guid downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
                if (Native.SHGetKnownFolderPath(ref downloads, 0x4000, IntPtr.Zero, out value) == 0)
                {
                    string result = Marshal.PtrToStringUni(value);
                    if (!String.IsNullOrWhiteSpace(result)) return result;
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            finally { if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value); }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        public static void ValidateRequest(DownloadRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            string url = (request.Url ?? String.Empty).Trim();
            if (url.Length == 0) throw new ArgumentException("請先貼上影片或音樂的網址。");
            foreach (char character in url)
            {
                if (Char.IsWhiteSpace(character) || Char.IsControl(character))
                    throw new ArgumentException("請輸入單一完整網址，網址中不可包含空白或換行。");
            }
            // Do not silently accept a second URL or control characters trimmed at the ends.
            if ((request.Url ?? String.Empty).IndexOfAny(new char[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("請輸入單一完整網址，網址中不可包含換行。");
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) ||
                String.IsNullOrEmpty(parsed.Host))
                throw new ArgumentException("請輸入以 https:// 或 http:// 開頭的完整網址。");
            if (String.IsNullOrWhiteSpace(request.OutputDirectory))
                throw new ArgumentException("請選擇儲存資料夾。");
            try { Path.GetFullPath(request.OutputDirectory); }
            catch (Exception error)
            {
                if (!(error is ArgumentException) && !(error is NotSupportedException) && !(error is PathTooLongException)) throw;
                throw new ArgumentException("儲存資料夾路徑無效。", error);
            }
            if (!Enum.IsDefined(typeof(MediaFormat), request.Format))
                throw new ArgumentException("請選擇有效的下載格式。");
            if (request.MaxHeight < 0 || request.MaxHeight > 16384)
                throw new ArgumentException("影片解析度設定無效。");
            if (request.ExpectedId != null)
            {
                if (String.IsNullOrWhiteSpace(request.ExpectedId))
                    throw new ArgumentException("已確認的影片識別碼無效，請重新讀取網址。");
                foreach (char character in request.ExpectedId)
                    if (Char.IsControl(character))
                        throw new ArgumentException("已確認的影片識別碼無效，請重新讀取網址。");
            }
        }

        public static List<string> BuildArguments(DownloadRequest request, string toolsDirectory)
        {
            ValidateRequest(request);
            List<string> arguments = new List<string> {
                "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--playlist-items", "1", "--windows-filenames",
                "--no-overwrites", "--no-post-overwrites", "--newline", "--progress", "--no-simulate",
                "--color", "never", "--encoding", "utf-8", "--progress-delta", "0.3",
                "--ffmpeg-location", Path.GetFullPath(toolsDirectory),
                "--paths", Path.GetFullPath(request.OutputDirectory),
                "--output", "%(title).160B [%(id)s].%(ext)s",
                "--progress-template", "download:" + ProgressPrefix + "%(progress.status)s\t%(progress.downloaded_bytes)s\t%(progress.total_bytes)s\t%(progress.total_bytes_estimate)s\t%(progress.speed)s\t%(progress.eta)s",
                "--progress-template", "postprocess:" + PostprocessPrefix + "%(progress.status)s",
                "--print", "after_move:" + FilePrefix + "%(filepath)j"
            };
            AddRuntimeArguments(arguments, toolsDirectory);
            AddFormatArguments(arguments, request);
            if (request.ExpectedId != null)
            {
                // yt-dlp's filter language handles quoted string equality, with an
                // escaped ampersand/quote kept inside the literal (not a condition).
                string expectedId = request.ExpectedId.Replace("&", "\\&").Replace("'", "\\'");
                arguments.AddRange(new string[] { "--match-filters", "id='" + expectedId + "'" });
            }
            arguments.Add("--");
            arguments.Add(request.Url.Trim());
            return arguments;
        }

        public static List<string> BuildPreviewArguments(DownloadRequest request, string toolsDirectory)
        {
            ValidateRequest(request);
            List<string> arguments = new List<string> {
                "--ignore-config", "--no-plugin-dirs", "--yes-playlist",
                "--dump-single-json", "--simulate", "--flat-playlist", "--no-progress", "--no-cache-dir",
                "--color", "never", "--encoding", "utf-8",
                "--ffmpeg-location", Path.GetFullPath(toolsDirectory)
            };
            AddRuntimeArguments(arguments, toolsDirectory);
            AddFormatArguments(arguments, request);
            arguments.Add("--");
            arguments.Add(request.Url.Trim());
            return arguments;
        }

        private static void AddRuntimeArguments(List<string> arguments, string toolsDirectory)
        {
            string deno = Path.Combine(Path.GetFullPath(toolsDirectory), "deno.exe");
            if (File.Exists(deno)) arguments.AddRange(new string[] { "--js-runtimes", "deno:" + deno });
        }

        private static void AddFormatArguments(List<string> arguments, DownloadRequest request)
        {
            string resolution = request.MaxHeight > 0
                ? "[height<=?" + request.MaxHeight.ToString(CultureInfo.InvariantCulture) + "]" : String.Empty;
            switch (request.Format)
            {
                case MediaFormat.Mp4:
                    // Prefer widely compatible H.264 + AAC. The fallback preserves yt-dlp's
                    // own selection behavior when that combination is unavailable.
                    arguments.AddRange(new string[] {
                        "--format", "bv[ext=mp4][vcodec^=avc1]" + resolution + "+ba[ext=m4a]/b[ext=mp4][vcodec^=avc1]" + resolution + "/bv*" + resolution + "+ba/b" + resolution,
                        "--format-sort", "vcodec:h264,acodec:aac",
                        "--merge-output-format", "mp4", "--recode-video", "mp4" });
                    break;
                case MediaFormat.Mkv:
                    arguments.AddRange(new string[] { "--format", "bv*" + resolution + "+ba/b" + resolution,
                        "--merge-output-format", "mkv", "--remux-video", "mkv" });
                    break;
                case MediaFormat.Mp3:
                case MediaFormat.M4a:
                case MediaFormat.Wav:
                    string audioFormat = request.Format.ToString().ToLowerInvariant();
                    arguments.AddRange(new string[] { "--format", request.Format == MediaFormat.M4a ? "ba[ext=m4a]/ba/b" : "ba/b",
                        "--extract-audio", "--audio-format", audioFormat, "--audio-quality", "0" });
                    break;
                case MediaFormat.OriginalAudio:
                    arguments.AddRange(new string[] { "--format", "ba" });
                    break;
            }
        }

        // Microsoft C runtime command-line quoting. The process is launched directly,
        // so characters such as &, %, and | never pass through a command shell.
        public static string QuoteArgument(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            StringBuilder result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in value)
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') result.Append('\\', slashes * 2 + 1);
                else result.Append('\\', slashes);
                result.Append(character);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }

        public async Task<DownloadPreview> PreviewAsync(DownloadRequest request,
            IProgress<DownloadUpdate> progress, CancellationToken token)
        {
            ValidateRequest(request);
            token.ThrowIfCancellationRequested();
            DownloadRequest snapshot = Snapshot(request);
            string executable = Path.Combine(toolsDirectory, "yt-dlp.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("尚未安裝 yt-dlp，請先按「安裝／更新工具」。", executable);

            // Keep machine-readable stdout separate from stderr. JSON may contain
            // signed media URLs and large format lists, so it is never logged.
            string metadata = null;
            int metadataLines = 0;
            bool tooLarge = false;
            object stateLock = new object();
            Queue<string> recentOutput = new Queue<string>();
            Action<string> onMetadata = delegate(string line)
            {
                if (String.IsNullOrWhiteSpace(line)) return;
                metadataLines++;
                if (line.Length > MaxPreviewJsonLength) tooLarge = true;
                else if (metadataLines == 1) metadata = line;
            };
            Action<string> onDiagnostic = delegate(string line)
            {
                if (String.IsNullOrWhiteSpace(line)) return;
                string detail = line.Length > 1000 ? line.Substring(0, 1000) + "…" : line;
                lock (stateLock)
                {
                    recentOutput.Enqueue(detail);
                    while (recentOutput.Count > 10) recentOutput.Dequeue();
                }
                Report(progress, new DownloadUpdate { Detail = detail });
            };
            Report(progress, new DownloadUpdate { Status = "正在確認下載內容…",
                Detail = "yt-dlp 正在讀取影片與清單標題；確認前不會下載影音檔案。" });
            int exitCode = await RunProcessAsync(executable,
                BuildCommand(BuildPreviewArguments(snapshot, toolsDirectory)), toolsDirectory,
                onMetadata, onDiagnostic, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (exitCode != 0)
            {
                string details;
                lock (stateLock) details = String.Join(Environment.NewLine, recentOutput.ToArray());
                throw new InvalidOperationException("無法確認下載內容（yt-dlp 結束代碼 " + exitCode + "）。" +
                    (String.IsNullOrWhiteSpace(details) ? String.Empty : Environment.NewLine + details));
            }
            if (tooLarge)
                throw new InvalidOperationException("此清單的影片資料過大，請改用較小的清單或單一影片網址。");
            if (metadataLines != 1 || metadata == null)
                throw new InvalidOperationException("無法取得完整的影片或清單資料，請檢查網址後再試一次。");
            return ParsePreview(metadata, snapshot.Url);
        }

        private static DownloadPreview ParsePreview(string metadata, string originalUrl)
        {
            Dictionary<string, object> root;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer {
                    MaxJsonLength = MaxPreviewJsonLength, RecursionLimit = 128 };
                root = serializer.DeserializeObject(metadata) as Dictionary<string, object>;
            }
            catch (ArgumentException error)
            {
                throw new InvalidOperationException("yt-dlp 回傳的影片資料無法辨識，請重新讀取網址。", error);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException("yt-dlp 回傳的影片資料無法辨識，請重新讀取網址。", error);
            }
            if (root == null)
                throw new InvalidOperationException("yt-dlp 未回傳有效的影片資料，請重新讀取網址。");
            string type = ReadString(root, "_type");
            bool isPlaylist = type == "playlist" || type == "multi_video" || root.ContainsKey("entries");
            DownloadPreview preview = new DownloadPreview { IsPlaylist = isPlaylist,
                PlaylistTitle = isPlaylist ? ReadString(root, "title") : null,
                CurrentVideoId = GetCurrentVideoId(originalUrl) };
            if (isPlaylist)
            {
                object entriesValue;
                object[] entries = root.TryGetValue("entries", out entriesValue) ? entriesValue as object[] : null;
                if (entries == null || entries.Length == 0)
                    throw new InvalidOperationException("此清單沒有可讀取的影片，請檢查網址或清單的存取權限。");
                for (int index = 0; index < entries.Length; index++)
                {
                    Dictionary<string, object> item = entries[index] as Dictionary<string, object>;
                    try { preview.Items.Add(ParsePreviewItem(item, null, true)); }
                    catch (InvalidOperationException error)
                    {
                        string title = item == null ? null : ReadString(item, "title");
                        preview.Items.Add(new PreviewItem { IsAvailable = false,
                            Title = String.IsNullOrWhiteSpace(title)
                                ? "第 " + (index + 1).ToString(CultureInfo.InvariantCulture) + " 項（無法讀取影片資訊）" : title.Trim(),
                            Id = item == null ? null : ReadString(item, "id"),
                            UnavailableReason = error.Message });
                    }
                }
            }
            else
            {
                PreviewItem item = ParsePreviewItem(root, originalUrl, false);
                if (!item.IsAvailable) throw new InvalidOperationException(item.UnavailableReason);
                preview.Items.Add(item);
            }
            return preview;
        }

        private static PreviewItem ParsePreviewItem(Dictionary<string, object> item, string originalUrl, bool isPlaylist)
        {
            if (item == null || item.ContainsKey("entries"))
                throw new InvalidOperationException("無法讀取這個項目的影片資訊，請改用該影片的直接網址。");
            string itemType = ReadString(item, "_type");
            if (itemType != null && itemType != "video" && itemType != "url" && itemType != "url_transparent")
                throw new InvalidOperationException("這個項目不是可辨識的影片，請改用該影片的直接網址。");
            string title = ReadString(item, "title");
            string id = ReadString(item, "id");
            if (String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("無法取得影片標題或識別碼，請檢查網址後再試一次。");
            foreach (char character in id)
                if (Char.IsControl(character))
                    throw new InvalidOperationException("影片識別碼無效，請重新讀取網址。");
            string resolvedUrl = ReadString(item, "webpage_url");
            if (String.IsNullOrWhiteSpace(resolvedUrl))
                resolvedUrl = isPlaylist ? ReadString(item, "url") : originalUrl;
            Uri parsed;
            if (String.IsNullOrWhiteSpace(resolvedUrl) ||
                !Uri.TryCreate(resolvedUrl, UriKind.Absolute, out parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
                String.IsNullOrEmpty(parsed.Host))
                throw new InvalidOperationException("無法取得這部影片的完整網址，請直接貼上該影片的網址。");
            foreach (char character in resolvedUrl)
                if (Char.IsControl(character) || Char.IsWhiteSpace(character))
                    throw new InvalidOperationException("影片網址無效，請直接貼上該影片的網址。");
            PreviewItem result = new PreviewItem { Title = title.Trim(), Id = id, Url = resolvedUrl };
            string availability = ReadString(item, "availability");
            if (availability == "private" || availability == "needs_auth" || availability == "premium_only" ||
                availability == "subscriber_only" || availability == "unavailable")
            {
                result.IsAvailable = false;
                result.UnavailableReason = "yt-dlp 回報此影片目前無法公開存取（" + availability + "）。";
            }
            return result;
        }

        // This is only a selection hint for the confirmation window. Titles, item
        // identities and download URLs always come from yt-dlp's metadata.
        private static string GetCurrentVideoId(string originalUrl)
        {
            Uri uri;
            if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out uri)) return null;
            string host = uri.Host.ToLowerInvariant();
            string[] path = uri.AbsolutePath.Trim('/').Split('/');
            if ((host == "youtu.be" || host == "www.youtu.be") && path.Length > 0 && path[0].Length > 0)
                return Uri.UnescapeDataString(path[0]);
            if (host != "youtube.com" && !host.EndsWith(".youtube.com", StringComparison.Ordinal)) return null;
            if (path.Length == 2 && (path[0] == "shorts" || path[0] == "live" || path[0] == "embed"))
                return Uri.UnescapeDataString(path[1]);
            foreach (string part in uri.Query.TrimStart('?').Split('&'))
            {
                int separator = part.IndexOf('=');
                if (separator > 0 && Uri.UnescapeDataString(part.Substring(0, separator)) == "v")
                    return Uri.UnescapeDataString(part.Substring(separator + 1).Replace("+", " "));
            }
            return null;
        }

        private static string ReadString(Dictionary<string, object> item, string key)
        {
            object value;
            return item.TryGetValue(key, out value) ? value as string : null;
        }

        private static DownloadRequest Snapshot(DownloadRequest request)
        {
            return new DownloadRequest { Url = request.Url.Trim(),
                OutputDirectory = Path.GetFullPath(request.OutputDirectory), Format = request.Format,
                MaxHeight = request.MaxHeight, ExpectedId = request.ExpectedId };
        }

        private static string BuildCommand(List<string> arguments)
        {
            StringBuilder command = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (command.Length > 0) command.Append(' ');
                command.Append(QuoteArgument(argument));
            }
            return command.ToString();
        }

        public async Task DownloadAsync(DownloadRequest request, IProgress<DownloadUpdate> progress, CancellationToken token)
        {
            ValidateRequest(request);
            token.ThrowIfCancellationRequested();
            // Freeze editable UI values before crossing an asynchronous boundary.
            DownloadRequest snapshot = Snapshot(request);
            string executable = Path.Combine(toolsDirectory, "yt-dlp.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("尚未安裝 yt-dlp，請先按「安裝／更新工具」。", executable);
            if (snapshot.Format != MediaFormat.OriginalAudio &&
                (!File.Exists(Path.Combine(toolsDirectory, "ffmpeg.exe")) || !File.Exists(Path.Combine(toolsDirectory, "ffprobe.exe"))))
                throw new FileNotFoundException("此格式需要 FFmpeg，請先按「安裝／更新工具」。");
            EnsureWritableDirectory(snapshot.OutputDirectory);
            string command = BuildCommand(BuildArguments(snapshot, toolsDirectory));
            object stateLock = new object();
            Queue<string> recentOutput = new Queue<string>();
            string finalPath = null;
            Action<string> onLine = delegate(string line)
            {
                if (String.IsNullOrWhiteSpace(line)) return;
                DownloadUpdate update;
                if (TryParseProgress(line, out update))
                {
                    Report(progress, update);
                    return;
                }
                if (line.StartsWith(FilePrefix, StringComparison.Ordinal))
                {
                    try
                    {
                        string path = new JavaScriptSerializer().Deserialize<string>(line.Substring(FilePrefix.Length));
                        if (!String.IsNullOrWhiteSpace(path))
                        {
                            lock (stateLock) finalPath = path;
                            Report(progress, new DownloadUpdate { Status = "完成處理", FilePath = path, Detail = path });
                        }
                    }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                    return;
                }
                lock (stateLock)
                {
                    recentOutput.Enqueue(line);
                    while (recentOutput.Count > 20) recentOutput.Dequeue();
                }
                Report(progress, new DownloadUpdate { Detail = line });
            };
            Report(progress, new DownloadUpdate { Status = "正在讀取網址…", Detail = "yt-dlp 正在準備下載。" });
            int exitCode = await RunProcessAsync(executable, command, toolsDirectory, onLine, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (exitCode != 0)
            {
                string details;
                lock (stateLock) details = String.Join(Environment.NewLine, recentOutput.ToArray());
                throw new InvalidOperationException("yt-dlp 未能完成下載（結束代碼 " + exitCode + "）。" +
                    (String.IsNullOrWhiteSpace(details) ? String.Empty : Environment.NewLine + details));
            }
            string completedPath;
            lock (stateLock) completedPath = finalPath;
            if (String.IsNullOrWhiteSpace(completedPath) || !File.Exists(completedPath))
                throw new InvalidOperationException(snapshot.ExpectedId == null
                    ? "yt-dlp 已結束，但沒有回報可確認的輸出檔案。請查看詳細紀錄。"
                    : "沒有取得已確認影片的輸出檔案；影片可能已變更或無法下載。請重新讀取網址，並查看詳細紀錄。");
            Report(progress, new DownloadUpdate { Status = "下載完成", Detail = completedPath, FilePath = completedPath, Percent = 100 });
        }

        private static void EnsureWritableDirectory(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".simple-downloader-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    1, FileOptions.DeleteOnClose)) { stream.WriteByte(0); }
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is System.Security.SecurityException)) throw;
                throw new IOException("無法寫入儲存資料夾，請選擇其他位置：" + directory, error);
            }
        }

        private static bool TryParseProgress(string line, out DownloadUpdate update)
        {
            update = null;
            if (line.StartsWith(PostprocessPrefix, StringComparison.Ordinal))
            {
                update = new DownloadUpdate { Status = "正在合併或轉換格式…", Detail = "FFmpeg 正在處理檔案。" };
                return true;
            }
            if (!line.StartsWith(ProgressPrefix, StringComparison.Ordinal)) return false;
            string[] fields = line.Substring(ProgressPrefix.Length).Split('\t');
            if (fields.Length < 6) return true;
            double downloaded, total, estimated, speed, eta;
            bool hasDownloaded = Double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out downloaded);
            Double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out total);
            Double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out estimated);
            Double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out speed);
            Double.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out eta);
            if (total <= 0) total = estimated;
            bool finished = fields[0] == "finished";
            double? percentage = hasDownloaded && total > 0 ? (double?)Math.Max(0, Math.Min(100, 100 * downloaded / total)) : null;
            StringBuilder details = new StringBuilder();
            if (hasDownloaded) details.Append(FormatBytes(downloaded));
            if (total > 0) details.Append(" / ").Append(FormatBytes(total));
            if (speed > 0) details.Append(" · ").Append(FormatBytes(speed)).Append("/s");
            if (eta > 0 && eta < TimeSpan.MaxValue.TotalSeconds)
                details.Append(" · 剩餘 ").Append(TimeSpan.FromSeconds(eta).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
            update = new DownloadUpdate { Status = finished ? "串流下載完成，正在處理…" : "正在下載…",
                Detail = details.ToString(), Percent = percentage };
            return true;
        }

        private static string FormatBytes(double bytes)
        {
            if (bytes >= 1073741824) return (bytes / 1073741824).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1048576) return (bytes / 1048576).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return (bytes / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        }

        private static void Report(IProgress<DownloadUpdate> progress, DownloadUpdate update)
        {
            if (progress != null && update != null) progress.Report(update);
        }

        private static async Task ReadLinesAsync(StreamReader reader, Action<string> onLine)
        {
            string line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null) onLine(line);
        }

        private static async Task<int> RunProcessAsync(string executable, string arguments, string workingDirectory,
            Action<string> onLine, CancellationToken token)
        {
            return await RunProcessAsync(executable, arguments, workingDirectory, onLine, onLine, token).ConfigureAwait(false);
        }

        private static async Task<int> RunProcessAsync(string executable, string arguments, string workingDirectory,
            Action<string> onOutput, Action<string> onError, CancellationToken token)
        {
            // Start suspended, assign the job, then resume. A fast yt-dlp cannot create
            // untracked FFmpeg/Deno children in the gap between Start and assignment.
            using (NativeProcess child = NativeProcess.Start(executable, arguments, workingDirectory))
            using (CancellationTokenRegistration registration = token.Register(child.Cancel))
            {
                token.ThrowIfCancellationRequested();
                try { child.Resume(); }
                catch (Win32Exception) { token.ThrowIfCancellationRequested(); throw; }
                Task stdout = ReadLinesAsync(child.StandardOutput, onOutput);
                Task stderr = ReadLinesAsync(child.StandardError, onError);
                int code = await Task.Run<int>(delegate { return child.WaitForExit(); }).ConfigureAwait(false);
                // EOF is reached after all inherited writer handles close. Kill-on-close
                // also covers a child left behind by an abnormal downloader exit.
                child.CloseJob();
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return code;
            }
        }

        private sealed class NativeProcess : IDisposable
        {
            private readonly object gate = new object();
            private SafeKernelHandle process;
            private SafeKernelHandle thread;
            private SafeKernelHandle job;
            public StreamReader StandardOutput;
            public StreamReader StandardError;

            public static NativeProcess Start(string executable, string arguments, string workingDirectory)
            {
                NativeProcess child = new NativeProcess();
                SafeFileHandle outRead = null, outWrite = null, errRead = null, errWrite = null, input = null;
                try
                {
                    Native.SecurityAttributes attributes = new Native.SecurityAttributes();
                    attributes.Length = Marshal.SizeOf(typeof(Native.SecurityAttributes));
                    attributes.InheritHandle = true;
                    if (!Native.CreatePipe(out outRead, out outWrite, ref attributes, 0) ||
                        !Native.CreatePipe(out errRead, out errWrite, ref attributes, 0)) throw new Win32Exception();
                    if (!Native.SetHandleInformation(outRead, 1, 0) || !Native.SetHandleInformation(errRead, 1, 0)) throw new Win32Exception();
                    input = Native.CreateFile("NUL", 0x80000000, 3, ref attributes, 3, 0, IntPtr.Zero);
                    if (input.IsInvalid) throw new Win32Exception();
                    child.job = Native.CreateJobObject(IntPtr.Zero, null);
                    if (child.job.IsInvalid) throw new Win32Exception();
                    Native.JobExtendedLimitInformation limit = new Native.JobExtendedLimitInformation();
                    limit.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    if (!Native.SetInformationJobObject(child.job, 9, ref limit,
                        (uint)Marshal.SizeOf(typeof(Native.JobExtendedLimitInformation)))) throw new Win32Exception();
                    Native.StartupInfo startup = new Native.StartupInfo();
                    startup.Size = Marshal.SizeOf(typeof(Native.StartupInfo));
                    startup.Flags = 0x100; // STARTF_USESTDHANDLES
                    startup.StandardInput = input.DangerousGetHandle();
                    startup.StandardOutput = outWrite.DangerousGetHandle();
                    startup.StandardError = errWrite.DangerousGetHandle();
                    Native.ProcessInformation information;
                    StringBuilder command = new StringBuilder(QuoteArgument(executable) + " " + arguments);
                    if (!Native.CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true,
                        0x08000004, IntPtr.Zero, workingDirectory, ref startup, out information)) throw new Win32Exception();
                    child.process = new SafeKernelHandle(information.Process);
                    child.thread = new SafeKernelHandle(information.Thread);
                    if (!Native.AssignProcessToJobObject(child.job, child.process))
                    {
                        int code = Marshal.GetLastWin32Error();
                        Native.TerminateProcess(child.process, 1);
                        throw new Win32Exception(code, "無法建立可安全取消的下載程序。");
                    }
                    child.StandardOutput = new StreamReader(new FileStream(outRead, FileAccess.Read), new UTF8Encoding(false, false));
                    outRead = null;
                    child.StandardError = new StreamReader(new FileStream(errRead, FileAccess.Read), new UTF8Encoding(false, false));
                    errRead = null;
                    return child;
                }
                catch { child.Dispose(); throw; }
                finally
                {
                    if (outRead != null) outRead.Dispose();
                    if (errRead != null) errRead.Dispose();
                    if (outWrite != null) outWrite.Dispose();
                    if (errWrite != null) errWrite.Dispose();
                    if (input != null) input.Dispose();
                }
            }

            public void Resume()
            {
                lock (gate)
                {
                    if (Native.ResumeThread(thread) == UInt32.MaxValue) throw new Win32Exception();
                    thread.Dispose();
                    thread = null;
                }
            }

            public void Cancel()
            {
                // Registration disposal waits for any in-flight callback, and the same
                // lock guards completion, so cancellation never uses a recycled handle.
                lock (gate)
                {
                    if (job != null && !job.IsClosed && !job.IsInvalid) Native.TerminateJobObject(job, 1);
                }
            }

            public int WaitForExit()
            {
                if (Native.WaitForSingleObject(process, UInt32.MaxValue) != 0) throw new Win32Exception();
                uint code;
                if (!Native.GetExitCodeProcess(process, out code)) throw new Win32Exception();
                return unchecked((int)code);
            }

            public void CloseJob()
            {
                lock (gate)
                {
                    if (job != null) { job.Dispose(); job = null; }
                }
            }

            public void Dispose()
            {
                CloseJob();
                if (StandardOutput != null) StandardOutput.Dispose();
                if (StandardError != null) StandardError.Dispose();
                if (thread != null) thread.Dispose();
                if (process != null) process.Dispose();
            }
        }

        private sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeKernelHandle() : base(true) { }
            public SafeKernelHandle(IntPtr value) : base(true) { SetHandle(value); }
            protected override bool ReleaseHandle() { return Native.CloseHandle(handle); }
        }

        private static class Native
        {
            [StructLayout(LayoutKind.Sequential)] public struct SecurityAttributes
            { public int Length; public IntPtr SecurityDescriptor; [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle; }
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct StartupInfo
            {
                public int Size; public string Reserved; public string Desktop; public string Title;
                public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
                public short ShowWindow, ReservedSize; public IntPtr ReservedBytes;
                public IntPtr StandardInput, StandardOutput, StandardError;
            }
            [StructLayout(LayoutKind.Sequential)] public struct ProcessInformation
            { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
            [StructLayout(LayoutKind.Sequential)] public struct JobBasicLimitInformation
            {
                public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
                public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
                public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass;
            }
            [StructLayout(LayoutKind.Sequential)] public struct IoCounters
            { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
            [StructLayout(LayoutKind.Sequential)] public struct JobExtendedLimitInformation
            {
                public JobBasicLimitInformation BasicLimitInformation; public IoCounters IoInfo;
                public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
            }
            [DllImport("shell32.dll")] public static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr handle);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, uint size);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFile(string name, uint access, uint share, ref SecurityAttributes attributes, uint creation, uint flags, IntPtr template);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeKernelHandle CreateJobObject(IntPtr attributes, string name);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetInformationJobObject(SafeKernelHandle job, int informationClass, ref JobExtendedLimitInformation information, uint length);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern bool AssignProcessToJobObject(SafeKernelHandle job, SafeKernelHandle process);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern bool TerminateJobObject(SafeKernelHandle job, uint code);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern bool TerminateProcess(SafeKernelHandle process, uint code);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation information);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern uint ResumeThread(SafeKernelHandle thread);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern uint WaitForSingleObject(SafeKernelHandle handle, uint milliseconds);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetExitCodeProcess(SafeKernelHandle process, out uint code);
        }
    }
}
