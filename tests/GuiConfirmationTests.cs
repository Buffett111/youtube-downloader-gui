using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SimpleDownloader;

// Runs the actual MainForm operation against local mock tools. No media/network requests.
internal static class GuiConfirmationTests
{
    private static int assertions;
    private static int exitCode;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var host = new Form { ShowInTaskbar = false, Opacity = 0 })
        {
            host.Shown += async delegate
            {
                try
                {
                    foreach (string action in new[] { "cancel", "close", "enter", "confirm-all", "confirm-subset", "confirm-current", "zero", "unavailable", "metadata-error", "confirm-partial-failure", "confirm-batch-cancel" })
                        await TestAction(Path.GetFullPath(args[0]), action);
                    Console.WriteLine("PASS: " + assertions + " GUI confirmation assertions.");
                }
                catch (Exception error) { Console.Error.WriteLine(error); exitCode = 1; }
                finally { host.Close(); }
            };
            Application.Run(host);
        }
        return exitCode;
    }

    private static async Task TestAction(string fixtureDirectory, string action)
    {
        string runDirectory = Path.Combine(fixtureDirectory, "gui-" + action + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        string destination = Path.Combine(runDirectory, "destination");
        string callLog = Path.Combine(runDirectory, "calls.jsonl");
        string media = Path.Combine(destination, "confirmed.mp4");
        Environment.SetEnvironmentVariable("SD_TEST_MODE", action == "metadata-error" ? "preview-malformed" : action == "unavailable" ? "preview-container-invalid" :
            action == "confirm-partial-failure" ? "preview-batch-failure" : action == "confirm-batch-cancel" ? "preview-batch-cancel" : "preview-playlist");
        Environment.SetEnvironmentVariable("SD_TEST_CALL_LOG", callLog);
        Environment.SetEnvironmentVariable("SD_TEST_OUTPUT", media);
        Environment.SetEnvironmentVariable("SD_TEST_MARKERS", runDirectory);
        int dialogs = 0;
        int phase = 0;
        Exception modalError = null;
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        using (var form = new MainForm { ShowInTaskbar = false, Opacity = 0 })
        using (var timer = new System.Windows.Forms.Timer { Interval = 25 })
        {
            SetField(form, "installer", new ToolInstaller(fixtureDirectory));
            SetField(form, "downloader", new DownloadService(fixtureDirectory));
            Field<TextBox>(form, "urlBox").Text = action == "confirm-current" ?
                "https://www.youtube.com/watch?v=resolved-video&list=long-list" : "https://www.youtube.com/playlist?list=long-list";
            Field<TextBox>(form, "folderBox").Text = destination;
            // Avoid an OS message box for this expected failure; this is the existing close path.
            if (action == "metadata-error") SetField(form, "closeWhenDone", true);
            form.Show();
            timer.Tick += delegate
            {
                DownloadConfirmForm dialog = Application.OpenForms.OfType<DownloadConfirmForm>().FirstOrDefault();
                if (DateTime.UtcNow > deadline)
                {
                    modalError = new TimeoutException("GUI operation timed out: " + action);
                    CancellationTokenSource source = Field<CancellationTokenSource>(form, "cancellation");
                    if (source != null) source.Cancel();
                    if (dialog != null) dialog.Close();
                    return;
                }
                if (dialog == null)
                {
                    if (action == "confirm-batch-cancel" && phase == 2 && File.Exists(Path.Combine(runDirectory, "child.pid")))
                    {
                        phase = 3;
                        Field<Button>(form, "cancelButton").PerformClick();
                    }
                    return;
                }
                dialog.Opacity = 0;
                try
                {
                    var selection = (CheckedListBox)dialog.Controls.Find("VideoSelection", true).Single();
                    var confirm = (Button)dialog.Controls.Find("ConfirmDownload", true).Single();
                    if (phase == 0)
                    {
                        dialogs++;
                        Check(selection.Items.Count == (action == "unavailable" ? 2 : 3), action + " lists all playlist titles");
                        Check(selection.CheckedItems.Count == (action == "confirm-current" || action == "unavailable" ? 1 : 3), action + " has the expected default selection");
                        if (action == "confirm-subset" || action == "zero")
                        {
                            ((Button)dialog.Controls.Find("SelectNone", true).Single()).PerformClick();
                            if (action == "confirm-subset") { selection.SetItemChecked(0, true); selection.SetItemChecked(2, true); }
                        }
                        if (action == "confirm-all")
                        {
                            ((Button)dialog.Controls.Find("SelectNone", true).Single()).PerformClick();
                            ((Button)dialog.Controls.Find("SelectAll", true).Single()).PerformClick();
                        }
                        if (action == "unavailable") selection.SetItemChecked(1, true);
                        phase = 1;
                        return;
                    }
                    Check(!Directory.Exists(destination) && !File.Exists(media), action + " has no media or destination before confirmation");
                    Check(File.ReadAllLines(callLog).Length == 1, action + " starts only metadata extraction before confirmation");
                    int expectedCount = action == "zero" ? 0 : action == "confirm-subset" ? 2 : action == "confirm-current" || action == "unavailable" ? 1 : 3;
                    Check(selection.CheckedItems.Count == expectedCount, action + " preserves the chosen scope");
                    Control countDisplay = dialog.Controls.Find("SelectionCount", true).Single();
                    Check(countDisplay.Text.Contains(expectedCount.ToString()), action + " shows the chosen count");
                    Check(countDisplay.Visible && countDisplay.Height >= countDisplay.Font.Height && countDisplay.Bottom <= countDisplay.Parent.ClientSize.Height,
                        action + " count text fits visibly within its toolbar without vertical clipping");
                    Check(confirm.Enabled == (expectedCount > 0), action + " enables confirmation only with selected videos");
                    if (action == "close") dialog.Close();
                    else if (action == "enter")
                    {
                        Check(((Control)dialog.AcceptButton).Name == "CancelDownload", "Enter defaults to cancel instead of approving an unseen dialog");
                        dialog.AcceptButton.PerformClick();
                    }
                    else
                    {
                        phase = 2;
                        ((Button)dialog.Controls.Find(action.StartsWith("confirm", StringComparison.Ordinal) ? "ConfirmDownload" : "CancelDownload", true).Single()).PerformClick();
                    }
                }
                catch (Exception error) { modalError = error; dialog.Close(); }
            };
            timer.Start();
            Task task = (Task)typeof(MainForm).GetMethod("StartDownload", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
            await task;
            timer.Stop();
            if (modalError != null) throw modalError;
            string[][] calls = File.ReadAllLines(callLog).Select(line => new JavaScriptSerializer().Deserialize<string[]>(line)).ToArray();
            Check(dialogs == (action == "metadata-error" ? 0 : 1), action + " shows the expected number of confirmation dialogs");
            if (action == "confirm-batch-cancel")
            {
                Check(calls.Length == 2 && calls[1].Last() == "https://www.youtube.com/watch?v=resolved-video", "canceling the current batch item never starts remaining selected videos");
                Check(!File.Exists(media), "canceling the first batch item leaves no completed media output");
                Check(Field<Label>(form, "statusLabel").Text.Contains("取消"), "batch cancellation is reported separately from success");
            }
            else if (action.StartsWith("confirm", StringComparison.Ordinal))
            {
                string[] selectedIds = action == "confirm-all" || action == "confirm-partial-failure" ? new[] { "resolved-video", "second-video", "third-video" } :
                    action == "confirm-subset" ? new[] { "resolved-video", "third-video" } : new[] { "resolved-video" };
                Check(calls.Length == selectedIds.Length + 1, action + " starts exactly the confirmed number of media downloads");
                for (int i = 0; i < selectedIds.Length; i++)
                {
                    Check(calls[i + 1].Contains("--no-simulate") && calls[i + 1].Last() == "https://www.youtube.com/watch?v=" + selectedIds[i], action + " downloads only selected video " + selectedIds[i]);
                    int match = Array.IndexOf(calls[i + 1], "--match-filters");
                    Check(match >= 0 && calls[i + 1][match + 1].Contains(selectedIds[i]), action + " pins selected video ID " + selectedIds[i]);
                }
                Check(File.Exists(media), "explicit confirmation writes the media output");
                Check(Field<Label>(form, "statusLabel").Text.Contains("完成"), "explicit confirmation reaches completed UI state");
                if (action == "confirm-partial-failure")
                    Check(Field<TextBox>(form, "logBox").Text.Contains("simulated downloader failure"), "a failed item is logged while later selected items continue");
            }
            else
            {
                Check(calls.Length == 1 && calls[0].Contains("--simulate"), action + " never starts media download");
                Check(!Directory.Exists(destination) && !File.Exists(media), action + " leaves the destination untouched");
                if (action != "metadata-error") Check(Field<Label>(form, "statusLabel").Text == "已取消下載", action + " restores the canceled UI state");
            }
            Check(!Field<bool>(form, "busy"), action + " releases busy state");
            form.Close();
        }
    }

    private static IEnumerable<Control> AllControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in AllControls(child)) yield return descendant;
        }
    }

    private static T Field<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        assertions++;
        Console.WriteLine("  OK " + description);
    }
}
