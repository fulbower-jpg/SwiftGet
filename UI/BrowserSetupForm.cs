using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SwiftGet
{
    class BrowserSetupForm : Form
    {
        string folder;

        public BrowserSetupForm()
        {
            Text = "Browser extension setup";
            Font = new Font("Segoe UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 330);

            var head = new Label { Left = 16, Top = 14, Width = 488, Height = 22, Text = "Connect Chrome, Edge or Brave to SwiftGet", Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
            var steps = new Label
            {
                Left = 16, Top = 44, Width = 488, Height = 170,
                Text =
                    "1.  Click \"Prepare extension\" below. SwiftGet unpacks the extension into a folder.\n\n" +
                    "2.  Open your browser's extensions page (the button below, or type chrome://extensions or edge://extensions).\n\n" +
                    "3.  Turn on \"Developer mode\" (top-right switch).\n\n" +
                    "4.  Click \"Load unpacked\" and choose the folder that was just opened.\n\n" +
                    "Done! Downloads in your browser now go to SwiftGet. Right-click any link and choose \"Download with SwiftGet\" to send it manually."
            };
            var note = new Label
            {
                Left = 16, Top = 222, Width = 488, Height = 40, ForeColor = Color.DimGray,
                Text = "Your browser may remind you about developer-mode extensions when it starts. That is normal for extensions that are not from the web store; just dismiss it."
            };

            var bPrep = new Button { Text = "Prepare extension", Left = 16, Top = 276, Width = 140, Height = 30 };
            var bChrome = new Button { Text = "Open Chrome page", Left = 164, Top = 276, Width = 130, Height = 30 };
            var bEdge = new Button { Text = "Open Edge page", Left = 300, Top = 276, Width = 120, Height = 30 };
            var bClose = new Button { Text = "Close", Left = 436, Top = 276, Width = 68, Height = 30, DialogResult = DialogResult.Cancel };

            bPrep.Click += delegate { Prepare(); };
            bChrome.Click += delegate { OpenBrowser("chrome.exe", "chrome://extensions"); };
            bEdge.Click += delegate { OpenBrowser("msedge.exe", "edge://extensions"); };

            Controls.AddRange(new Control[] { head, steps, note, bPrep, bChrome, bEdge, bClose });
            CancelButton = bClose;
        }

        void Prepare()
        {
            try
            {
                folder = BrowserIntegration.ExtractExtension();
                Clipboard.SetText(folder);
                Process.Start("explorer.exe", "\"" + folder + "\"");
                MessageBox.Show(this, "The extension is ready in:\n\n" + folder + "\n\n(The folder path was also copied to your clipboard.)",
                    "Extension prepared", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not prepare the extension: " + ex.Message, "SwiftGet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void OpenBrowser(string exe, string page)
        {
            try { Process.Start(exe, page); }
            catch
            {
                MessageBox.Show(this, "Could not open the browser automatically.\nOpen it yourself and type  " + page + "  in the address bar.",
                    "SwiftGet", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
