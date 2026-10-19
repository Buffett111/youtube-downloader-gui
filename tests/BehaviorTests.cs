using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using SimpleDownloader;

internal static class BehaviorTests
{
    private static string fixtureDirectory;
    private static string outputDirectory;
    private static int assertions;

    private sealed class ProgressCollector : IProgress<DownloadUpdate>
    {
        private readonly List<DownloadUpdate> updates = new List<DownloadUpdate>();

        public void Report(DownloadUpdate value)
        {
            lock (updates) updates.Add(value);
        }

        public DownloadUpdate[] Snapshot()
        {
            lock (updates) return updates.ToArray();
        }
    }

    private static int Main(string[] args)
    {
        fixtureDirectory = Path.GetFullPath(args[0]);
        outputDirectory = Path.Combine(fixtureDirectory, "輸出 資料夾");
        Directory.CreateDirectory(outputDirectory);
        string previousMode = Environment.GetEnvironmentVariable("SD_TEST_MODE");
        string previousOutput = Environment.GetEnvironmentVariable("SD_TEST_OUTPUT");
        string previousMarkers = Environment.GetEnvironmentVariable("SD_TEST_MARKERS");
        string previousCallLog = Environment.GetEnvironmentVariable("SD_TEST_CALL_LOG");
        try
        {
            Run().GetAwaiter().GetResult();
            Console.WriteLine("PASS: " + assertions + " behavioral assertions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.ToString());
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("SD_TEST_MODE", previousMode);
            Environment.SetEnvironmentVariable("SD_TEST_OUTPUT", previousOutput);
            Environment.SetEnvironmentVariable("SD_TEST_MARKERS", previousMarkers);
            Environment.SetEnvironmentVariable("SD_TEST_CALL_LOG", previousCallLog);
        }
    }

    private static async Task Run()
    {
        TestArgumentRoundTrip();
        TestRequestValidation();
        TestDownloadArguments();
        TestPreviewArguments();
        await TestPreviewSingleVideo();
        await TestPreviewPlaylist();
        await TestPreviewFailures();
        await TestConfirmedDownload();
        await TestSuccessAndProgress();
        await TestFailure();
        await TestMissingExecutable();
        await TestCancellation();
        await TestPreviewCancellation();
        await TestAlreadyCanceled();
    }

    private static DownloadRequest Request()
    {
        return new DownloadRequest
        {
            Url = "https://www.youtube.com/watch?v=local-fixture&feature=test",
            OutputDirectory = outputDirectory,
            Format = MediaFormat.Mp4,
            MaxHeight = 1080
        };
    }

    private static void TestArgumentRoundTrip()
    {
        string[] expected = {
            "", "plain", "with spaces", "C:\\中文 資料夾\\", "embedded\"quote",
            "backslash\\\"quote", "two\\\\slashes\\", "https://example.test/watch?v=abc&list=def"
        };
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(fixtureDirectory, "yt-dlp.exe"),
            Arguments = "--test-echo " + String.Join(" ", expected.Select(DownloadService.QuoteArgument)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        string output;
        using (Process process = Process.Start(start))
        {
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Check(process.ExitCode == 0, "argument echo helper exited successfully");
        }
        string[] lines = output.Replace("\r", "").Split('\n');
        Check(lines.Length >= expected.Length, "argument count survives process launch");
        for (int i = 0; i < expected.Length; i++)
            Check(Encoding.UTF8.GetString(Convert.FromBase64String(lines[i])) == expected[i], "argument round trip " + i);
    }

    private static void TestRequestValidation()
    {
        DownloadService.ValidateRequest(Request());
        Check(Path.IsPathRooted(DownloadService.GetDownloadsDirectory()), "Downloads default is an absolute path");
        foreach (string badUrl in new[] { "", "--help", "ftp://example.test/video", "https://a.test/ https://b.test/", "https://a.test/\nhttps://b.test/" })
        {
            DownloadRequest request = Request();
            request.Url = badUrl;
            ExpectThrows(delegate { DownloadService.ValidateRequest(request); }, "reject invalid or multiple URL input");
        }
        DownloadRequest blankOutput = Request();
        blankOutput.OutputDirectory = "";
        ExpectThrows(delegate { DownloadService.ValidateRequest(blankOutput); }, "reject empty output directory");
        DownloadRequest badFormat = Request();
        badFormat.Format = (MediaFormat)999;
        ExpectThrows(delegate { DownloadService.ValidateRequest(badFormat); }, "reject unsupported format enum");
    }

    private static void TestDownloadArguments()
    {
        foreach (MediaFormat format in Enum.GetValues(typeof(MediaFormat)))
        {
            DownloadRequest request = Request();
            request.Format = format;
            List<string> arguments = DownloadService.BuildArguments(request, fixtureDirectory);
            Check(arguments.Contains("--no-playlist"), format + " downloads only the pasted item");
            Check(arguments[arguments.IndexOf("--playlist-items") + 1] == "1", format + " restricts a pure playlist URL to its first item");
            Check(arguments.Contains("--ignore-config"), format + " ignores unrelated machine configuration");
            Check(arguments.Last() == request.Url, format + " preserves the complete URL");
            Check(arguments.Contains("--"), format + " separates options from the URL");
            Check(arguments.Any(value => value.Contains(outputDirectory)), format + " uses requested output directory");
            if (format == MediaFormat.Mp4 || format == MediaFormat.Mkv)
            {
                Check(arguments.Any(value => value.Contains("height<=1080") || value.Contains("height<=?1080")), format + " applies the resolution limit");
                request.MaxHeight = 0;
                arguments = DownloadService.BuildArguments(request, fixtureDirectory);
                Check(!arguments.Any(value => value.Contains("height<=")), format + " permits original maximum resolution");
            }
            if (format == MediaFormat.Mp3 || format == MediaFormat.M4a || format == MediaFormat.Wav)
                Check(arguments.Contains(format.ToString().ToLowerInvariant()), format + " selects the requested audio codec");
        }
    }

    private static DownloadRequest PreviewRequest()
    {
        DownloadRequest request = Request();
        request.Url = "https://www.youtube.com/watch?v=resolved-video&list=long-list&index=8";
        request.OutputDirectory = Path.Combine(fixtureDirectory, "not-created-" + Guid.NewGuid().ToString("N"));
        return request;
    }

    private static void TestPreviewArguments()
    {
        foreach (MediaFormat format in Enum.GetValues(typeof(MediaFormat)))
        {
            DownloadRequest request = PreviewRequest();
            request.Format = format;
            List<string> arguments = DownloadService.BuildPreviewArguments(request, fixtureDirectory);
            Check(arguments.Contains("--simulate") && !arguments.Contains("--no-simulate"), format + " preview cannot download media");
            Check(arguments.Contains("--dump-single-json"), format + " preview requests machine-readable metadata");
            Check(arguments.Contains("--yes-playlist") && !arguments.Contains("--no-playlist"), format + " preview exposes playlist contents for user selection");
            Check(arguments.Contains("--flat-playlist") && !arguments.Contains("--playlist-items"), format + " preview lists the whole playlist without extracting media");
            Check(arguments.Contains("--ignore-config") && arguments.Contains("--no-plugin-dirs"), format + " preview ignores unrelated configuration and plugins");
            Check(arguments.Last() == request.Url && arguments[arguments.Count - 2] == "--", format + " preview preserves one complete URL after the option separator");
        }
    }

    private static async Task TestPreviewSingleVideo()
    {
        Environment.SetEnvironmentVariable("SD_TEST_MODE", "preview-single");
        DownloadRequest request = PreviewRequest();
        request.Url = "https://www.youtube.com/watch?v=resolved-video";
        string mediaPath = Path.Combine(outputDirectory, "preview-must-not-download-" + Guid.NewGuid().ToString("N") + ".mp4");
        Environment.SetEnvironmentVariable("SD_TEST_OUTPUT", mediaPath);
        DownloadPreview preview = await new DownloadService(fixtureDirectory).PreviewAsync(request, new ProgressCollector(), CancellationToken.None);
        Check(preview.Items[0].Title == "測試影片：中文 & title \"quoted\"", "preview preserves the actual Unicode title");
        Check(preview.Count == 1, "a single video previews exactly one selectable item");
        Check(preview.Items[0].Url == "https://www.youtube.com/watch?v=resolved-video", "preview resolves the canonical video URL");
        Check(preview.Items[0].Id == "resolved-video", "preview retains the resolved video ID for confirmation");
        Check(!Directory.Exists(request.OutputDirectory), "preview does not create the selected destination");
        Check(!File.Exists(mediaPath), "preview does not write a media file");

        Environment.SetEnvironmentVariable("SD_TEST_MODE", "preview-no-url");
        preview = await new DownloadService(fixtureDirectory).PreviewAsync(request, null, CancellationToken.None);
        Check(preview.Items[0].Url == request.Url, "a single video without webpage_url safely retains its original URL");
    }

    private static async Task TestPreviewPlaylist()
    {
        Environment.SetEnvironmentVariable("SD_TEST_MODE", "preview-playlist");
        DownloadRequest request = PreviewRequest();
        DownloadPreview preview = await new DownloadService(fixtureDirectory).PreviewAsync(request, null, CancellationToken.None);
        Check(preview.Count == 3, "playlist inspection lists every selectable video");
        Check(preview.IsPlaylist, "preview identifies the source as a playlist");
        Check(preview.PlaylistTitle == "含有多部影片的播放清單", "preview retains the playlist context");
        Check(preview.Items[0].Title == "測試影片：中文 & title \"quoted\"" && preview.Items[1].Title == "第二部影片" && preview.Items[2].Title == "第三部影片", "playlist inspection retains each individual title in order");
        Check(preview.Items[0].Url == "https://www.youtube.com/watch?v=resolved-video" && preview.Items[1].Url == "https://www.youtube.com/watch?v=second-video", "playlist inspection resolves each child video URL");
        Check(preview.CurrentVideoId == "resolved-video", "a video plus playlist URL identifies the currently linked video");
        Check(!Directory.Exists(request.OutputDirectory), "playlist inspection does not create a destination");
        Environment.SetEnvironmentVariable("SD_TEST_MODE", "preview-container-invalid");
        preview = await new DownloadService(fixtureDirectory).PreviewAsync(request, null, CancellationToken.None);
        Check(preview.Count == 2 && preview.Items[0].IsAvailable && !preview.Items[1].IsAvailable, "an unavailable playlist item remains visible without blocking valid videos");
        Check(!String.IsNullOrWhiteSpace(preview.Items[1].UnavailableReason), "an unavailable playlist item provides an explanation");
    }

    private static async Task TestPreviewFailures()
    {
        foreach (string mode in new[] { "failure", "preview-empty", "preview-malformed", "preview-multiple",
            "preview-no-title", "preview-no-id", "preview-bad-url", "preview-container-empty" })
        {
            Environment.SetEnvironmentVariable("SD_TEST_MODE", mode);
            DownloadRequest request = PreviewRequest();
            Exception error = await CaptureFailure(new DownloadService(fixtureDirectory).PreviewAsync(request, null, CancellationToken.None));
            Check(error != null && !(error is OperationCanceledException), mode + " prevents unconfirmed download");
            Check(!Directory.Exists(request.OutputDirectory), mode + " does not create a destination");
            if (mode == "failure") Check(error.ToString().Contains("simulated downloader failure"), "preview retains the downloader failure detail");
        }
    }

    private static async Task TestConfirmedDownload()
    {
        string callLog = Path.Combine(fixtureDirectory, "confirmed-" + Guid.NewGuid().ToString("N") + ".jsonl");
        Environment.SetEnvironmentVariable("SD_TEST_CALL_LOG", callLog);
        Environment.SetEnvironmentVariable("SD_TEST_MODE", "preview-playlist");
        DownloadRequest request = PreviewRequest();
        request.Url = "https://www.youtube.com/playlist?list=long-list";
        var service = new DownloadService(fixtureDirectory);
        DownloadPreview preview = await service.PreviewAsync(request, null, CancellationToken.None);
        Check(File.ReadAllLines(callLog).Length == 1, "metadata inspection alone does not start a download process");
        PreviewItem selected = preview.Items[1];
        request.Url = selected.Url;
        request.ExpectedId = selected.Id;
        string result = Path.Combine(request.OutputDirectory, "confirmed.mp4");
        Environment.SetEnvironmentVariable("SD_TEST_MODE", "success");
        Environment.SetEnvironmentVariable("SD_TEST_OUTPUT", result);
        await service.DownloadAsync(request, null, CancellationToken.None);
        string[][] calls = File.ReadAllLines(callLog).Select(line => new JavaScriptSerializer().Deserialize<string[]>(line)).ToArray();
        Check(calls.Length == 2 && calls[0].Contains("--simulate") && calls[1].Contains("--no-simulate"), "confirmation transitions from metadata-only to exactly one media download");
        Check(calls[1].Last() == selected.Url && calls[1].Last() != "https://www.youtube.com/playlist?list=long-list", "confirmed download uses the selected video's inspected URL");
        int filterIndex = Array.IndexOf(calls[1], "--match-filters");
        Check(filterIndex >= 0 && calls[1][filterIndex + 1].Contains("second-video"), "confirmed download guards the selected video's inspected ID");
        Check(File.Exists(result), "confirmed download produces the requested file");
        Environment.SetEnvironmentVariable("SD_TEST_CALL_LOG", null);
    }

    private static async Task TestSuccessAndProgress()
    {
        string output = Path.Combine(outputDirectory, "測試影片 中文 [local].mp4");
        Environment.SetEnvironmentVariable("SD_TEST_MODE", "success");
        Environment.SetEnvironmentVariable("SD_TEST_OUTPUT", output);
        var updates = new ProgressCollector();
        await new DownloadService(fixtureDirectory).DownloadAsync(Request(), updates, CancellationToken.None);
        DownloadUpdate[] snapshot = updates.Snapshot();
        Check(File.Exists(output), "successful downloader fixture wrote the output");
        Check(snapshot.Any(value => value.Percent.HasValue && Math.Abs(value.Percent.Value - 25) < 0.01), "byte progress is parsed as 25 percent");
        Check(snapshot.Any(value => String.Equals(value.FilePath, output, StringComparison.OrdinalIgnoreCase)), "JSON Unicode result path survives decoding");
        Check(snapshot.Any(value => value.Percent.HasValue && value.Percent.Value == 100), "successful completion reports 100 percent");
    }

    private static async Task TestFailure()
    {
        Environment.SetEnvironmentVariable("SD_TEST_MODE", "failure");
        Exception error = await CaptureFailure(new DownloadService(fixtureDirectory).DownloadAsync(Request(), new ProgressCollector(), CancellationToken.None));
        Check(error != null, "nonzero downloader exit is surfaced");
        Check(error.ToString().Contains("simulated downloader failure"), "downloader diagnostic is retained");
    }

    private static async Task TestMissingExecutable()
    {
        string missing = Path.Combine(fixtureDirectory, "missing tools");
        Directory.CreateDirectory(missing);
        Exception error;
        try
        {
            error = await CaptureFailure(new DownloadService(missing).DownloadAsync(Request(), new ProgressCollector(), CancellationToken.None));
        }
        catch (Exception caught) { error = caught; }
        Check(error != null, "missing local executable is surfaced");
    }

    private static async Task TestCancellation()
    {
        string markers = Path.Combine(fixtureDirectory, "cancellation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(markers);
        Environment.SetEnvironmentVariable("SD_TEST_MODE", "cancel");
        Environment.SetEnvironmentVariable("SD_TEST_MARKERS", markers);
        using (var cancellation = new CancellationTokenSource())
        {
            Task task = new DownloadService(fixtureDirectory).DownloadAsync(Request(), new ProgressCollector(), cancellation.Token);
            try
            {
                await WaitFor(delegate { return File.Exists(Path.Combine(markers, "child.pid")); }, 5000, "downloader spawned transcoder child");
                cancellation.Cancel();
                Check(await Task.WhenAny(task, Task.Delay(5000)) == task, "cancellation completes within five seconds");
                Exception error = await CaptureFailure(task);
                Check(error is OperationCanceledException, "cancellation is distinguishable from downloader failure");
                await WaitFor(delegate { return !MarkerProcessAlive(markers, "parent.pid") && !MarkerProcessAlive(markers, "child.pid"); }, 3000, "cancellation terminates downloader and transcoder");
            }
            finally
            {
                cancellation.Cancel();
                KillMarkerProcess(markers, "parent.pid");
                KillMarkerProcess(markers, "child.pid");
            }
        }
    }

    private static async Task TestAlreadyCanceled()
    {
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            Exception error;
            try { error = await CaptureFailure(new DownloadService(fixtureDirectory).DownloadAsync(Request(), new ProgressCollector(), cancellation.Token)); }
            catch (Exception caught) { error = caught; }
            Check(error is OperationCanceledException, "already canceled request is canceled immediately");
            DownloadRequest previewRequest = PreviewRequest();
            try { error = await CaptureFailure(new DownloadService(fixtureDirectory).PreviewAsync(previewRequest, null, cancellation.Token)); }
            catch (Exception caught) { error = caught; }
            Check(error is OperationCanceledException, "already canceled metadata request is canceled immediately");
            Check(!Directory.Exists(previewRequest.OutputDirectory), "already canceled preview leaves no destination");
        }
    }

    private static async Task TestPreviewCancellation()
    {
        string markers = Path.Combine(fixtureDirectory, "preview-cancellation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(markers);
        Environment.SetEnvironmentVariable("SD_TEST_MODE", "cancel");
        Environment.SetEnvironmentVariable("SD_TEST_MARKERS", markers);
        DownloadRequest request = PreviewRequest();
        using (var cancellation = new CancellationTokenSource())
        {
            Task task = new DownloadService(fixtureDirectory).PreviewAsync(request, null, cancellation.Token);
            try
            {
                await WaitFor(delegate { return File.Exists(Path.Combine(markers, "child.pid")); }, 5000, "metadata extractor spawned its helper");
                cancellation.Cancel();
                Check(await Task.WhenAny(task, Task.Delay(5000)) == task, "metadata cancellation completes within five seconds");
                Check(await CaptureFailure(task) is OperationCanceledException, "metadata cancellation is distinguishable from extraction failure");
                await WaitFor(delegate { return !MarkerProcessAlive(markers, "parent.pid") && !MarkerProcessAlive(markers, "child.pid"); }, 3000, "metadata cancellation terminates extractor and helper");
                Check(!Directory.Exists(request.OutputDirectory), "canceled metadata leaves no destination folder");
            }
            finally
            {
                cancellation.Cancel();
                KillMarkerProcess(markers, "parent.pid");
                KillMarkerProcess(markers, "child.pid");
            }
        }
    }

    private static async Task<Exception> CaptureFailure(Task task)
    {
        try { await task; return null; }
        catch (Exception error) { return error; }
    }

    private static async Task WaitFor(Func<bool> predicate, int timeoutMilliseconds, string description)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!predicate() && watch.ElapsedMilliseconds < timeoutMilliseconds)
            await Task.Delay(25);
        Check(predicate(), description);
    }

    private static bool MarkerProcessAlive(string markers, string file)
    {
        string path = Path.Combine(markers, file);
        if (!File.Exists(path)) return false;
        int processId;
        if (!Int32.TryParse(File.ReadAllText(path), out processId)) return true;
        try { using (Process process = Process.GetProcessById(processId)) return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static void KillMarkerProcess(string markers, string file)
    {
        string path = Path.Combine(markers, file);
        int processId;
        if (!File.Exists(path) || !Int32.TryParse(File.ReadAllText(path), out processId)) return;
        try { using (Process process = Process.GetProcessById(processId)) if (!process.HasExited) process.Kill(); }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }

    private static void ExpectThrows(Action action, string description)
    {
        bool threw = false;
        try { action(); } catch (ArgumentException) { threw = true; }
        Check(threw, description);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        assertions++;
        Console.WriteLine("  OK " + description);
    }
}
