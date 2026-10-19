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
            ClientSize = new Size(760, 680);
            MinimumSize = new Size(640, 560);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 8, AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 38, 48, 42 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 72, 32, 104, 52 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "確認下載內容", Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
            string scope = preview.IsPlaylist ? "播放清單：" + (preview.PlaylistTitle ?? "未命名清單") : "單一影片";
            layout.Controls.Add(new Label { Text = scope + "\n請勾選要下載的影片；按「全選」可下載整份清單。", Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty }, 0, 1);
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int width in new[] { 78, 92, 142 }) toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
            countLabel.Name = "SelectionCount";
            countLabel.Font = new Font(Font, FontStyle.Bold);
            countLabel.Dock = DockStyle.Fill;
            countLabel.TextAlign = ContentAlignment.MiddleLeft;
            countLabel.Margin = Padding.Empty;
            toolbar.Controls.Add(countLabel, 0, 0);
            var all = MakeButton("SelectAll", "全選", 72);
            var none = MakeButton("SelectNone", "全不選", 86);
            var current = MakeButton("SelectCurrent", "只選目前影片", 136);
            all.Click += delegate { SetSelection(false, true); };
            none.Click += delegate { SetSelection(false, false); };
            current.Click += delegate { SetSelection(true, false); };
            bool hasCurrent = preview.Items.Exists(delegate(PreviewItem item) { return item.IsAvailable && item.Id == preview.CurrentVideoId; });
            current.Visible = hasCurrent && preview.IsPlaylist;
            toolbar.Controls.Add(all, 1, 0);
            toolbar.Controls.Add(none, 2, 0);
            toolbar.Controls.Add(current, 3, 0);
            layout.Controls.Add(toolbar, 0, 2);
            selection.Name = "VideoSelection";
            selection.AccessibleName = "選擇要下載的影片";
            selection.Dock = DockStyle.Fill;
            selection.CheckOnClick = true;
            selection.IntegralHeight = false;
            selection.HorizontalScrollbar = true;
            selection.BackColor = Color.White;
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
                Margin = new Padding(0, 4, 0, 8), AccessibleName = "目前反白影片的完整標題", TabStop = false };
            selection.SelectedIndexChanged += delegate
            {
                if (selection.SelectedIndex < 0) return;
                PreviewItem item = preview.Items[selection.SelectedIndex];
                title.Text = item.Title + (item.IsAvailable ? "" : "\r\n" + item.UnavailableReason);
            };
            layout.Controls.Add(title, 0, 4);
            int unavailable = preview.Items.FindAll(delegate(PreviewItem item) { return !item.IsAvailable; }).Count;
            layout.Controls.Add(new Label { Text = unavailable > 0 ? unavailable + " 項目前無法讀取，已標示且無法勾選。" : "只有勾選的項目會下載；按取消不會下載影音檔案。",
                Dock = DockStyle.Fill, ForeColor = Color.FromArgb(72, 60, 163), Margin = Padding.Empty }, 0, 5);
            var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            details.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            details.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            AddDetail(details, 0, "輸出格式", FormatDescription(request));
            AddDetail(details, 1, "儲存位置", request.OutputDirectory);
            layout.Controls.Add(details, 0, 6);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 0) };
            confirm.Name = "ConfirmDownload";
            confirm.Size = new Size(180, 40);
            confirm.BackColor = Color.FromArgb(91, 73, 218);
            confirm.ForeColor = Color.White;
            confirm.FlatStyle = FlatStyle.Flat;
            confirm.FlatAppearance.BorderSize = 0;
            confirm.Margin = new Padding(10, 0, 0, 0);
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
            cancel.Height = 40;
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(confirm);
            actions.Controls.Add(cancel);
            layout.Controls.Add(actions, 0, 7);
            AcceptButton = cancel;
            CancelButton = cancel;
            SetSelection(hasCurrent, !hasCurrent);
            if (selection.Items.Count > 0)
                selection.SelectedIndex = hasCurrent ? preview.Items.FindIndex(delegate(PreviewItem item) { return item.IsAvailable && item.Id == preview.CurrentVideoId; }) : 0;
            Shown += delegate { cancel.Focus(); };
            ResumeLayout(true);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Height = Math.Min(Height, area.Height - 24);
            Width = Math.Min(Width, area.Width - 24);
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
            var button = new Button { Name = name, Text = text, Size = new Size(width, 34), FlatStyle = FlatStyle.Flat,
                BackColor = Color.White, Margin = new Padding(3, 2, 3, 2) };
            button.FlatAppearance.BorderColor = Color.FromArgb(216, 221, 231);
            return button;
        }

        private static void AddDetail(TableLayoutPanel parent, int row, string name, string value)
        {
            parent.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty,
                ForeColor = Color.FromArgb(102, 112, 132) }, 0, row);
            parent.Controls.Add(new TextBox { Text = value, ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(246, 247, 251), Margin = new Padding(0, 17, 0, 0), AccessibleName = name, TabStop = false }, 1, row);
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
