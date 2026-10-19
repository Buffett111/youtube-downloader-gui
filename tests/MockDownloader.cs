using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

// Local test double only. No network access or YouTube download is performed.
internal static class MockDownloader
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length > 0 && args[0] == "--test-echo")
        {
            for (int i = 1; i < args.Length; i++)
                Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(args[i])));
            return 0;
        }

        string markerDirectory = Environment.GetEnvironmentVariable("SD_TEST_MARKERS");
        if (args.Length > 0 && args[0] == "--test-child")
        {
            File.WriteAllText(Path.Combine(markerDirectory, "child.pid"), Process.GetCurrentProcess().Id.ToString());
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        if (Array.IndexOf(args, "--version") >= 0)
        {
            Console.WriteLine("2026.10.14");
            return 0;
        }

        string mode = Environment.GetEnvironmentVariable("SD_TEST_MODE") ?? "success";
        string callLog = Environment.GetEnvironmentVariable("SD_TEST_CALL_LOG");
        if (!String.IsNullOrEmpty(callLog))
            File.AppendAllText(callLog, new JavaScriptSerializer().Serialize(args) + Environment.NewLine, new UTF8Encoding(false));
        bool simulate = Array.IndexOf(args, "--simulate") >= 0;
        if (mode == "failure" || (mode == "preview-batch-failure" && !simulate && args[args.Length - 1].Contains("second-video")))
        {
            Console.Error.WriteLine("ERROR: simulated downloader failure");
            return 17;
        }

        if (mode == "cancel" || (mode == "preview-batch-cancel" && !simulate))
        {
            File.WriteAllText(Path.Combine(markerDirectory, "parent.pid"), Process.GetCurrentProcess().Id.ToString());
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                Arguments = "--test-child",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Console.WriteLine("__SD_PROGRESS__\tdownloading\t10\t100\tNA\t20\t5");
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        if (simulate)
        {
            if (mode == "preview-empty") return 0;
            if (mode == "preview-malformed") { Console.WriteLine("{bad metadata"); return 0; }
            var metadata = new Dictionary<string, object> {
                { "id", "resolved-video" }, { "title", "測試影片：中文 & title \"quoted\"" },
                { "webpage_url", "https://www.youtube.com/watch?v=resolved-video" },
                { "_type", "video" }, { "extractor_key", "Youtube" }
            };
            if (mode == "preview-no-title") metadata.Remove("title");
            if (mode == "preview-no-id") metadata.Remove("id");
            if (mode == "preview-no-url") metadata.Remove("webpage_url");
            if (mode == "preview-bad-url") metadata["webpage_url"] = "file:///C:/unsafe.mp4";
            if (mode == "preview-playlist" || mode == "preview-container-single" || mode == "preview-container-empty" || mode == "preview-container-invalid" || mode == "preview-batch-failure" || mode == "preview-batch-cancel")
            {
                var second = new Dictionary<string, object> {
                    { "_type", "url" }, { "id", "second-video" }, { "title", "第二部影片" },
                    { "url", "https://www.youtube.com/watch?v=second-video" }
                };
                var third = new Dictionary<string, object> {
                    { "_type", "url" }, { "id", "third-video" }, { "title", "第三部影片" },
                    { "url", "https://www.youtube.com/watch?v=third-video" }
                };
                object[] entries = mode == "preview-container-empty" ? new object[0] :
                    mode == "preview-container-single" ? new object[] { metadata } :
                    mode == "preview-container-invalid" ? new object[] { metadata, null } : new object[] { metadata, second, third };
                metadata = new Dictionary<string, object> {
                    { "_type", "playlist" }, { "title", "含有多部影片的播放清單" },
                    { "playlist_count", entries.Length }, { "entries", entries },
                    { "webpage_url", "https://www.youtube.com/playlist?list=long-list" }
                };
            }
            Console.WriteLine(new JavaScriptSerializer().Serialize(metadata));
            if (mode == "preview-multiple") Console.WriteLine(new JavaScriptSerializer().Serialize(metadata));
            return 0;
        }

        string result = Environment.GetEnvironmentVariable("SD_TEST_OUTPUT");
        File.WriteAllText(result, "local fixture output", new UTF8Encoding(false));
        Console.WriteLine("__SD_PROGRESS__\tdownloading\t25\t100\tNA\t50\t2");
        Console.WriteLine("__SD_PROGRESS__\tfinished\t100\t100\tNA\tNA\t0");
        Console.WriteLine("__SD_POST__\tstarted");
        Console.WriteLine("__SD_POST__\tfinished");
        Console.WriteLine("__SD_FILE__" + new JavaScriptSerializer().Serialize(result));
        return 0;
    }
}
