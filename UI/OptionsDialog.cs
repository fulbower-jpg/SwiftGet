using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SwiftGet
{
    class OptionsDialog : Form
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        readonly Settings s;
        readonly TextBox txtFolder = new TextBox();
        readonly CheckBox chkCat = new CheckBox(), chkClip = new CheckBox(), chkTray = new CheckBox(),
                          chkDialog = new CheckBox(), chkStartup = new CheckBox();
        readonly NumericUpDown numConn = new NumericUpDown(), numConc = new NumericUpDown(), numLimit = new NumericUpDown();

        public OptionsDialog(Settings settings)
        {
            s = settings;
            Text = "SwiftGet Options";
            Font = new Font("Segoe UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 400);

            var tabs = new TabControl { Left = 10, Top = 10, Width = 460, Height = 340 };
            var general = new TabPage("General");
            var conn = new TabPage("Connection");
            tabs.TabPages.Add(general); tabs.TabPages.Add(conn);

            general.Controls.Add(new Label { Text = "Default download folder:", Left = 14, Top = 16, AutoSize = true });
            txtFolder.SetBounds(14, 38, 340, 24); txtFolder.Text = s.DefaultFolder;
            var browse = new Button { Text = "Browse...", Left = 362, Top = 36, Width = 78, Height = 27 };
            browse.Click += delegate
            {
                using (var fb = new FolderBrowserDialog())
                {
                    fb.SelectedPath = txtFolder.Text;
                    if (fb.ShowDialog(this) == DialogResult.OK) txtFolder.Text = fb.SelectedPath;
                }
            };
            general.Controls.AddRange(new Control[] { txtFolder, browse });

            AddCheck(general, chkCat, "Sort files into category sub-folders (Compressed, Video, ...)", 78, s.UseCategoryFolders);
            AddCheck(general, chkClip, "Watch the clipboard for download links", 108, s.MonitorClipboard);
            AddCheck(general, chkDialog, "Show the progress window when a download starts", 138, s.ShowProgressDialog);
            AddCheck(general, chkTray, "Keep running in the system tray when the window is closed", 168, s.MinimizeToTray);
            AddCheck(general, chkStartup, "Start SwiftGet when Windows starts", 198, IsStartupEnabled());

            AddNumber(conn, numConn, "Connections per download (1-32):", 20, 1, 32, s.MaxConnections);
            AddNumber(conn, numConc, "Downloads running at the same time (1-10):", 60, 1, 10, s.MaxConcurrent);
            AddNumber(conn, numLimit, "Speed limit in KB/s (0 = unlimited):", 100, 0, 1000000, s.SpeedLimitKBps);
            conn.Controls.Add(new Label
            {
                Left = 14, Top = 150, Width = 420, Height = 60, ForeColor = Color.DimGray,
                Text = "More connections make most downloads faster, but some servers refuse more than 4-8 per file. The speed limit is shared by all downloads."
            });

            var ok = new Button { Text = "OK", Left = 308, Top = 360, Width = 76, Height = 28 };
            var cancel = new Button { Text = "Cancel", Left = 392, Top = 360, Width = 76, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += delegate { Apply(); DialogResult = DialogResult.OK; Close(); };
            Controls.AddRange(new Control[] { tabs, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
        }

        static void AddCheck(Control parent, CheckBox c, string text, int top, bool value)
        {
            c.Text = text; c.Left = 14; c.Top = top; c.Width = 430; c.Checked = value;
            parent.Controls.Add(c);
        }

        static void AddNumber(Control parent, NumericUpDown n, string text, int top, int min, int max, int value)
        {
            parent.Controls.Add(new Label { Text = text, Left = 14, Top = top + 3, Width = 290 });
            n.SetBounds(320, top, 100, 24);
            n.Minimum = min; n.Maximum = max; n.Value = Math.Max(min, Math.Min(max, value));
            parent.Controls.Add(n);
        }

        void Apply()
        {
            s.DefaultFolder = txtFolder.Text.Trim();
            s.UseCategoryFolders = chkCat.Checked;
            s.MonitorClipboard = chkClip.Checked;
            s.ShowProgressDialog = chkDialog.Checked;
            s.MinimizeToTray = chkTray.Checked;
            s.MaxConnections = (int)numConn.Value;
            s.MaxConcurrent = (int)numConc.Value;
            s.SpeedLimitKBps = (int)numLimit.Value;
            SetStartup(chkStartup.Checked);
        }

        static bool IsStartupEnabled()
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("SwiftGet") != null; }
            catch { return false; }
        }

        static void SetStartup(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("SwiftGet", "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue("SwiftGet", false);
                }
            }
            catch { }
        }
    }
}
