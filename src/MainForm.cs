using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SimpleDownloader
{
    public sealed class MainForm : Form
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, string text);
        private static readonly Color Ink = Color.FromArgb(30, 37, 54);
        private static readonly Color Muted = Color.FromArgb(102, 112, 132);
        private static readonly Color Accent = Color.FromArgb(91, 73, 218);
        private static readonly Color Canvas = Color.FromArgb(246, 247, 251);
        private readonly TextBox urlBox = new TextBox();
        private readonly TextBox folderBox = new TextBox();
        private readonly ComboBox formatBox = new ComboBox();
        private readonly ComboBox qualityBox = new ComboBox();
        private readonly RadioButton videoChoice = new RadioButton();
        private readonly RadioButton audioChoice = new RadioButton();
        private readonly Button startButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly Button installButton = new Button();
        private readonly Button pasteButton = new Button();
        private readonly Button browseButton = new Button();
        private readonly Button resetFolderButton = new Button();
        private readonly Button openButton = new Button();
        private readonly Label formatHint = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Label detailLabel = new Label();
        private readonly Label toolsLabel = new Label();
        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly TextBox logBox = new TextBox();
        private readonly LinkLabel logToggle = new LinkLabel();
        private readonly TableLayoutPanel root = new TableLayoutPanel();
        private readonly ToolInstaller installer;
        private readonly DownloadService downloader;
        private CancellationTokenSource cancellation;
        private bool busy;
        private bool closeWhenDone;
        private string lastOutputDirectory;

        public MainForm()
        {
            SuspendLayout();
            Text = "影片與音樂下載器";
            Font = new Font("Microsoft JhengHei UI", 10F);
            ForeColor = Ink;
            BackColor = Canvas;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(820, 755);
            MinimumSize = new Size(720, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            string toolPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleDownloader", "tools");
            installer = new ToolInstaller(toolPath);
            downloader = new DownloadService(toolPath);
            BuildInterface();
            folderBox.Text = DownloadService.GetDownloadsDirectory();
            lastOutputDirectory = folderBox.Text;
            RefreshTools();
            FormClosing += OnClosing;
            ResumeLayout(true);
            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            Height = Math.Min(Height, workingArea.Height - ScalePixels(24));
            Width = Math.Min(Width, workingArea.Width - ScalePixels(24));
        }

        private void BuildInterface()
        {
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(30, 20, 30, 16);
            root.ColumnCount = 1;
            root.RowCount = 8;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int[] heights = { 82, 110, 147, 113, 62, 106, 0, 64 };
            foreach (int height in heights) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.AutoScroll = true;
            Controls.Add(root);

            var heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var title = LabelAt("影片與音樂下載器", 0, 0, 480, 34, 21F, true);
            heading.Controls.Add(title);
            heading.Controls.Add(LabelAt("貼上網址，選擇格式，把喜歡的內容存到電腦。", 1, 44, 640, 24, 10F, false, Muted));
            var mark = LabelAt("yt-dlp", 0, 6, 100, 28, 11F, true, Accent);
            mark.Dock = DockStyle.Right;
            mark.TextAlign = ContentAlignment.TopRight;
            heading.Controls.Add(mark);
            root.Controls.Add(heading, 0, 0);

            var urlCard = Card();
            urlCard.Controls.Add(LabelAt("01  貼上影片或播放清單網址", 18, 13, 420, 24, 10F, true));
            ConfigureText(urlBox, "影片網址");
            urlBox.HandleCreated += delegate { SendMessage(urlBox.Handle, 0x1501, IntPtr.Zero, "https://www.youtube.com/watch?v=…"); };
            var urlRow = new TableLayoutPanel { Location = new Point(18, 49), Height = 38, Width = 700, ColumnCount = 2 };
            urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            urlBox.Dock = DockStyle.Fill;
            urlBox.Margin = new Padding(0, 4, 10, 0);
            ConfigureButton(pasteButton, "貼上", false);
            pasteButton.Dock = DockStyle.Fill;
            pasteButton.Margin = Padding.Empty;
            pasteButton.Click += delegate { PasteUrl(); };
            urlRow.Controls.Add(urlBox, 0, 0);
            urlRow.Controls.Add(pasteButton, 1, 0);
            urlCard.Controls.Add(urlRow);
            urlCard.Layout += delegate { urlRow.Width = urlCard.ClientSize.Width - 2 * urlRow.Left; };
            root.Controls.Add(urlCard, 0, 1);

            var options = Card();
            options.Controls.Add(LabelAt("02  選擇下載格式", 18, 13, 190, 24, 10F, true));
            videoChoice.Text = "影片";
            audioChoice.Text = "音樂";
            videoChoice.SetBounds(220, 12, 86, 27);
            audioChoice.SetBounds(312, 12, 86, 27);
            videoChoice.Checked = true;
            videoChoice.CheckedChanged += delegate { if (videoChoice.Checked) PopulateFormats(); };
            audioChoice.CheckedChanged += delegate { if (audioChoice.Checked) PopulateFormats(); };
            options.Controls.Add(videoChoice);
            options.Controls.Add(audioChoice);
            var choiceRow = new TableLayoutPanel { Location = new Point(18, 49), Height = 37, Width = 700, ColumnCount = 4 };
            choiceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
            choiceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            choiceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            choiceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            choiceRow.Controls.Add(new Label { Text = "格式", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            choiceRow.Controls.Add(formatBox, 1, 0);
            choiceRow.Controls.Add(new Label { Text = "品質", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter }, 2, 0);
            choiceRow.Controls.Add(qualityBox, 3, 0);
            foreach (ComboBox box in new[] { formatBox, qualityBox })
            {
                box.DropDownStyle = ComboBoxStyle.DropDownList;
                box.Dock = DockStyle.Fill;
                box.Margin = new Padding(0, 3, 0, 0);
                box.FlatStyle = FlatStyle.Standard;
            }
            formatBox.AccessibleName = "下載格式";
            qualityBox.AccessibleName = "影片畫質上限";
            formatBox.SelectedIndexChanged += delegate { UpdateFormatHint(); };
            options.Controls.Add(choiceRow);
            formatHint.SetBounds(18, 99, 700, 27);
            formatHint.ForeColor = Muted;
            formatHint.Font = new Font(Font.FontFamily, 9F);
            options.Controls.Add(formatHint);
            options.Layout += delegate { choiceRow.Width = options.ClientSize.Width - 2 * choiceRow.Left; formatHint.Width = choiceRow.Width; };
            PopulateFormats();
            root.Controls.Add(options, 0, 2);

            var destination = Card();
            destination.Controls.Add(LabelAt("03  儲存位置", 18, 13, 180, 24, 10F, true));
            ConfigureButton(resetFolderButton, "預設下載資料夾", false);
            resetFolderButton.FlatAppearance.BorderSize = 0;
            resetFolderButton.ForeColor = Accent;
            resetFolderButton.SetBounds(200, 10, 154, 29);
            resetFolderButton.Click += delegate { folderBox.Text = DownloadService.GetDownloadsDirectory(); };
            destination.Controls.Add(resetFolderButton);
            var folderRow = new TableLayoutPanel { Location = new Point(18, 49), Height = 38, Width = 700, ColumnCount = 2 };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            ConfigureText(folderBox, "儲存資料夾");
            folderBox.Dock = DockStyle.Fill;
            folderBox.Margin = new Padding(0, 4, 10, 0);
            ConfigureButton(browseButton, "瀏覽…", false);
            browseButton.Dock = DockStyle.Fill;
            browseButton.Margin = Padding.Empty;
            browseButton.Click += delegate { BrowseFolder(); };
            folderRow.Controls.Add(folderBox, 0, 0);
            folderRow.Controls.Add(browseButton, 1, 0);
            destination.Controls.Add(folderRow);
            destination.Layout += delegate { folderRow.Width = destination.ClientSize.Width - 2 * folderRow.Left; };
            root.Controls.Add(destination, 0, 3);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 10), ColumnCount = 3 };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
            ConfigureButton(startButton, "開始下載", true);
            ConfigureButton(cancelButton, "取消", false);
            ConfigureButton(openButton, "開啟資料夾", false);
            startButton.Dock = cancelButton.Dock = openButton.Dock = DockStyle.Fill;
            startButton.Margin = new Padding(0, 0, 10, 0);
            cancelButton.Margin = new Padding(0, 0, 10, 0);
            openButton.Margin = Padding.Empty;
            cancelButton.Enabled = false;
            startButton.Click += async delegate { await StartDownload(); };
            cancelButton.Click += delegate { CancelWork(); };
            openButton.Click += delegate { OpenFolder(); };
            actions.Controls.Add(startButton, 0, 0);
            actions.Controls.Add(cancelButton, 1, 0);
            actions.Controls.Add(openButton, 2, 0);
            root.Controls.Add(actions, 0, 4);
            AcceptButton = startButton;

            var progressPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
            statusLabel.SetBounds(0, 7, 690, 25);
            statusLabel.Font = new Font(Font, FontStyle.Bold);
            statusLabel.Text = "準備就緒";
            statusLabel.AutoEllipsis = true;
            progressPanel.Controls.Add(statusLabel);
            progressBar.SetBounds(0, 40, 740, 9);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressPanel.Controls.Add(progressBar);
            detailLabel.SetBounds(0, 60, 590, 34);
            detailLabel.ForeColor = Muted;
            detailLabel.Font = new Font(Font.FontFamily, 9F);
            detailLabel.Text = "首次下載會自動安裝所需工具，之後即可直接使用。";
            detailLabel.AutoEllipsis = true;
            progressPanel.Controls.Add(detailLabel);
            logToggle.Text = "顯示詳細紀錄";
            logToggle.LinkColor = Accent;
            logToggle.ActiveLinkColor = Accent;
            logToggle.SetBounds(610, 60, 130, 25);
            logToggle.TextAlign = ContentAlignment.TopRight;
            logToggle.LinkClicked += delegate { ToggleLog(); };
            progressPanel.Controls.Add(logToggle);
            progressPanel.Layout += delegate
            {
                progressBar.Width = progressPanel.ClientSize.Width;
                statusLabel.Width = progressPanel.ClientSize.Width;
                detailLabel.Width = Math.Max(200, progressPanel.ClientSize.Width - logToggle.Width - ScalePixels(15));
                logToggle.Left = progressPanel.ClientSize.Width - logToggle.Width;
            };
            root.Controls.Add(progressPanel, 0, 5);

            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BackColor = Color.White;
            logBox.Font = new Font("Consolas", 9F);
            logBox.Dock = DockStyle.Fill;
            logBox.Visible = false;
            logBox.AccessibleName = "下載詳細紀錄";
            root.Controls.Add(logBox, 0, 6);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, Padding = new Padding(0, 12, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            toolsLabel.Dock = DockStyle.Top;
            toolsLabel.Height = 46;
            toolsLabel.ForeColor = Muted;
            toolsLabel.Font = new Font(Font.FontFamily, 9F);
            toolsLabel.TextAlign = ContentAlignment.MiddleLeft;
            toolsLabel.AutoEllipsis = true;
            ConfigureButton(installButton, "安裝下載工具", false);
            installButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            installButton.Size = new Size(170, 38);
            installButton.Margin = new Padding(0, 4, 0, 4);
            installButton.Click += async delegate { await InstallTools(); };
            footer.Controls.Add(toolsLabel, 0, 0);
            footer.Controls.Add(installButton, 1, 0);
            root.Controls.Add(footer, 0, 7);
        }

        private Panel Card()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 0, 10) };
            panel.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Color.FromArgb(226, 229, 238)))
                    e.Graphics.DrawRectangle(pen, 0, 0, panel.ClientSize.Width - 1, panel.ClientSize.Height - 1);
            };
            return panel;
        }

        private static Label LabelAt(string text, int x, int y, int w, int h, float size, bool bold, Color? color = null)
        {
            return new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h), ForeColor = color ?? Ink,
                Font = new Font("Microsoft JhengHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular), AutoEllipsis = true };
        }

        private void ConfigureText(TextBox box, string accessibleName)
        {
            box.Font = new Font(Font.FontFamily, 11F);
            box.BorderStyle = BorderStyle.FixedSingle;
            box.AccessibleName = accessibleName;
        }

        private static void ConfigureButton(Button button, string text, bool primary)
        {
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(216, 221, 231);
            button.BackColor = primary ? Accent : Color.White;
            button.ForeColor = primary ? Color.White : Ink;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        private void PopulateFormats()
        {
            if (formatBox == null) return;
            formatBox.Items.Clear();
            qualityBox.Items.Clear();
            if (videoChoice.Checked)
            {
                formatBox.Items.Add(new FormatOption("MP4  ·  常用影片", MediaFormat.Mp4));
                formatBox.Items.Add(new FormatOption("MKV  ·  保留最佳畫質", MediaFormat.Mkv));
                qualityBox.Items.AddRange(new object[] { new QualityOption("最高可用畫質", 0), new QualityOption("4K（2160p）", 2160), new QualityOption("1440p", 1440), new QualityOption("1080p", 1080), new QualityOption("720p", 720) });
                qualityBox.SelectedIndex = 3;
            }
            else
            {
                formatBox.Items.Add(new FormatOption("MP3  ·  通用音樂格式", MediaFormat.Mp3));
                formatBox.Items.Add(new FormatOption("M4A  ·  AAC 音訊", MediaFormat.M4a));
                formatBox.Items.Add(new FormatOption("WAV  ·  未壓縮音訊", MediaFormat.Wav));
                formatBox.Items.Add(new FormatOption("原始音訊  ·  不轉檔", MediaFormat.OriginalAudio));
                qualityBox.Items.Add(new QualityOption("最佳來源音質", 0));
                qualityBox.SelectedIndex = 0;
            }
            qualityBox.Enabled = videoChoice.Checked && !busy;
            formatBox.SelectedIndex = 0;
        }

        private void UpdateFormatHint()
        {
            var option = formatBox.SelectedItem as FormatOption;
            if (option == null) return;
            switch (option.Value)
            {
                case MediaFormat.Mp4: formatHint.Text = "優先選用相容格式；必要時轉成 MP4，轉檔可能需要一些時間。"; break;
                case MediaFormat.Mkv: formatHint.Text = "依畫質上限選取影音並合併；原始影片畫質較低時使用可用畫質。"; break;
                case MediaFormat.Mp3: formatHint.Text = "擷取最佳可用音訊，再轉成高音質 MP3。"; break;
                case MediaFormat.M4a: formatHint.Text = "擷取音訊並輸出為 M4A，適合手機與音樂播放器。"; break;
                case MediaFormat.Wav: formatHint.Text = "輸出未壓縮 WAV，檔案較大；音質仍以原始來源為限。"; break;
                default: formatHint.Text = "保留來源音訊格式（例如 WebM 或 M4A），不重新編碼。"; break;
            }
        }

        private async Task StartDownload()
        {
            if (busy) return;
            var request = new DownloadRequest {
                Url = urlBox.Text.Trim(), OutputDirectory = folderBox.Text.Trim(),
                Format = ((FormatOption)formatBox.SelectedItem).Value,
                MaxHeight = ((QualityOption)qualityBox.SelectedItem).Value
            };
            try { DownloadService.ValidateRequest(request); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "請檢查輸入內容", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            lastOutputDirectory = request.OutputDirectory;
            int selectedCount = 0;
            int completedCount = 0;
            var failedTitles = new List<string>();
            await RunOperation(async delegate(IProgress<DownloadUpdate> report, CancellationToken token)
            {
                await installer.EnsureInstalledAsync(report, token, false);
                token.ThrowIfCancellationRequested();
                DownloadPreview preview = await downloader.PreviewAsync(request, report, token);
                token.ThrowIfCancellationRequested();
                statusLabel.Text = "等待確認下載內容";
                detailLabel.Text = "確認影片標題與數量後，才會開始下載。";
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 0;
                List<PreviewItem> selected;
                using (var dialog = new DownloadConfirmForm(preview, request))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) throw new DownloadNotConfirmedException();
                    selected = new List<PreviewItem>(dialog.SelectedItems);
                }
                token.ThrowIfCancellationRequested();
                if (selected.Count == 0) throw new DownloadNotConfirmedException();
                selectedCount = selected.Count;
                AppendLog("已確認下載 " + selectedCount + " 部影片。");
                for (int index = 0; index < selected.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    PreviewItem item = selected[index];
                    int itemNumber = index + 1;
                    AppendLog("[" + itemNumber + "/" + selectedCount + "] " + item.Title);
                    // A frozen URL and identity prevent playlist edits from changing the user's selection.
                    var itemRequest = new DownloadRequest { Url = item.Url, ExpectedId = item.Id,
                        OutputDirectory = request.OutputDirectory, Format = request.Format, MaxHeight = request.MaxHeight };
                    var itemProgress = new ForwardProgress(delegate(DownloadUpdate update)
                    {
                        report.Report(new DownloadUpdate {
                            Status = String.IsNullOrWhiteSpace(update.Status) ? null : "第 " + itemNumber + "/" + selectedCount + " 部 · " + update.Status,
                            Detail = update.Detail, FilePath = update.FilePath, Percent = update.Percent
                        });
                    });
                    try
                    {
                        await downloader.DownloadAsync(itemRequest, itemProgress, token);
                        completedCount++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        failedTitles.Add(item.Title);
                        AppendLog("下載失敗：" + item.Title + Environment.NewLine + ex.Message);
                    }
                }
                if (completedCount == 0)
                    throw new InvalidOperationException("所選的 " + selectedCount + " 部影片皆未能完成下載，請查看詳細紀錄。" + Environment.NewLine + String.Join(Environment.NewLine, failedTitles.ToArray()));
                lastOutputDirectory = request.OutputDirectory;
            }, "下載完成", "檔案已儲存，可以按「開啟資料夾」查看。", delegate
            {
                return new[] {
                    failedTitles.Count == 0 ? "下載完成（" + completedCount + "/" + selectedCount + " 部）" : "下載結束：完成 " + completedCount + " 部，失敗 " + failedTitles.Count + " 部",
                    failedTitles.Count == 0 ? "檔案已儲存，可以按「開啟資料夾」查看。" : "已完成的檔案已保留；詳細紀錄列有失敗項目，可重新勾選後重試。"
                };
            });
        }

        private async Task InstallTools()
        {
            if (busy) return;
            await RunOperation(async delegate(IProgress<DownloadUpdate> report, CancellationToken token)
            {
                await installer.EnsureInstalledAsync(report, token, true);
            }, "工具已準備完成", "現在可以貼上網址並開始下載。");
        }

        private async Task RunOperation(Func<IProgress<DownloadUpdate>, CancellationToken, Task> action, string success, string successDetail, Func<string[]> completion = null)
        {
            SetBusy(true);
            logBox.Clear();
            statusLabel.Text = "正在準備…";
            detailLabel.Text = "";
            progressBar.Style = ProgressBarStyle.Marquee;
            cancellation = new CancellationTokenSource();
            var current = cancellation;
            var report = new Progress<DownloadUpdate>(delegate(DownloadUpdate update)
            {
                if (IsDisposed || !busy || cancellation != current) return;
                if (!String.IsNullOrWhiteSpace(update.Status)) statusLabel.Text = update.Status;
                if (!String.IsNullOrWhiteSpace(update.Detail))
                {
                    detailLabel.Text = update.Detail;
                    AppendLog(update.Detail);
                }
                if (!String.IsNullOrWhiteSpace(update.FilePath)) AppendLog("儲存檔案：" + update.FilePath);
                if (update.Percent.HasValue)
                {
                    progressBar.Style = ProgressBarStyle.Continuous;
                    progressBar.Value = (int)Math.Max(0, Math.Min(100, update.Percent.Value));
                }
                else if (!String.IsNullOrWhiteSpace(update.Status)) progressBar.Style = ProgressBarStyle.Marquee;
            });
            try
            {
                await action(report, current.Token);
                current.Token.ThrowIfCancellationRequested();
                if (completion != null)
                {
                    string[] outcome = completion();
                    success = outcome[0];
                    successDetail = outcome[1];
                }
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 100;
                statusLabel.Text = success;
                detailLabel.Text = successDetail;
                AppendLog(success);
            }
            catch (DownloadNotConfirmedException)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 0;
                statusLabel.Text = "已取消下載";
                detailLabel.Text = "尚未下載任何影片或音樂，可以修改網址後重試。";
                AppendLog("使用者取消確認，未開始下載。");
            }
            catch (OperationCanceledException)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 0;
                statusLabel.Text = "已取消";
                detailLabel.Text = "已完成的檔案會保留；未完成的下載可稍後重試。";
                AppendLog("操作已取消。");
            }
            catch (Exception ex)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 0;
                statusLabel.Text = "操作未完成";
                detailLabel.Text = "請查看詳細紀錄；可更新工具後重試。";
                AppendLog(ex.Message);
                if (!closeWhenDone)
                {
                    string message = ex.Message;
                    if (message.Length > 1400) message = message.Substring(message.Length - 1400);
                    MessageBox.Show(this, message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                cancellation = null;
                current.Dispose();
                SetBusy(false);
                RefreshTools();
                if (closeWhenDone) Close();
            }
        }

        private void SetBusy(bool value)
        {
            busy = value;
            foreach (Control control in new Control[] { urlBox, folderBox, pasteButton, browseButton, resetFolderButton, formatBox, videoChoice, audioChoice, startButton, installButton }) control.Enabled = !value;
            qualityBox.Enabled = !value && videoChoice.Checked;
            cancelButton.Enabled = value;
            startButton.Text = value ? "處理中…" : "開始下載";
        }

        private void RefreshTools()
        {
            toolsLabel.Text = installer.IsReady ? "●  下載工具已就緒" : "○  尚未備妥工具 · 首次使用會自動安裝";
            installButton.Text = installer.IsReady ? "更新下載工具" : "安裝下載工具";
        }

        private void AppendLog(string message)
        {
            if (logBox.TextLength > 60000) logBox.Text = logBox.Text.Substring(logBox.TextLength - 35000);
            logBox.AppendText(message + Environment.NewLine);
        }

        private void ToggleLog()
        {
            bool show = !logBox.Visible;
            logBox.Visible = show;
            int logHeight = ScalePixels(150);
            root.RowStyles[6].Height = show ? logHeight : 0;
            logToggle.Text = show ? "隱藏詳細紀錄" : "顯示詳細紀錄";
            if (show && WindowState == FormWindowState.Normal)
                Height = Math.Min(Height + logHeight, Screen.FromControl(this).WorkingArea.Height - ScalePixels(24));
            else if (!show && WindowState == FormWindowState.Normal) Height = Math.Max(MinimumSize.Height, Height - logHeight);
        }

        private int ScalePixels(int value)
        {
            return Math.Max(1, (int)Math.Round(value * root.Padding.Left / 30.0));
        }

        private void PasteUrl()
        {
            try { if (Clipboard.ContainsText()) { urlBox.Text = Clipboard.GetText().Trim(); urlBox.Focus(); } }
            catch (Exception ex) { MessageBox.Show(this, "無法讀取剪貼簿：" + ex.Message, Text); }
        }

        private void BrowseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "選擇下載檔案的儲存位置", ShowNewFolderButton = true })
            {
                if (Directory.Exists(folderBox.Text)) dialog.SelectedPath = folderBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) folderBox.Text = dialog.SelectedPath;
            }
        }

        private void OpenFolder()
        {
            string folder = busy ? lastOutputDirectory : folderBox.Text.Trim();
            try
            {
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("資料夾尚不存在，完成下載後即可開啟。");
                Process.Start(new ProcessStartInfo { FileName = Path.GetFullPath(folder), UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "無法開啟資料夾", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }

        private void CancelWork()
        {
            if (cancellation == null) return;
            cancelButton.Enabled = false;
            statusLabel.Text = "正在取消…";
            cancellation.Cancel();
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!busy) return;
            closeWhenDone = true;
            e.Cancel = true;
            CancelWork();
        }

        private sealed class FormatOption
        {
            public readonly string Name; public readonly MediaFormat Value;
            public FormatOption(string name, MediaFormat value) { Name = name; Value = value; }
            public override string ToString() { return Name; }
        }
        private sealed class DownloadNotConfirmedException : Exception { }
        private sealed class ForwardProgress : IProgress<DownloadUpdate>
        {
            private readonly Action<DownloadUpdate> callback;
            public ForwardProgress(Action<DownloadUpdate> callback) { this.callback = callback; }
            public void Report(DownloadUpdate update) { callback(update); }
        }
        private sealed class QualityOption
        {
            public readonly string Name; public readonly int Value;
            public QualityOption(string name, int value) { Name = name; Value = value; }
            public override string ToString() { return Name; }
        }
    }
}
