using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SimpleDownloader
{
    public sealed class DownloadConfirmForm : Form
    {
        private readonly CheckedListBox selection = new CheckedListBox();
        private readonly Label countLabel = new Label();
        private readonly Button confirm = new Button();
        private readonly DownloadPreview preview;
        public List<PreviewItem> SelectedItems { get; private set; }

        public DownloadConfirmForm(DownloadPreview preview, DownloadRequest request)
        {
            if (preview == null || preview.Items == null || preview.Items.Count == 0)
                throw new ArgumentException("無法確認要下載的影片，請重新讀取網址。");
            if (request == null) throw new ArgumentNullException("request");
            this.preview = preview;
            SelectedItems = new List<PreviewItem>();
            SuspendLayout();
            Text = "確認下載內容";
            Name = "DownloadConfirmForm";
            Font = new Font("Microsoft JhengHei UI", 10F);
            ForeColor = Color.FromArgb(30, 37, 54);
            BackColor = Color.FromArgb(246, 247, 251);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(760, 700);
            MinimumSize = new Size(440, 360);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;

            // Keep the action buttons visible while the content can scroll on
            // smaller/high-DPI displays. Content rows grow from their controls'
            // preferred size instead of assuming a particular font or DPI.
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20),
                ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(shell);
            var viewport = new Panel { Name = "ConfirmationContent", Dock = DockStyle.Fill,
                AutoScroll = true, Margin = Padding.Empty };
            shell.Controls.Add(viewport, 0, 0);
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 0, 8, 0), ColumnCount = 1, RowCount = 7, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < 3; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
            for (int row = 4; row < 7; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            viewport.Controls.Add(layout);
            bool listLayoutQueued = false;
            EventHandler fitList = delegate
            {
                if (listLayoutQueued || !layout.IsHandleCreated || layout.IsDisposed) return;
                listLayoutQueued = true;
                layout.BeginInvoke(new MethodInvoker(delegate
                {
                    listLayoutQueued = false;
                    if (layout.IsDisposed || viewport.IsDisposed) return;
                    // Measure after AutoSize and DPI scaling have settled.
                    // A percent row inside an AutoSize table can repeatedly
                    // enlarge a ListBox; use the available pixels explicitly.
                    int[] rows = layout.GetRowHeights();
                    int otherHeight = layout.Padding.Vertical;
                    for (int row = 0; row < rows.Length; row++)
                        if (row != 3) otherHeight += rows[row];
                    int listHeight = Math.Max(selection.MinimumSize.Height + selection.Margin.Vertical,
                        viewport.ClientSize.Height - otherHeight);
                    if (Math.Abs(layout.RowStyles[3].Height - listHeight) > 0.5F)
                        layout.RowStyles[3].Height = listHeight;
                }));
            };
            viewport.SizeChanged += fitList;
            layout.Layout += delegate { fitList(layout, EventArgs.Empty); };
            layout.Controls.Add(new Label { Text = "確認下載內容", Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
                AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) }, 0, 0);
            string scope = preview.IsPlaylist ? "播放清單：" + (preview.PlaylistTitle ?? "未命名清單") : "單一影片";
            layout.Controls.Add(new Label { Text = scope + "\n請勾選要下載的影片；按「全選」可下載整份清單。",
                AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) }, 0, 1);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = Padding.Empty };
            countLabel.Name = "SelectionCount";
            countLabel.Font = new Font(Font, FontStyle.Bold);
            countLabel.AutoSize = true;
            countLabel.Anchor = AnchorStyles.Left;
            countLabel.TextAlign = ContentAlignment.MiddleLeft;
            countLabel.Padding = new Padding(0, 1, 0, 1);
            countLabel.Margin = new Padding(0, 8, 16, 8);
            toolbar.Controls.Add(countLabel);
            var all = MakeButton("SelectAll", "全選", 72);
            var none = MakeButton("SelectNone", "全不選", 86);
            var current = MakeButton("SelectCurrent", "只選目前影片", 136);
            all.Click += delegate { SetSelection(false, true); };
            none.Click += delegate { SetSelection(false, false); };
            current.Click += delegate { SetSelection(true, false); };
            bool hasCurrent = preview.Items.Exists(delegate(PreviewItem item) { return item.IsAvailable && item.Id == preview.CurrentVideoId; });
            current.Visible = hasCurrent && preview.IsPlaylist;
            toolbar.Controls.Add(all);
            toolbar.Controls.Add(none);
            toolbar.Controls.Add(current);
            layout.Controls.Add(toolbar, 0, 2);
            selection.Name = "VideoSelection";
            selection.AccessibleName = "選擇要下載的影片";
            selection.Dock = DockStyle.Fill;
            selection.CheckOnClick = true;
            selection.IntegralHeight = false;
            selection.HorizontalScrollbar = true;
            selection.BackColor = Color.White;
            selection.MinimumSize = new Size(0, 144);
            selection.Margin = new Padding(0, 4, 0, 6);
            for (int i = 0; i < preview.Items.Count; i++) selection.Items.Add(new DisplayItem(i + 1, preview.Items[i]));
            selection.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (!preview.Items[e.Index].IsAvailable) e.NewValue = CheckState.Unchecked;
                UpdateCount(e.Index, e.NewValue);
            };
            layout.Controls.Add(selection, 0, 3);
            var title = new TextBox { Name = "VideoTitle", ReadOnly = true, Multiline = true, WordWrap = true,
                ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                MinimumSize = new Size(0, 64), Margin = new Padding(0, 4, 0, 8), AccessibleName = "目前反白影片的完整標題", TabStop = false };
            selection.SelectedIndexChanged += delegate
            {
                if (selection.SelectedIndex < 0) return;
                PreviewItem item = preview.Items[selection.SelectedIndex];
                title.Text = item.Title + (item.IsAvailable ? "" : "\r\n" + item.UnavailableReason);
            };
            layout.Controls.Add(title, 0, 4);
            int unavailable = preview.Items.FindAll(delegate(PreviewItem item) { return !item.IsAvailable; }).Count;
            layout.Controls.Add(new Label { Text = unavailable > 0 ? unavailable + " 項目前無法讀取，已標示且無法勾選。" : "只有勾選的項目會下載；按取消不會下載影音檔案。",
                AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(72, 60, 163), Margin = new Padding(0, 0, 0, 10) }, 0, 5);
            var details = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            AddDetail(details, 0, "輸出格式", FormatDescription(request));
            AddDetail(details, 1, "儲存位置", request.OutputDirectory);
            layout.Controls.Add(details, 0, 6);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Margin = Padding.Empty, Padding = new Padding(0, 12, 0, 0) };
            confirm.Name = "ConfirmDownload";
            confirm.AutoSize = true;
            confirm.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            confirm.MinimumSize = new Size(180, 40);
            confirm.Padding = new Padding(12, 6, 12, 6);
            confirm.BackColor = Color.FromArgb(91, 73, 218);
            confirm.ForeColor = Color.White;
            confirm.FlatStyle = FlatStyle.Flat;
            confirm.FlatAppearance.BorderSize = 0;
            confirm.Margin = new Padding(10, 0, 0, 4);
            confirm.Click += delegate
            {
                SelectedItems.Clear();
                foreach (int index in selection.CheckedIndices)
                {
                    PreviewItem item = preview.Items[index];
                    if (item.IsAvailable) SelectedItems.Add(new PreviewItem { Title = item.Title, Url = item.Url, Id = item.Id, IsAvailable = true });
                }
                if (SelectedItems.Count == 0) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            var cancel = MakeButton("CancelDownload", "取消", 106);
            cancel.MinimumSize = new Size(106, 40);
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(confirm);
            actions.Controls.Add(cancel);
            shell.Controls.Add(actions, 0, 1);
            AcceptButton = cancel;
            CancelButton = cancel;
            SetSelection(hasCurrent, !hasCurrent);
            if (selection.Items.Count > 0)
                selection.SelectedIndex = hasCurrent ? preview.Items.FindIndex(delegate(PreviewItem item) { return item.IsAvailable && item.Id == preview.CurrentVideoId; }) : 0;
            Shown += delegate
            {
                FitToWorkingArea();
                cancel.Focus();
            };
            DpiChanged += delegate
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (!IsDisposed) FitToWorkingArea();
                }));
            };
            ResumeLayout(true);
        }

        protected override void OnLoad(EventArgs e)
        {
            // AutoScale has run by this point. Clamp the actual pixel size,
            // including the scaled minimum, to the monitor that owns the form.
            FitToWorkingArea();
            base.OnLoad(e);
        }

        private void FitToWorkingArea()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int width = Math.Max(1, area.Width - 24);
            int height = Math.Max(1, area.Height - 24);
            MinimumSize = new Size(Math.Min(MinimumSize.Width, width), Math.Min(MinimumSize.Height, height));
            Size = new Size(Math.Min(Width, width), Math.Min(Height, height));
            Location = new Point(Math.Max(area.Left + 12, Math.Min(Left, area.Right - Width - 12)),
                Math.Max(area.Top + 12, Math.Min(Top, area.Bottom - Height - 12)));
        }

        private void SetSelection(bool onlyCurrent, bool all)
        {
            for (int i = 0; i < preview.Items.Count; i++)
            {
                PreviewItem item = preview.Items[i];
                selection.SetItemChecked(i, item.IsAvailable && (all || (onlyCurrent && item.Id == preview.CurrentVideoId)));
            }
            UpdateCount(-1, CheckState.Unchecked);
        }

        private void UpdateCount(int changedIndex, CheckState newState)
        {
            int count = selection.CheckedItems.Count;
            if (changedIndex >= 0)
            {
                if (selection.GetItemChecked(changedIndex)) count--;
                if (newState == CheckState.Checked) count++;
            }
            countLabel.Text = "已選 " + count + " / " + preview.Items.Count + " 部影片";
            confirm.Text = "確認下載（" + count + " 部）";
            confirm.Enabled = count > 0;
        }

        private static Button MakeButton(string name, string text, int width)
        {
            var button = new Button { Name = name, Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(width, 34), Padding = new Padding(8, 4, 8, 4), FlatStyle = FlatStyle.Flat,
                BackColor = Color.White, Margin = new Padding(3, 2, 3, 2) };
            button.FlatAppearance.BorderColor = Color.FromArgb(216, 221, 231);
            return button;
        }

        private static void AddDetail(TableLayoutPanel parent, int row, string name, string value)
        {
            parent.Controls.Add(new Label { Text = name, AutoSize = true, Anchor = AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 4, 14, 4),
                ForeColor = Color.FromArgb(102, 112, 132) }, 0, row);
            parent.Controls.Add(new TextBox { Text = value, ReadOnly = true, BorderStyle = BorderStyle.None,
                Anchor = AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(246, 247, 251),
                Margin = new Padding(0, 4, 0, 4), AccessibleName = name, TabStop = false }, 1, row);
        }

        private static string FormatDescription(DownloadRequest request)
        {
            if (request.Format == MediaFormat.OriginalAudio) return "原始音訊（不轉檔）";
            string format = request.Format.ToString().ToUpperInvariant();
            if (request.Format == MediaFormat.Mp4 || request.Format == MediaFormat.Mkv)
                format += request.MaxHeight == 0 ? " · 最高可用畫質" : " · 最高 " + request.MaxHeight + "p";
            return format;
        }

        private sealed class DisplayItem
        {
            private readonly int number;
            private readonly PreviewItem item;
            public DisplayItem(int number, PreviewItem item) { this.number = number; this.item = item; }
            public override string ToString() { return number + ". " + (item.IsAvailable ? "" : "[無法下載] ") + item.Title.Replace('\r', ' ').Replace('\n', ' '); }
        }
    }
}
