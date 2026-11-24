using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Threading;
using System.Windows.Forms;

namespace SwiftGet
{
    public class MainForm : Form
    {
        static readonly Regex LinkRegex = new Regex(
            @"^https?://\S+\.(zip|rar|7z|gz|tgz|tar|bz2|xz|iso|exe|msi|apk|dmg|jar|pdf|docx?|xlsx?|pptx?|epub|mp3|flac|wav|m4a|mp4|mkv|avi|mov|wmv|webm|img|bin)(\?\S*)?$",
            RegexOptions.IgnoreCase);

        readonly DownloadManager mgr = new DownloadManager();
        readonly Dictionary<string, ListViewItem> rows = new Dictionary<string, ListViewItem>();
        readonly Dictionary<string, ProgressForm> progress = new Dictionary<string, ProgressForm>();

        MenuStrip menu;
        ToolStrip toolbar;
        SplitContainer split;
        TreeView tree;
        DoubleBufferedListView list;
        StatusStrip status;
        ToolStripStatusLabel lblCount, lblSpeed;
        NotifyIcon tray;
        System.Windows.Forms.Timer uiTimer, clipTimer;
        ContextMenuStrip ctx;
        ToolStripMenuItem ctxOpen, ctxFolder, ctxResume, ctxStop, ctxRedo, ctxCopy, ctxDelete;
        ImageList treeImages, listImages;

        bool reallyExit, pipeStarted, trayTipShown;
        string lastClip = "", pendingClipUrl;
        string filterTag = "all";
        string lastSignature = "";

        public MainForm(string[] args)
        {
            Text = "SwiftGet";
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.None;   // this window scales itself through Px()
            ClientSize = new Size(Px(1020), Px(600));
            MinimumSize = new Size(Px(760), Px(420));
            StartPosition = FormStartPosition.CenterScreen;
            Icon appIcon = Icons.AppIcon();
            Icon = appIcon;

            BuildImages();
            BuildMenu();
            BuildToolbar();
            BuildBody();
            BuildStatus();
            BuildTray(appIcon);

            Controls.Add(split);
            Controls.Add(toolbar);
            Controls.Add(menu);
            Controls.Add(status);
            MainMenuStrip = menu;

            mgr.Completed += OnCompleted;

            uiTimer = new System.Windows.Forms.Timer { Interval = 400 };
            uiTimer.Tick += delegate { RefreshList(); UpdateStatus(); };
            uiTimer.Start();

            clipTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            clipTimer.Tick += delegate { CheckClipboard(); };
            clipTimer.Start();

            RefreshList();
            UpdateStatus();

            bool startHidden = args.Any(a => a == "--tray");
            if (startHidden) { WindowState = FormWindowState.Minimized; ShowInTaskbar = false; Opacity = 0; Shown += delegate { Hide(); Opacity = 1; ShowInTaskbar = true; }; }

            string url = args.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || a.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
            if (url != null) Shown += delegate { BeginInvoke(new Action(() => ShowAddDialog(url))); };
        }

        // ------------------------------------------------------------------ construction

        static float dpiScale;
        static int Px(int v)
        {
            if (dpiScale <= 0)
            {
                try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpiScale = g.DpiX / 96f; } catch { dpiScale = 1f; }
                if (dpiScale <= 0) dpiScale = 1f;
            }
            return (int)Math.Round(v * dpiScale);
        }

        void BuildImages()
        {
            treeImages = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(Px(16), Px(16)) };
            treeImages.Images.Add("folder", Icons.Glyph(Icons.Folder, Color.FromArgb(230, 170, 40), Px(16)));
            treeImages.Images.Add("unfinished", Icons.Glyph(Icons.Clock, Color.FromArgb(40, 110, 200), Px(16)));
            treeImages.Images.Add("finished", Icons.Glyph(Icons.Check, Color.FromArgb(40, 160, 80), Px(16)));
            treeImages.Images.Add("queue", Icons.Glyph(Icons.Download, Color.FromArgb(90, 90, 90), Px(16)));

            listImages = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(Px(16), Px(16)) };
            foreach (string c in Categories.Names) listImages.Images.Add(c, Icons.Glyph(Icons.Page, Icons.CategoryColor(c), Px(16)));
        }

        ToolStripMenuItem Item(string text, EventHandler h, Keys keys)
        {
            var mi = new ToolStripMenuItem(text);
            if (h != null) mi.Click += h;
            if (keys != Keys.None) mi.ShortcutKeys = keys;
            return mi;
        }

        void BuildMenu()
        {
            menu = new MenuStrip();
            var tasks = new ToolStripMenuItem("Tasks");
            tasks.DropDownItems.Add(Item("Add new download...", delegate { ShowAddDialog(null); }, Keys.Control | Keys.N));
            tasks.DropDownItems.Add(Item("Browser extension setup...", delegate { ShowBrowserSetup(); }, Keys.None));
            tasks.DropDownItems.Add(new ToolStripSeparator());
            tasks.DropDownItems.Add(Item("Exit", delegate { ExitApp(); }, Keys.None));

            var dl = new ToolStripMenuItem("Downloads");
            dl.DropDownItems.Add(Item("Resume", delegate { DoResume(); }, Keys.None));
            dl.DropDownItems.Add(Item("Stop", delegate { DoStop(); }, Keys.None));
            dl.DropDownItems.Add(Item("Stop all", delegate { mgr.StopAll(); }, Keys.None));
            dl.DropDownItems.Add(new ToolStripSeparator());
            dl.DropDownItems.Add(Item("Start queue", delegate { mgr.StartQueue(); }, Keys.None));
            dl.DropDownItems.Add(Item("Stop queue", delegate { mgr.StopQueue(); }, Keys.None));
            dl.DropDownItems.Add(new ToolStripSeparator());
            dl.DropDownItems.Add(Item("Redownload", delegate { DoRedownload(); }, Keys.None));
            dl.DropDownItems.Add(Item("Delete", delegate { DoDelete(); }, Keys.Delete));
            dl.DropDownItems.Add(Item("Delete all completed", delegate { DoDeleteCompleted(); }, Keys.None));

            var view = new ToolStripMenuItem("View");
            view.DropDownItems.Add(Item("Show / hide categories", delegate { split.Panel1Collapsed = !split.Panel1Collapsed; }, Keys.None));

            var help = new ToolStripMenuItem("Help");
            help.DropDownItems.Add(Item("About SwiftGet", delegate
            {
                MessageBox.Show(this, "SwiftGet - a free, open download manager.\n\nMulti-connection downloads with dynamic segmentation, pause / resume, queue and speed limiter.\n\nData folder: " +
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SwiftGet"), "About SwiftGet", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }, Keys.None));

            var tools = new ToolStripMenuItem("Options");
            tools.Click += delegate { ShowOptions(); };

            menu.Items.AddRange(new ToolStripItem[] { tasks, dl, view, tools, help });
        }

        ToolStripButton Button(string text, string glyph, Color color, EventHandler h)
        {
            int s = Px(32);
            var b = new ToolStripButton(text, Icons.Glyph(glyph, color, s))
            {
                TextImageRelation = TextImageRelation.ImageAboveText,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                AutoSize = false,
                Width = Px(80),
                Height = Px(74),
                ImageScaling = ToolStripItemImageScaling.None
            };
            b.Click += h;
            return b;
        }

        void BuildToolbar()
        {
            toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(Px(32), Px(32)), Padding = new Padding(6, 4, 6, 2) };
            Color blue = Color.FromArgb(30, 110, 215), green = Color.FromArgb(30, 150, 70), red = Color.FromArgb(210, 60, 60),
                  orange = Color.FromArgb(235, 140, 20), gray = Color.FromArgb(90, 90, 90);
            toolbar.Items.Add(Button("Add URL", Icons.Add, blue, delegate { ShowAddDialog(null); }));
            toolbar.Items.Add(Button("Resume", Icons.Play, green, delegate { DoResume(); }));
            toolbar.Items.Add(Button("Stop", Icons.Pause, orange, delegate { DoStop(); }));
            toolbar.Items.Add(Button("Stop All", Icons.Stop, red, delegate { mgr.StopAll(); }));
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(Button("Delete", Icons.Delete, red, delegate { DoDelete(); }));
            toolbar.Items.Add(Button("Clear Done", Icons.Check, gray, delegate { DoDeleteCompleted(); }));
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(Button("Options", Icons.Settings, gray, delegate { ShowOptions(); }));
            toolbar.Items.Add(Button("Start Queue", Icons.Play, blue, delegate { mgr.StartQueue(); }));
            toolbar.Items.Add(Button("Stop Queue", Icons.Stop, orange, delegate { mgr.StopQueue(); }));
        }

        void BuildBody()
        {
            split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 4 };
            split.Panel1MinSize = Px(120);

            tree = new TreeView { Dock = DockStyle.Fill, ImageList = treeImages, HideSelection = false, ShowLines = false, ItemHeight = Px(22), BorderStyle = BorderStyle.None };
            TreeNode all = new TreeNode("All Downloads") { Tag = "all", ImageKey = "folder", SelectedImageKey = "folder" };
            foreach (string c in Categories.Names)
                all.Nodes.Add(new TreeNode(c) { Tag = "cat:" + c, ImageKey = "folder", SelectedImageKey = "folder" });
            tree.Nodes.Add(all);
            tree.Nodes.Add(new TreeNode("Unfinished") { Tag = "unfinished", ImageKey = "unfinished", SelectedImageKey = "unfinished" });
            tree.Nodes.Add(new TreeNode("Finished") { Tag = "finished", ImageKey = "finished", SelectedImageKey = "finished" });
            TreeNode q = new TreeNode("Queues") { Tag = "queue", ImageKey = "queue", SelectedImageKey = "queue" };
            q.Nodes.Add(new TreeNode("Main download queue") { Tag = "queue", ImageKey = "queue", SelectedImageKey = "queue" });
            tree.Nodes.Add(q);
            all.Expand(); q.Expand();
            tree.SelectedNode = all;
            tree.AfterSelect += delegate { filterTag = (string)tree.SelectedNode.Tag; lastSignature = ""; RefreshList(); };
            split.Panel1.Controls.Add(tree);

            list = new DoubleBufferedListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, MultiSelect = true,
                HideSelection = false, SmallImageList = listImages, ShowItemToolTips = true, BorderStyle = BorderStyle.None
            };
            list.Columns.Add("File Name", Px(280));
            list.Columns.Add("Size", Px(80), HorizontalAlignment.Right);
            list.Columns.Add("Status", Px(110));
            list.Columns.Add("Time left", Px(100));
            list.Columns.Add("Transfer rate", Px(100), HorizontalAlignment.Right);
            list.Columns.Add("Last try date", Px(150));
            list.Columns.Add("Save to", Px(260));
            list.DoubleClick += delegate { OpenSelected(); };
            list.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) OpenSelected();
                else if (e.Control && e.KeyCode == Keys.A) foreach (ListViewItem i in list.Items) i.Selected = true;
            };
            split.Panel2.Controls.Add(list);

            ctx = new ContextMenuStrip();
            ctxOpen = new ToolStripMenuItem("Open", null, delegate { OpenSelected(); });
            ctxFolder = new ToolStripMenuItem("Open folder", null, delegate { OpenFolder(); });
            ctxResume = new ToolStripMenuItem("Resume download", null, delegate { DoResume(); });
            ctxStop = new ToolStripMenuItem("Stop download", null, delegate { DoStop(); });
            ctxRedo = new ToolStripMenuItem("Redownload", null, delegate { DoRedownload(); });
            ctxCopy = new ToolStripMenuItem("Copy address", null, delegate { CopyAddress(); });
            ctxDelete = new ToolStripMenuItem("Delete", null, delegate { DoDelete(); });
            ctx.Items.AddRange(new ToolStripItem[] { ctxOpen, ctxFolder, new ToolStripSeparator(), ctxResume, ctxStop, ctxRedo,
                new ToolStripSeparator(), ctxCopy, ctxDelete });
            ctx.Opening += delegate (object s, System.ComponentModel.CancelEventArgs e)
            {
                List<DownloadItem> sel = Selected();
                if (sel.Count == 0) { e.Cancel = true; return; }
                bool done = sel.Count == 1 && sel[0].Status == DownloadStatus.Complete;
                ctxOpen.Enabled = done; ctxFolder.Enabled = sel.Count == 1 && !string.IsNullOrEmpty(sel[0].SavePath);
                ctxResume.Enabled = sel.Any(i => i.Status == DownloadStatus.Stopped || i.Status == DownloadStatus.Error);
                ctxStop.Enabled = sel.Any(i => i.IsActive || i.Status == DownloadStatus.Queued);
            };
            list.ContextMenuStrip = ctx;
        }

        void BuildStatus()
        {
            status = new StatusStrip();
            lblCount = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            lblSpeed = new ToolStripStatusLabel { AutoSize = false, Width = Px(180), TextAlign = ContentAlignment.MiddleRight };
            status.Items.AddRange(new ToolStripItem[] { lblCount, lblSpeed });
        }

        void BuildTray(Icon appIcon)
        {
            tray = new NotifyIcon { Icon = appIcon, Text = "SwiftGet", Visible = true };
            var m = new ContextMenuStrip();
            m.Items.Add("Open SwiftGet", null, delegate { ShowMainWindow(); });
            m.Items.Add("Add URL...", null, delegate { ShowAddDialog(null); });
            m.Items.Add("Stop all", null, delegate { mgr.StopAll(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Exit", null, delegate { ExitApp(); });
            tray.ContextMenuStrip = m;
            tray.DoubleClick += delegate { ShowMainWindow(); };
            tray.BalloonTipClicked += delegate
            {
                if (pendingClipUrl != null) { string u = pendingClipUrl; pendingClipUrl = null; ShowAddDialog(u); }
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try { split.SplitterDistance = Px(200); } catch { }
        }

        // ------------------------------------------------------------------ list handling

        bool Matches(DownloadItem it)
        {
            if (filterTag == "all") return true;
            if (filterTag == "unfinished") return it.Status != DownloadStatus.Complete;
            if (filterTag == "finished") return it.Status == DownloadStatus.Complete;
            if (filterTag == "queue") return it.InQueue && it.Status != DownloadStatus.Complete;
            if (filterTag.StartsWith("cat:")) return it.Category == filterTag.Substring(4);
            return true;
        }

        List<DownloadItem> Selected()
        {
            var r = new List<DownloadItem>();
            foreach (ListViewItem li in list.SelectedItems) { DownloadItem d = li.Tag as DownloadItem; if (d != null) r.Add(d); }
            return r;
        }

        string[] RowText(DownloadItem it)
        {
            long total = it.TotalSize, done = it.GetDownloaded();
            double pct = total > 0 ? Math.Min(100.0, done * 100.0 / total) : 0;
            string st;
            switch (it.Status)
            {
                case DownloadStatus.Complete: st = "Complete"; break;
                case DownloadStatus.Downloading: st = total > 0 ? pct.ToString("0.0") + "%" : Fmt.Size(done); break;
                case DownloadStatus.Connecting: st = "Connecting..."; break;
                case DownloadStatus.Queued: st = "Queued"; break;
                case DownloadStatus.Error: st = "Error"; break;
                default: st = total > 0 && done > 0 ? "Stopped (" + pct.ToString("0.0") + "%)" : "Stopped"; break;
            }
            string left = it.Status == DownloadStatus.Downloading && it.Speed > 1 && total > 0 ? Fmt.Time((total - done) / it.Speed) : "";
            string rate = it.Status == DownloadStatus.Downloading ? Fmt.Speed(it.Speed) : "";
            string last = it.LastTry == DateTime.MinValue ? "" : it.LastTry.ToString("MMM dd HH:mm:ss yyyy");
            string dir = string.IsNullOrEmpty(it.SavePath) ? it.Folder : Path.GetDirectoryName(it.SavePath);
            return new[] { it.FileName, total >= 0 && it.Probed ? Fmt.Size(total) : "", st, left, rate, last, dir };
        }

        void RefreshList()
        {
            List<DownloadItem> visible = mgr.GetItems().Where(Matches).ToList();
            string sig = string.Join(",", visible.Select(i => i.Id + ":" + i.FileName));
            if (sig != lastSignature)
            {
                lastSignature = sig;
                var selIds = new HashSet<string>(Selected().Select(i => i.Id));
                list.BeginUpdate();
                list.Items.Clear();
                rows.Clear();
                foreach (DownloadItem it in visible)
                {
                    string[] t = RowText(it);
                    var li = new ListViewItem(t) { Tag = it, ImageKey = it.Category };
                    rows[it.Id] = li;
                    list.Items.Add(li);
                    if (selIds.Contains(it.Id)) li.Selected = true;
                }
                list.EndUpdate();
            }
            else
            {
                foreach (DownloadItem it in visible)
                {
                    ListViewItem li;
                    if (!rows.TryGetValue(it.Id, out li)) continue;
                    string[] t = RowText(it);
                    for (int c = 0; c < t.Length; c++)
                        if (li.SubItems[c].Text != t[c]) li.SubItems[c].Text = t[c];
                    string tip = it.Status == DownloadStatus.Error ? it.Error : it.Url;
                    if (li.ToolTipText != tip) li.ToolTipText = tip;
                }
            }
        }

        void UpdateStatus()
        {
            List<DownloadItem> all = mgr.GetItems();
            int active = all.Count(i => i.IsActive);
            lblCount.Text = all.Count + " downloads   |   " + active + " active   |   " + all.Count(i => i.Status == DownloadStatus.Complete) + " complete";
            double sp = mgr.TotalSpeed;
            lblSpeed.Text = sp > 1 ? Fmt.Speed(sp) : "";
            tray.Text = active > 0 ? ("SwiftGet - " + active + " active, " + Fmt.Speed(sp)) : "SwiftGet";
        }

        // ------------------------------------------------------------------ actions

        void ShowBrowserSetup()
        {
            ShowMainWindow();
            using (var f = new BrowserSetupForm()) f.ShowDialog(this);
        }

        void ShowAddDialog(string prefill, string referer = null, string cookie = null, string userAgent = null, bool fromBrowser = false)
        {
            if (!fromBrowser) ShowMainWindow();
            if (prefill == null)
            {
                string clip = SafeClipboard();
                if (clip != null && LinkRegex.IsMatch(clip.Trim())) prefill = clip.Trim();
            }
            using (var dlg = new AddUrlDialog(mgr.Settings, prefill))
            {
                if (fromBrowser) { dlg.TopMost = true; dlg.Shown += delegate { dlg.Activate(); }; }
                DialogResult result = Visible ? dlg.ShowDialog(this) : dlg.ShowDialog();
                if (result != DialogResult.OK) return;

                DownloadItem last = null;
                foreach (string url in dlg.Urls)
                {
                    bool withContext = fromBrowser && string.Equals(url, prefill, StringComparison.Ordinal);
                    last = mgr.Add(url, dlg.Folder, !dlg.StartNow,
                        withContext ? referer : null, withContext ? cookie : null, withContext ? userAgent : null);
                    if (dlg.StartNow) mgr.Start(last);
                }
                if (dlg.StartNow && dlg.Urls.Count == 1 && mgr.Settings.ShowProgressDialog) OpenProgress(last);
                RefreshList();
            }
        }

        void ShowOptions()
        {
            using (var dlg = new OptionsDialog(mgr.Settings))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) mgr.SaveSettings();
            }
        }

        void OpenProgress(DownloadItem it)
        {
            ProgressForm f;
            if (progress.TryGetValue(it.Id, out f)) { f.Show(); f.Activate(); return; }
            f = new ProgressForm(mgr, it);
            progress[it.Id] = f;
            string id = it.Id;
            f.FormClosed += delegate { progress.Remove(id); };
            f.Show();
        }

        void OpenSelected()
        {
            List<DownloadItem> sel = Selected();
            if (sel.Count == 0) return;
            DownloadItem it = sel[0];
            if (it.Status == DownloadStatus.Complete && File.Exists(it.SavePath)) CompleteForm.Launch(it.SavePath, false);
            else OpenProgress(it);
        }

        void OpenFolder()
        {
            List<DownloadItem> sel = Selected();
            if (sel.Count == 0) return;
            DownloadItem it = sel[0];
            string p = it.Status == DownloadStatus.Complete ? it.SavePath : it.PartPath;
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) CompleteForm.Launch(p, true);
            else if (!string.IsNullOrEmpty(it.SavePath)) { try { Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(it.SavePath) + "\""); } catch { } }
        }

        void DoResume()
        {
            foreach (DownloadItem it in Selected())
                if (it.Status == DownloadStatus.Stopped || it.Status == DownloadStatus.Error) mgr.Start(it);
        }

        void DoStop()
        {
            foreach (DownloadItem it in Selected()) mgr.Stop(it);
        }

        void DoRedownload()
        {
            List<DownloadItem> sel = Selected();
            if (sel.Count == 0) return;
            if (MessageBox.Show(this, "Download the selected file(s) again from the beginning?", "Redownload", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (DownloadItem it in sel) mgr.Redownload(it);
        }

        void CopyAddress()
        {
            string text = string.Join(Environment.NewLine, Selected().Select(i => i.Url));
            if (text.Length > 0) { try { Clipboard.SetText(text); lastClip = text; } catch { } }
        }

        async void DoDelete()
        {
            List<DownloadItem> sel = Selected();
            if (sel.Count == 0) return;
            bool deleteFiles = false;
            if (sel.Any(i => i.Status == DownloadStatus.Complete))
            {
                DialogResult r = MessageBox.Show(this, "Also delete the downloaded file(s) from your disk?\n\nYes = remove from list and delete file\nNo = remove from list only",
                    "Delete", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                deleteFiles = r == DialogResult.Yes;
            }
            else if (MessageBox.Show(this, "Delete " + sel.Count + " download(s) from the list?\n(Partly downloaded data will be removed.)", "Delete",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            foreach (DownloadItem it in sel)
            {
                ProgressForm f;
                if (progress.TryGetValue(it.Id, out f)) f.Close();
                await mgr.RemoveAsync(it, deleteFiles);
            }
            lastSignature = "";
            RefreshList();
        }

        void DoDeleteCompleted()
        {
            if (!mgr.GetItems().Any(i => i.Status == DownloadStatus.Complete)) return;
            if (MessageBox.Show(this, "Remove all completed downloads from the list? (Files stay on disk.)", "Delete completed",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            mgr.RemoveCompleted();
            lastSignature = "";
            RefreshList();
        }

        // ------------------------------------------------------------------ events

        void OnCompleted(DownloadItem it)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    ProgressForm f;
                    if (progress.TryGetValue(it.Id, out f)) f.Close();
                    try { SystemSounds.Asterisk.Play(); } catch { }
                    new CompleteForm(it).Show();
                    lastSignature = "";
                }));
            }
            catch { }
        }

        string SafeClipboard()
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch { return null; }
        }

        void CheckClipboard()
        {
            if (!mgr.Settings.MonitorClipboard) return;
            string t = SafeClipboard();
            if (t == null || t == lastClip) return;
            lastClip = t;
            t = t.Trim();
            if (t.Length > 2000 || !LinkRegex.IsMatch(t)) return;
            pendingClipUrl = t;
            string shown = t.Length > 80 ? t.Substring(0, 77) + "..." : t;
            tray.ShowBalloonTip(6000, "Download link copied", "Click here to download:\n" + shown, ToolTipIcon.Info);
        }

        // ------------------------------------------------------------------ window lifetime

        public void ShowMainWindow()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApp()
        {
            if (mgr.ActiveCount > 0 &&
                MessageBox.Show(this, "Downloads are still running. Exit anyway?\n\nThey will be paused and can be resumed next time.", "Exit SwiftGet",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            reallyExit = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing && mgr.Settings.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                if (!trayTipShown)
                {
                    trayTipShown = true;
                    tray.ShowBalloonTip(3000, "SwiftGet is still running", "Right-click the tray icon to exit.", ToolTipIcon.Info);
                }
                return;
            }
            uiTimer.Stop(); clipTimer.Stop();
            foreach (ProgressForm f in progress.Values.ToList()) f.Close();
            mgr.Shutdown();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------ single instance (named pipe)

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (pipeStarted) return;
            pipeStarted = true;
            var th = new Thread(PipeLoop) { IsBackground = true, Name = "SwiftGet pipe" };
            th.Start();
        }

        void PipeLoop()
        {
            while (!IsDisposed)
            {
                try
                {
                    using (var srv = new NamedPipeServerStream(Ipc.PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte))
                    {
                        srv.WaitForConnection();
                        using (var sr = new StreamReader(srv, Encoding.UTF8))
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                string l = line;
                                BeginInvoke(new Action(() => HandleExternal(l)));
                            }
                        }
                    }
                }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }
                catch { Thread.Sleep(300); }
            }
        }

        void HandleExternal(string line)
        {
            if (line.StartsWith("{"))
            {
                Dictionary<string, object> d;
                try { d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(line); }
                catch { return; }
                if (Field(d, "cmd") == "add")
                {
                    string url = Field(d, "url");
                    if (url.Length > 0)
                    {
                        ShowAddDialog(url, Field(d, "referer"), Field(d, "cookie"), Field(d, "userAgent"), true);
                        return;
                    }
                }
                ShowMainWindow();
                return;
            }
            if (line.StartsWith("ADD ")) ShowAddDialog(line.Substring(4).Trim());
            else ShowMainWindow();
        }

        static string Field(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        public static void SendToRunningInstance(string[] args)
        {
            string url = args.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || a.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
            Ipc.Send(url != null ? "ADD " + url : "SHOW", 3000);
        }
    }
}
