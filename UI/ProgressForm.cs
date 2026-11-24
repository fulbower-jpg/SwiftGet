using System;
using System.Drawing;
using System.Windows.Forms;

namespace SwiftGet
{
    /// <summary>The per-download window: status, speed, time left and the connection map.</summary>
    class ProgressForm : Form
    {
        readonly DownloadManager mgr;
        readonly DownloadItem item;
        readonly Timer timer = new Timer();
        readonly TextBox txtUrl = new TextBox();
        readonly Label lStatus = new Label(), lSize = new Label(), lDone = new Label(), lRate = new Label(),
                       lLeft = new Label(), lResume = new Label(), lConn = new Label();
        readonly ProgressBar bar = new ProgressBar();
        readonly SegmentMap map = new SegmentMap();
        readonly Button btnToggle = new Button();

        public DownloadItem Item { get { return item; } }

        public ProgressForm(DownloadManager mgr, DownloadItem item)
        {
            this.mgr = mgr;
            this.item = item;

            Font = new Font("Segoe UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 330);
            ShowInTaskbar = true;

            txtUrl.SetBounds(14, 14, 472, 24);
            txtUrl.ReadOnly = true;
            txtUrl.Text = item.Url;

            string[] names = { "Status", "File size", "Downloaded", "Transfer rate", "Time left", "Resume capability", "Connections" };
            Label[] vals = { lStatus, lSize, lDone, lRate, lLeft, lResume, lConn };
            for (int i = 0; i < names.Length; i++)
            {
                var n = new Label { Text = names[i], Left = 14, Top = 50 + i * 21, Width = 130, ForeColor = Color.DimGray };
                vals[i].SetBounds(150, 50 + i * 21, 336, 20);
                vals[i].AutoEllipsis = true;
                Controls.Add(n); Controls.Add(vals[i]);
            }

            bar.SetBounds(14, 205, 472, 20);
            bar.Minimum = 0; bar.Maximum = 1000;
            var lm = new Label { Text = "Start positions and download progress by connections", Left = 14, Top = 232, AutoSize = true, ForeColor = Color.DimGray };
            map.SetBounds(14, 252, 472, 24);
            map.Item = item;

            btnToggle.SetBounds(240, 288, 100, 28);
            var btnHide = new Button { Text = "Hide", Left = 346, Top = 288, Width = 70, Height = 28 };
            var btnOpen = new Button { Text = "Open folder", Left = 14, Top = 288, Width = 100, Height = 28 };
            var btnCancel = new Button { Text = "Close", Left = 422, Top = 288, Width = 64, Height = 28 };
            btnToggle.Click += delegate
            {
                if (item.Status == DownloadStatus.Stopped || item.Status == DownloadStatus.Error) mgr.Start(item);
                else mgr.Stop(item);
            };
            btnHide.Click += delegate { Close(); };
            btnCancel.Click += delegate { Close(); };
            btnOpen.Click += delegate
            {
                if (!string.IsNullOrEmpty(item.SavePath)) CompleteForm.Launch(item.SavePath, true);
            };

            Controls.AddRange(new Control[] { txtUrl, bar, lm, map, btnToggle, btnHide, btnOpen, btnCancel });

            txtUrl.TabStop = false;
            Shown += delegate { ActiveControl = btnToggle; };
            timer.Interval = 300;
            timer.Tick += delegate { UpdateView(); };
            timer.Start();
            FormClosed += delegate { timer.Stop(); timer.Dispose(); };
            UpdateView();
        }

        void UpdateView()
        {
            long total = item.TotalSize, done = item.GetDownloaded();
            double pct = total > 0 ? Math.Min(100.0, done * 100.0 / total) : 0;
            string st;
            switch (item.Status)
            {
                case DownloadStatus.Connecting: st = "Connecting..."; break;
                case DownloadStatus.Downloading: st = "Receiving data..."; break;
                case DownloadStatus.Queued: st = "Waiting in queue"; break;
                case DownloadStatus.Complete: st = "Complete"; break;
                case DownloadStatus.Error: st = "Error: " + item.Error; break;
                default: st = "Stopped"; break;
            }
            lStatus.Text = st;
            lSize.Text = total > 0 ? Fmt.Size(total) + "  (" + total.ToString("N0") + " bytes)" : (item.Probed ? "Unknown" : "...");
            lDone.Text = Fmt.Size(done) + (total > 0 ? "  (" + pct.ToString("0.0") + "%)" : "");
            lRate.Text = item.IsActive ? Fmt.Speed(item.Speed) : "";
            lLeft.Text = item.IsActive && item.Speed > 1 && total > 0 ? Fmt.Time((total - done) / item.Speed) : "";
            lResume.Text = !item.Probed ? "..." : (item.SupportsRange ? "Yes" : "No");
            lConn.Text = item.SupportsRange && item.IsActive ? item.CopySegments().FindAll(s => s.Remaining > 0).Count + " active segments" : "";
            bar.Value = (int)Math.Max(0, Math.Min(1000, pct * 10));
            map.Invalidate();
            Text = (total > 0 ? pct.ToString("0") + "%  " : "") + item.FileName;
            btnToggle.Text = (item.Status == DownloadStatus.Stopped || item.Status == DownloadStatus.Error) ? "Resume" : "Pause";
            btnToggle.Enabled = item.Status != DownloadStatus.Complete;
            if (item.Status == DownloadStatus.Complete) Close();
        }
    }
}
