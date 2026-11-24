using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SwiftGet
{
    /// <summary>Small "Download complete" popup in the bottom-right corner (like IDM's).</summary>
    class CompleteForm : Form
    {
        readonly Timer closeTimer = new Timer();
        static int open;

        protected override bool ShowWithoutActivation { get { return true; } }

        public CompleteForm(DownloadItem it)
        {
            Text = "Download complete";
            Font = new Font("Segoe UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(380, 112);

            var name = new Label { Left = 12, Top = 12, Width = 356, Height = 20, Text = it.FileName, AutoEllipsis = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            var info = new Label { Left = 12, Top = 34, Width = 356, Height = 20, Text = Fmt.Size(it.TotalSize) + "  -  " + Path.GetDirectoryName(it.SavePath), AutoEllipsis = true, ForeColor = Color.DimGray };
            var bOpen = new Button { Text = "Open", Left = 12, Top = 68, Width = 80, Height = 28 };
            var bFolder = new Button { Text = "Open folder", Left = 98, Top = 68, Width = 96, Height = 28 };
            var bClose = new Button { Text = "Close", Left = 288, Top = 68, Width = 80, Height = 28 };
            string path = it.SavePath;
            bOpen.Click += delegate { Launch(path, false); Close(); };
            bFolder.Click += delegate { Launch(path, true); Close(); };
            bClose.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { name, info, bOpen, bFolder, bClose });

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 12 - (open % 4) * (Height + 6));
            open++;
            FormClosed += delegate { open = Math.Max(0, open - 1); closeTimer.Stop(); closeTimer.Dispose(); };
            closeTimer.Interval = 20000;
            closeTimer.Tick += delegate { Close(); };
            closeTimer.Start();
        }

        public static void Launch(string path, bool folder)
        {
            try
            {
                if (folder) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open: " + ex.Message, "SwiftGet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
