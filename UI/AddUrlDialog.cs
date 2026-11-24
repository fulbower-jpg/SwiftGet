using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SwiftGet
{
    class AddUrlDialog : Form
    {
        readonly TextBox txtUrls = new TextBox();
        readonly TextBox txtFolder = new TextBox();

        public List<string> Urls = new List<string>();
        public string Folder;
        public bool StartNow;

        public AddUrlDialog(Settings settings, string prefill)
        {
            Text = "Enter new address to download from";
            Font = new Font("Segoe UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 250);

            var lbl = new Label { Text = "Address (one link per line):", Left = 14, Top = 14, AutoSize = true };
            txtUrls.SetBounds(14, 36, 530, 90);
            txtUrls.Multiline = true; txtUrls.AcceptsReturn = false; txtUrls.ScrollBars = ScrollBars.Vertical; txtUrls.WordWrap = false;
            txtUrls.Text = prefill ?? "";

            var lbl2 = new Label { Text = "Save to:", Left = 14, Top = 142, AutoSize = true };
            txtFolder.SetBounds(14, 162, 450, 24);
            txtFolder.Text = settings.DefaultFolder;
            var btnBrowse = new Button { Text = "Browse...", Left = 472, Top = 160, Width = 72, Height = 27 };
            btnBrowse.Click += delegate
            {
                using (var fb = new FolderBrowserDialog())
                {
                    fb.SelectedPath = txtFolder.Text;
                    if (fb.ShowDialog(this) == DialogResult.OK) txtFolder.Text = fb.SelectedPath;
                }
            };

            var btnStart = new Button { Text = "Start Download", Left = 232, Top = 206, Width = 120, Height = 28 };
            var btnLater = new Button { Text = "Download Later", Left = 358, Top = 206, Width = 108, Height = 28 };
            var btnCancel = new Button { Text = "Cancel", Left = 472, Top = 206, Width = 72, Height = 28, DialogResult = DialogResult.Cancel };
            btnStart.Click += delegate { Accept(true); };
            btnLater.Click += delegate { Accept(false); };

            Controls.AddRange(new Control[] { lbl, txtUrls, lbl2, txtFolder, btnBrowse, btnStart, btnLater, btnCancel });
            AcceptButton = btnStart;
            CancelButton = btnCancel;
            Shown += delegate { txtUrls.Focus(); txtUrls.SelectionStart = txtUrls.TextLength; };
        }

        void Accept(bool start)
        {
            var urls = new List<string>();
            foreach (string raw in txtUrls.Lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                Uri u;
                if (!Uri.TryCreate(line, UriKind.Absolute, out u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
                {
                    MessageBox.Show(this, "This is not a valid http:// or https:// address:\n\n" + line, "Invalid address",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                urls.Add(line);
            }
            if (urls.Count == 0)
            {
                MessageBox.Show(this, "Please enter at least one address.", "Nothing to download", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Urls = urls;
            Folder = txtFolder.Text.Trim();
            StartNow = start;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
