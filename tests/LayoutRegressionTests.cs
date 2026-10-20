using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SimpleDownloader;

// These tests render only forms constructed by this test process. They never
// capture the desktop or change Windows display settings. Synthetic scaling
// checks geometry and font pressure; it does not replace testing real OS DPI.
internal static class LayoutRegressionTests
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

    private static readonly List<string> failures = new List<string>();
    private static int assertions;
    private static string outputDirectory;
    private static float hostScale;
    private static bool legacySystemDpi;

    [STAThread]
    private static int Main(string[] args)
    {
        legacySystemDpi = args.Contains("--legacy-system-dpi");
        if (legacySystemDpi) SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        outputDirectory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(outputDirectory);
        using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
            hostScale = graphics.DpiX / 96F;
        Console.WriteLine("Native host DPI: " + (hostScale * 96F).ToString("0"));
        Console.WriteLine("Synthetic scale cases: 100%, 125%, 150%, 175%, 200%; fonts and geometry scale together.");
        Console.WriteLine("Limitation: synthetic Control.Scale is not an OS DPI or monitor-switch test.");
        try
        {
            if (!args.Contains("--clipboard-only"))
            {
                TestMain(hostScale, new Size(820, 755), "native-host", true);
                foreach (float scale in new[] { 1F, 1.25F, 1.5F, 1.75F, 2F })
                {
                    foreach (Size size in new[] { new Size(820, 755), new Size(720, 560), new Size(600, 420), new Size(980, 620) })
                    {
                        string name = ScaleName(scale) + "-" + size.Width + "x" + size.Height;
                        TestMain(scale, size, name, size.Width == 820 || (scale == 1.5F && size.Width == 600));
                    }
                    foreach (Size size in new[] { new Size(760, 680), new Size(640, 560), new Size(560, 440) })
                        TestConfirmation(scale, size, ScaleName(scale) + "-" + size.Width + "x" + size.Height, size.Width == 760);
                }
            }
            if (args.Contains("--skip-clipboard")) Console.WriteLine("SKIP: Clipboard paste check was explicitly disabled for this run.");
            else TestClipboardPaste();
        }
        catch (Exception error)
        {
            failures.Add("Unexpected exception: " + error);
        }
        foreach (string failure in failures) Console.WriteLine("FAIL: " + failure);
        Console.WriteLine((failures.Count == 0 ? "PASS" : "FAIL") + ": " + assertions +
            " layout assertions; " + failures.Count + " failures.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void TestMain(float scale, Size logicalSize, string name, bool render)
    {
        using (var form = new MainForm { ShowInTaskbar = false, Opacity = 0 })
        {
            Prepare(form, scale, logicalSize);
            string context = "main/" + name;
            if (name == "native-host" && !legacySystemDpi)
            {
                bool perMonitorV2 = AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(form.Handle), new IntPtr(-4));
                Check(perMonitorV2, "native main window uses PerMonitorV2 DPI awareness");
                Console.WriteLine("Native window PerMonitorV2: " + perMonitorV2);
            }
            TextBox url = Field<TextBox>(form, "urlBox");
            TextBox folder = Field<TextBox>(form, "folderBox");
            ComboBox format = Field<ComboBox>(form, "formatBox");
            ComboBox quality = Field<ComboBox>(form, "qualityBox");
            CheckEditable(url, 200F * scale, context + "/URL");
            CheckEditable(folder, 150F * scale, context + "/folder");
            CheckChoice(format, 110F * scale, context + "/format");
            CheckChoice(quality, 100F * scale, context + "/quality");
            CheckGeometry(form, context);
            CheckNoIntersection(url, Field<Button>(form, "pasteButton"), context);
            CheckNoIntersection(format, quality, context);
            CheckNoIntersection(folder, Field<Button>(form, "browseButton"), context);
            if (render) Render(form, "main-" + name + ".png");
            Field<RadioButton>(form, "audioChoice").Checked = true;
            Pump(form);
            Check(!quality.Enabled, context + "/audio mode disables video quality");
            Check(format.Items.Count >= 3 && format.SelectedIndex >= 0, context + "/audio formats remain selectable");
            Field<RadioButton>(form, "videoChoice").Checked = true;
            Pump(form);
            Check(quality.Enabled && quality.SelectedIndex >= 0, context + "/video mode restores quality selection");
            // Log expansion used to mutate a fixed root row; it must not hide inputs.
            typeof(MainForm).GetMethod("ToggleLog", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
            Pump(form);
            Check(Field<TextBox>(form, "logBox").Visible, context + "/log expands");
            CheckEditable(url, 200F * scale, context + "/URL with log");
            CheckChoice(format, 110F * scale, context + "/format with log");
            CheckChoice(quality, 100F * scale, context + "/quality with log");
            CheckGeometry(form, context + "/log-expanded");
            CheckReachable(Field<Button>(form, "installButton"), form, context + "/installer remains reachable");
            CheckReachable(quality, form, context + "/quality remains reachable");
            CheckReachable(url, form, context + "/URL remains reachable");
            if (logicalSize.Width == 600 && scale >= 1.5F)
            {
                Field<Label>(form, "statusLabel").Text = "下載中 https://example.invalid/" + new string('x', 400);
                Field<Label>(form, "detailLabel").Text = "ERROR: " + new string('M', 400);
                Pump(form);
                CheckEditable(url, 200F * scale, context + "/URL with long status");
                CheckChoice(quality, 100F * scale, context + "/quality with long status");
                CheckGeometry(form, context + "/long-status");
            }
            typeof(MainForm).GetMethod("ToggleLog", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
            Pump(form);
            Check(!Field<TextBox>(form, "logBox").Visible, context + "/log collapses");
            form.Close();
        }
        Console.WriteLine("Checked " + name + " main form.");
    }

    private static void TestConfirmation(float scale, Size logicalSize, string name, bool render)
    {
        var preview = new DownloadPreview { IsPlaylist = true, PlaylistTitle = "播放清單操作示範 — 長標題與縮放驗證", CurrentVideoId = "second" };
        preview.Items.Add(new PreviewItem { Id = "first", Url = "https://example.invalid/first", Title = "第一部影片：清晰顯示完整標題", IsAvailable = true });
        preview.Items.Add(new PreviewItem { Id = "second", Url = "https://example.invalid/second", Title = "第二部影片：使用者可選取目前影片或整份清單", IsAvailable = true });
        preview.Items.Add(new PreviewItem { Id = "third", Url = "https://example.invalid/third", Title = "第三部影片：Windows 高 DPI 縮放與較小筆電視窗測試", IsAvailable = true });
        preview.Items.Add(new PreviewItem { Id = "private", Title = "無法讀取的私人影片", IsAvailable = false, UnavailableReason = "測試項目" });
        var request = new DownloadRequest { Url = "https://example.invalid/playlist", OutputDirectory = @"C:\Users\Example\Downloads", Format = MediaFormat.Mp4, MaxHeight = 1080 };
        using (var form = new DownloadConfirmForm(preview, request) { Opacity = 0 })
        {
            Prepare(form, scale, logicalSize);
            string context = "confirm/" + name;
            var list = (CheckedListBox)form.Controls.Find("VideoSelection", true).Single();
            var count = (Label)form.Controls.Find("SelectionCount", true).Single();
            var confirm = (Button)form.Controls.Find("ConfirmDownload", true).Single();
            Check(list.Visible && list.Height >= list.ItemHeight * 3, context + "/at least three playlist titles fit");
            Check(list.Width >= 200F * scale, context + "/playlist titles have useful width");
            Check(count.Height >= count.Font.Height && count.Width >= TextWidth(count), context + "/selected count is fully visible: " + count.Size + ", text=" + TextWidth(count) + "x" + count.Font.Height);
            Check(list.CheckedIndices.Count == 1 && list.GetItemChecked(1), context + "/current video remains selected");
            CheckGeometry(form, context);
            ((Button)form.Controls.Find("SelectAll", true).Single()).PerformClick();
            Pump(form);
            Check(list.CheckedIndices.Count == 3 && !list.GetItemChecked(3), context + "/select all excludes unavailable item");
            Check(confirm.Text.Contains("3") && count.Text.Contains("3"), context + "/selected count updates after select all");
            Check(count.Height >= count.Font.Height && count.Width >= TextWidth(count), context + "/updated count remains visible: " + count.Size + ", text=" + TextWidth(count) + "x" + count.Font.Height);
            if (render) Render(form, "confirmation-" + name + ".png");
            ((Button)form.Controls.Find("SelectNone", true).Single()).PerformClick();
            Pump(form);
            Check(!confirm.Enabled && list.CheckedIndices.Count == 0, context + "/empty selection disables download");
            form.Close();
        }
        Console.WriteLine("Checked " + name + " confirmation form.");
    }

    private static void Prepare(Form form, float targetScale, Size logicalSize)
    {
        form.Show();
        Pump(form);
        var fonts = Descendants(form).Concat(new[] { (Control)form })
            .Select(control => new KeyValuePair<Control, Font>(control, (Font)control.Font.Clone())).ToList();
        form.AutoScaleMode = AutoScaleMode.None;
        float factor = targetScale / hostScale;
        form.SuspendLayout();
        form.MinimumSize = Size.Empty;
        form.Scale(new SizeF(factor, factor));
        foreach (var entry in fonts)
        {
            Font original = entry.Value;
            entry.Key.Font = new Font(original.FontFamily, original.Size * factor, original.Style, original.Unit);
            original.Dispose();
        }
        form.ClientSize = new Size((int)Math.Round(logicalSize.Width * targetScale), (int)Math.Round(logicalSize.Height * targetScale));
        form.ResumeLayout(true);
        Pump(form);
    }

    private static void CheckEditable(TextBox box, float minimumWidth, string context)
    {
        Check(box.Visible && box.Enabled && !box.ReadOnly, context + " is editable and visible");
        Check(box.Width >= minimumWidth, context + " has useful input width: " + box.Width);
        Check(box.Height >= box.PreferredHeight - 1, context + " is not vertically clipped: " + box.Height + "/" + box.PreferredHeight);
        CheckAncestors(box, context);
    }

    private static void CheckChoice(ComboBox box, float minimumWidth, string context)
    {
        Check(box.Visible && box.Enabled && box.SelectedIndex >= 0, context + " is visible and selectable");
        Check(box.Width >= minimumWidth, context + " has useful selection width: " + box.Width);
        Check(box.Height >= box.PreferredHeight - 1, context + " is not vertically clipped: " + box.Height + "/" + box.PreferredHeight);
        CheckAncestors(box, context);
    }

    private static void CheckGeometry(Form form, string context)
    {
        foreach (Control control in Descendants(form).Where(control => control.Visible))
        {
            var scrolling = control as ScrollableControl;
            if (scrolling != null && scrolling.AutoScroll)
                Check(!scrolling.HorizontalScroll.Visible, context + "/" + Describe(control) + " needs no horizontal scrolling");
            var table = control as TableLayoutPanel;
            if (table != null)
            {
                Check(table.RowStyles.Count >= table.GetRowHeights().Length, context + "/" + Describe(table) + " defines every table row explicitly");
            }
            if (control is TextBox || control is ComboBox || control is Button || control is RadioButton || control is CheckedListBox || control is LinkLabel)
                CheckAncestors(control, context + "/" + Describe(control));
            var button = control as Button;
            if (button != null)
            {
                Check(button.ClientSize.Width >= TextWidth(button) + 6, context + "/" + Describe(button) + " button text fits horizontally");
                Check(button.ClientSize.Height >= button.Font.Height + 4, context + "/" + Describe(button) + " button text fits vertically");
            }
        }
    }

    private static void CheckAncestors(Control control, string context)
    {
        for (Control child = control; child.Parent != null && !(child is Form); child = child.Parent)
        {
            Control parent = child.Parent;
            var scrolling = parent as ScrollableControl;
            // A scrollable viewport may intentionally be shorter than the
            // content. Internal rows/cards must still contain their controls.
            if (scrolling != null && scrolling.AutoScroll)
            {
                Check(child.Width <= scrolling.DisplayRectangle.Width + 2, context + " fits scrollable content horizontally");
                continue;
            }
            Check(child.Left >= -2 && child.Top >= -2 &&
                child.Right <= parent.ClientSize.Width + 2 && child.Bottom <= parent.ClientSize.Height + 2,
                context + " stays within " + Describe(parent) + ": child=" + child.Bounds + ", parent=" + parent.ClientSize);
        }
    }

    private static void CheckNoIntersection(Control first, Control second, string context)
    {
        Rectangle a = first.RectangleToScreen(first.ClientRectangle);
        Rectangle b = second.RectangleToScreen(second.ClientRectangle);
        Check(!a.IntersectsWith(b), context + "/" + Describe(first) + " does not overlap " + Describe(second));
    }

    private static void CheckReachable(Control control, Form form, string context)
    {
        for (Control ancestor = control.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            var viewport = ancestor as ScrollableControl;
            if (viewport == null || !viewport.AutoScroll) continue;
            control.Focus();
            viewport.ScrollControlIntoView(control);
            Pump(form);
            Rectangle content = control.RectangleToScreen(control.ClientRectangle);
            Rectangle visible = viewport.RectangleToScreen(viewport.ClientRectangle);
            Check(content.Left >= visible.Left - 2 && content.Right <= visible.Right + 2 &&
                content.Top >= visible.Top - 2 && content.Bottom <= visible.Bottom + 2,
                context + ": control=" + content + ", viewport=" + visible + ", scroll=" + viewport.AutoScrollPosition);
        }
    }

    private static void TestClipboardPaste()
    {
        // Snapshot formats before replacing the clipboard; do not log any data.
        IDataObject original = Clipboard.GetDataObject();
        var snapshot = new DataObject();
        if (original != null)
            foreach (string format in original.GetFormats(false))
                snapshot.SetData(format, false, original.GetData(format, false));
        try
        {
            const string url = "https://www.youtube.com/watch?v=layout-test&list=regression-test";
            Clipboard.SetText("  " + url + "  ");
            using (var form = new MainForm { ShowInTaskbar = false, Opacity = 0 })
            {
                form.Show();
                Pump(form);
                var box = Field<TextBox>(form, "urlBox");
                Field<Button>(form, "pasteButton").PerformClick();
                Check(box.Text == url, "actual PasteUrl button reads and trims clipboard URL");
                box.SelectAll();
                Clipboard.SetText(url + "&index=2");
                box.Paste();
                Check(box.Text == url + "&index=2", "URL TextBox supports native paste and replacement");
                form.Close();
            }
        }
        finally
        {
            if (original == null) Clipboard.Clear();
            else Clipboard.SetDataObject(snapshot, true);
        }
    }

    private static int TextWidth(Control control)
    {
        return TextRenderer.MeasureText(control.Text, control.Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
    }

    private static void Pump(Control form)
    {
        form.PerformLayout();
        Application.DoEvents();
        form.PerformLayout();
        Application.DoEvents();
    }

    private static void Render(Form form, string fileName)
    {
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(outputDirectory, fileName), ImageFormat.Png);
        }
    }

    private static string ScaleName(float scale) { return ((int)Math.Round(scale * 100)).ToString() + "pct"; }
    private static string Describe(Control control)
    {
        if (!String.IsNullOrEmpty(control.Name)) return control.Name;
        if (!String.IsNullOrEmpty(control.AccessibleName)) return control.AccessibleName;
        if (!String.IsNullOrEmpty(control.Text)) return control.Text.Replace("\r", " ").Replace("\n", " ");
        return control.GetType().Name;
    }
    private static T Field<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Check(bool condition, string description)
    {
        assertions++;
        if (!condition) failures.Add(description);
    }
}
