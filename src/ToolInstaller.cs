using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SimpleDownloader
{
    // Official release sources (the version and URLs are resolved at installation time):
    // https://github.com/yt-dlp/yt-dlp#release-files
    // https://github.com/yt-dlp/FFmpeg-Builds
    // https://github.com/denoland/deno/releases
    // https://docs.github.com/en/rest/releases/releases#get-the-latest-release
    public sealed class ToolInstaller
    {
        private readonly string toolsDirectory;
        private readonly SemaphoreSlim installationLock = new SemaphoreSlim(1, 1);
        private static readonly string[] Executables = { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe" };

        public ToolInstaller(string toolsDirectory)
        {
            if (String.IsNullOrWhiteSpace(toolsDirectory)) throw new ArgumentException("未指定工具資料夾。", "toolsDirectory");
            this.toolsDirectory = Path.GetFullPath(toolsDirectory);
        }

        public bool IsReady
        {
            get
            {
                foreach (string name in Executables)
                    if (!IsExecutable(Path.Combine(toolsDirectory, name))) return false;
                return true;
            }
        }

        public string StatusText
        {
            get
            {
                var missing = new List<string>();
                foreach (string name in Executables)
                    if (!IsExecutable(Path.Combine(toolsDirectory, name))) missing.Add(name);
                return missing.Count == 0 ? "下載工具已就緒" : "尚未安裝：" + String.Join("、", missing.ToArray());
            }
        }

        public async Task EnsureInstalledAsync(IProgress<DownloadUpdate> progress, CancellationToken token, bool update)
        {
            await installationLock.WaitAsync(token).ConfigureAwait(false);
            string staging = null;
            bool keepStaging = false;
            try
            {
                if (!Environment.Is64BitOperatingSystem)
                    throw new PlatformNotSupportedException("此版本需要 64 位元 Windows。");
                if (!update && IsReady) return;
                Directory.CreateDirectory(toolsDirectory);
                staging = Path.Combine(toolsDirectory, ".install-" + Guid.NewGuid().ToString("N"));
                string incoming = Path.Combine(staging, "incoming");
                Directory.CreateDirectory(incoming);
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var manifest = new StringBuilder();
                manifest.AppendLine("Downloaded directly from the following official GitHub projects.");
                manifest.AppendLine("Installed UTC: " + DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture));

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(30);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("SimpleDownloader/1.0");
                    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                    if (update || !IsExecutable(Path.Combine(toolsDirectory, "yt-dlp.exe")))
                    {
                        Report(progress, "準備 yt-dlp", "讀取官方最新版本資訊…", null);
                        Release release = await GetReleaseAsync(client, "yt-dlp/yt-dlp", token).ConfigureAwait(false);
                        Asset asset = release.Require("yt-dlp.exe");
                        string path = Path.Combine(incoming, asset.Name);
                        await DownloadAsync(client, asset.Url, path, "下載 yt-dlp", progress, token, 256L * 1024 * 1024).ConfigureAwait(false);
                        string checksumPath = Path.Combine(staging, "yt-dlp-SHA2-256SUMS");
                        Asset checksum = release.Require("SHA2-256SUMS");
                        await DownloadAsync(client, checksum.Url, checksumPath, "驗證 yt-dlp", progress, token, 1024 * 1024).ConfigureAwait(false);
                        VerifyDigest(checksumPath, checksum.Digest, false);
                        VerifySha256(path, ReadChecksum(checksumPath, "yt-dlp.exe"));
                        VerifyDigest(path, asset.Digest, false);
                        AddManifest(manifest, release, asset);
                    }

                    if (update || !IsExecutable(Path.Combine(toolsDirectory, "ffmpeg.exe")) || !IsExecutable(Path.Combine(toolsDirectory, "ffprobe.exe")))
                    {
                        Report(progress, "準備 FFmpeg", "讀取官方最新版本資訊…", null);
                        Release release = await GetReleaseAsync(client, "yt-dlp/FFmpeg-Builds", token).ConfigureAwait(false);
                        Asset asset = release.Require("ffmpeg-master-latest-win64-gpl.zip");
                        string path = Path.Combine(staging, "ffmpeg.zip");
                        await DownloadAsync(client, asset.Url, path, "下載 FFmpeg", progress, token, 1024L * 1024 * 1024).ConfigureAwait(false);
                        VerifyDigest(path, asset.Digest, true);
                        Report(progress, "解壓 FFmpeg", "準備影片合併與音樂轉檔工具…", null);
                        ExtractExecutables(path, incoming, new[] { "ffmpeg.exe", "ffprobe.exe" }, "FFmpeg", token);
                        AddManifest(manifest, release, asset);
                    }

                    if (update || !IsExecutable(Path.Combine(toolsDirectory, "deno.exe")))
                    {
                        Report(progress, "準備 Deno", "讀取官方最新版本資訊…", null);
                        Release release = await GetReleaseAsync(client, "denoland/deno", token).ConfigureAwait(false);
                        Asset asset = release.Require("deno-x86_64-pc-windows-msvc.zip");
                        string path = Path.Combine(staging, "deno.zip");
                        await DownloadAsync(client, asset.Url, path, "下載 Deno", progress, token, 512L * 1024 * 1024).ConfigureAwait(false);
                        Asset checksum = release.Find(asset.Name + ".sha256sum");
                        if (checksum != null)
                        {
                            string checksumPath = Path.Combine(staging, "deno.sha256sum");
                            await DownloadAsync(client, checksum.Url, checksumPath, "驗證 Deno", progress, token, 1024 * 1024).ConfigureAwait(false);
                            VerifyDigest(checksumPath, checksum.Digest, false);
                            VerifySha256(path, ReadChecksum(checksumPath, asset.Name));
                            VerifyDigest(path, asset.Digest, false);
                        }
                        else VerifyDigest(path, asset.Digest, true);
                        ExtractExecutables(path, incoming, new[] { "deno.exe" }, "Deno", token);
                        AddManifest(manifest, release, asset);
                    }
                }

                Report(progress, "檢查下載工具", "確認各工具可以正常啟動…", null);
                foreach (string name in Executables)
                {
                    string path = Path.Combine(incoming, name);
                    if (File.Exists(path)) await ValidateExecutableAsync(path, token).ConfigureAwait(false);
                }
                File.WriteAllText(Path.Combine(incoming, "installed-sources.txt"), manifest.ToString(), new UTF8Encoding(false));
                token.ThrowIfCancellationRequested();
                Report(progress, "安裝下載工具", "儲存已驗證的工具…", null);
                // Once the small commit starts it is completed or rolled back as a unit;
                // cancellation cannot leave half of the new tool set installed.
                try { CommitFiles(incoming, staging); }
                catch (RollbackException) { keepStaging = true; throw; }
                Report(progress, "下載工具已就緒", "yt-dlp、FFmpeg 與 Deno 已安裝完成。", 100);
            }
            finally
            {
                if (staging != null && !keepStaging)
                {
                    try { Directory.Delete(staging, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                installationLock.Release();
            }
        }

        private static async Task<Release> GetReleaseAsync(HttpClient client, string repository, CancellationToken token)
        {
            using (var response = await client.GetAsync("https://api.github.com/repos/" + repository + "/releases/latest", HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            using (token.Register(delegate { response.Dispose(); }))
            {
                CheckResponse(response);
                string json;
                try { json = await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
                catch { token.ThrowIfCancellationRequested(); throw; }
                token.ThrowIfCancellationRequested();
                var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
                var data = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (data == null) throw new InvalidDataException("GitHub 回傳的版本資訊無法讀取。");
                var release = new Release { Repository = repository, Tag = Text(data, "tag_name") };
                object entries;
                if (!data.TryGetValue("assets", out entries) || !(entries is IEnumerable))
                    throw new InvalidDataException("官方版本未提供可下載檔案。");
                foreach (object entry in (IEnumerable)entries)
                {
                    var item = entry as Dictionary<string, object>;
                    if (item == null) continue;
                    string url = Text(item, "browser_download_url");
                    if (!url.StartsWith("https://github.com/" + repository + "/releases/download/", StringComparison.Ordinal))
                        throw new InvalidDataException("官方版本資訊包含非預期的下載來源。");
                    release.Assets.Add(new Asset { Name = Text(item, "name"), Url = url, Digest = Text(item, "digest") });
                }
                return release;
            }
        }

        private static async Task DownloadAsync(HttpClient client, string url, string destination, string status, IProgress<DownloadUpdate> progress, CancellationToken token, long maximumBytes)
        {
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            using (token.Register(delegate { response.Dispose(); }))
            {
                CheckResponse(response);
                long? total = response.Content.Headers.ContentLength;
                if (total.HasValue && (total.Value <= 0 || total.Value > maximumBytes))
                    throw new InvalidDataException("下載檔案大小不符合預期。");
                try
                {
                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        byte[] buffer = new byte[81920];
                        long received = 0;
                        var timer = Stopwatch.StartNew();
                        long reported = -500;
                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            int count = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                            if (count == 0) break;
                            received += count;
                            if (received > maximumBytes) throw new InvalidDataException("下載檔案超過預期大小。");
                            await output.WriteAsync(buffer, 0, count, token).ConfigureAwait(false);
                            if (timer.ElapsedMilliseconds - reported >= 200)
                            {
                                reported = timer.ElapsedMilliseconds;
                                string detail = (received / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
                                if (total.HasValue) detail += " / " + (total.Value / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
                                Report(progress, status, detail, total.HasValue ? (double?)(received * 100.0 / total.Value) : null);
                            }
                        }
                        if (received == 0 || (total.HasValue && received != total.Value))
                            throw new InvalidDataException("下載未完成，請重新安裝工具。");
                    }
                }
                catch { token.ThrowIfCancellationRequested(); throw; }
            }
        }

        private static void CheckResponse(HttpResponseMessage response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                throw new HttpRequestException("GitHub 暫時限制下載請求，請稍後再試。");
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage.RequestUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("工具下載來源必須使用 HTTPS。");
        }

        private static string ReadChecksum(string path, string assetName)
        {
            foreach (string line in File.ReadAllLines(path))
            {
                Match match = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})(?:\s+\*?(.+))?$");
                if (match.Success && (!match.Groups[2].Success || match.Groups[2].Value == assetName))
                    return match.Groups[1].Value;
            }
            // Deno's Windows release currently publishes PowerShell Get-FileHash
            // output (Algorithm / Hash / Path), while Unix assets use sha256sum.
            string text = File.ReadAllText(path);
            Match hash = Regex.Match(text, @"^\s*Hash\s*:\s*([0-9a-fA-F]{64})\s*$", RegexOptions.Multiline);
            Match filename = Regex.Match(text, @"^\s*Path\s*:\s*(.+?)\s*$", RegexOptions.Multiline);
            if (Regex.IsMatch(text, @"^\s*Algorithm\s*:\s*SHA256\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                && hash.Success && filename.Success && Path.GetFileName(filename.Groups[1].Value) == assetName)
                return hash.Groups[1].Value;
            throw new InvalidDataException("官方校驗檔中找不到 " + assetName + " 的 SHA-256。");
        }

        private static void VerifyDigest(string path, string digest, bool required)
        {
            if (!String.IsNullOrEmpty(digest) && digest.StartsWith("sha256:", StringComparison.Ordinal))
                VerifySha256(path, digest.Substring(7));
            else if (required) throw new InvalidDataException("官方版本未提供 SHA-256 校驗碼，無法安全安裝。");
        }

        private static void VerifySha256(string path, string expected)
        {
            if (!Regex.IsMatch(expected ?? "", "^[0-9a-fA-F]{64}$")) throw new InvalidDataException("SHA-256 校驗碼格式錯誤。");
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                if (!String.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(Path.GetFileName(path) + " 校驗失敗，請重新安裝工具。");
            }
        }

        private static void ExtractExecutables(string archivePath, string destination, string[] required, string package, CancellationToken token)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                int noticeNumber = 0;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    string basename = entry.Name;
                    bool executable = Array.Exists(required, delegate(string candidate) { return String.Equals(candidate, basename, StringComparison.OrdinalIgnoreCase); });
                    bool notice = basename.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) || basename.StartsWith("COPYING", StringComparison.OrdinalIgnoreCase) || basename.StartsWith("README", StringComparison.OrdinalIgnoreCase);
                    if (!executable && !notice) continue;
                    if (entry.Length <= 0 || entry.Length > (executable ? 512L * 1024 * 1024 : 2L * 1024 * 1024))
                        throw new InvalidDataException("壓縮檔中的檔案大小不符合預期。");
                    if (executable && !found.Add(basename)) throw new InvalidDataException("壓縮檔中含有重複的 " + basename + "。");
                    // Never use entry.FullName to construct an output path (prevents ZIP traversal).
                    string name = executable ? basename.ToLowerInvariant() : package + "-notice-" + (++noticeNumber).ToString(CultureInfo.InvariantCulture) + ".txt";
                    using (var source = entry.Open())
                    using (var output = new FileStream(Path.Combine(destination, name), FileMode.CreateNew))
                    {
                        byte[] buffer = new byte[81920];
                        int count;
                        long written = 0;
                        while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            token.ThrowIfCancellationRequested();
                            written += count;
                            if (written > entry.Length) throw new InvalidDataException("壓縮檔內容大小不一致。");
                            output.Write(buffer, 0, count);
                        }
                        if (written != entry.Length) throw new InvalidDataException("壓縮檔內容不完整。");
                    }
                }
            }
            foreach (string name in required)
                if (!found.Contains(name)) throw new InvalidDataException("壓縮檔中找不到 " + name + "。");
        }

        private static bool IsExecutable(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    return stream.Length > 1024 && stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static async Task ValidateExecutableAsync(string path, CancellationToken token)
        {
            if (!IsExecutable(path)) throw new InvalidDataException(Path.GetFileName(path) + " 不是有效的 Windows 程式。");
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(path, Path.GetFileName(path).StartsWith("ff", StringComparison.OrdinalIgnoreCase) ? "-version" : "--version")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(path)
                };
                token.ThrowIfCancellationRequested();
                if (!process.Start()) throw new IOException("無法啟動 " + Path.GetFileName(path) + "。");
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                try
                {
                    var timer = Stopwatch.StartNew();
                    while (!process.HasExited)
                    {
                        token.ThrowIfCancellationRequested();
                        if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException(Path.GetFileName(path) + " 啟動逾時。");
                        await Task.Delay(100, token).ConfigureAwait(false);
                    }
                    string version = await output.ConfigureAwait(false);
                    string error = await errors.ConfigureAwait(false);
                    if (process.ExitCode != 0 || String.IsNullOrWhiteSpace(version))
                        throw new IOException(Path.GetFileName(path) + " 無法正常執行。" + (error.Length > 300 ? error.Substring(0, 300) : error));
                }
                finally
                {
                    if (!process.HasExited) { try { process.Kill(); } catch (InvalidOperationException) { } }
                }
            }
        }

        private void CommitFiles(string incoming, string staging)
        {
            string backup = Path.Combine(staging, "backup");
            Directory.CreateDirectory(backup);
            var completed = new List<string>();
            try
            {
                foreach (string path in Directory.GetFiles(incoming))
                {
                    string name = Path.GetFileName(path);
                    string destination = Path.Combine(toolsDirectory, name);
                    if (File.Exists(destination)) File.Replace(path, destination, Path.Combine(backup, name), true);
                    else File.Move(path, destination);
                    completed.Add(name);
                }
            }
            catch (Exception installError)
            {
                var rollbackErrors = new List<Exception>();
                for (int index = completed.Count - 1; index >= 0; --index)
                {
                    string name = completed[index];
                    string destination = Path.Combine(toolsDirectory, name);
                    string old = Path.Combine(backup, name);
                    try
                    {
                        if (File.Exists(old))
                        {
                            if (File.Exists(destination)) File.Replace(old, destination, null, true);
                            else File.Move(old, destination);
                        }
                        else File.Delete(destination);
                    }
                    catch (Exception error) { rollbackErrors.Add(error); }
                }
                if (rollbackErrors.Count != 0)
                    throw new RollbackException("安裝失敗，部分舊檔案無法還原。備份保留於：" + backup, installError);
                throw new IOException("無法替換下載工具，請確認沒有其他程式正在使用工具後重試。原有工具已保留。", installError);
            }
        }

        private static void AddManifest(StringBuilder manifest, Release release, Asset asset)
        {
            manifest.AppendLine(release.Repository + " " + release.Tag);
            manifest.AppendLine(asset.Url);
            manifest.AppendLine(asset.Digest);
            manifest.AppendLine("Project and licensing: https://github.com/" + release.Repository);
            manifest.AppendLine();
        }

        private static string Text(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }

        private static void Report(IProgress<DownloadUpdate> progress, string status, string detail, double? percent)
        {
            if (progress != null) progress.Report(new DownloadUpdate { Status = status, Detail = detail, Percent = percent });
        }

        private sealed class Asset
        {
            public string Name;
            public string Url;
            public string Digest;
        }

        private sealed class Release
        {
            public string Repository;
            public string Tag;
            public readonly List<Asset> Assets = new List<Asset>();
            public Asset Find(string name) { return Assets.Find(delegate(Asset asset) { return asset.Name == name; }); }
            public Asset Require(string name) { return Find(name) ?? throwMissing(name); }
            private static Asset throwMissing(string name) { throw new InvalidDataException("官方版本未提供 " + name + "，請稍後再試。"); }
        }

        private sealed class RollbackException : IOException
        {
            public RollbackException(string message, Exception inner) : base(message, inner) { }
        }
    }
}
