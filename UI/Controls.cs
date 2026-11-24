using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SwiftGet
{
    class DoubleBufferedListView : ListView
    {
        public DoubleBufferedListView() { DoubleBuffered = true; }
    }

    /// <summary>IDM-style bar showing which parts of the file each connection has downloaded.</summary>
    class SegmentMap : Control
    {
        static readonly Color[] palette =
        {
            Color.FromArgb(0, 150, 70), Color.FromArgb(30, 120, 215), Color.FromArgb(230, 140, 20), Color.FromArgb(150, 70, 190),
            Color.FromArgb(210, 60, 60), Color.FromArgb(0, 160, 170), Color.FromArgb(120, 150, 20), Color.FromArgb(200, 70, 140)
        };

        public DownloadItem Item;

        public SegmentMap()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.White);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            DownloadItem it = Item;
            if (it != null && it.TotalSize > 0)
            {
                int w = r.Width - 1;
                if (it.Status == DownloadStatus.Complete)
                {
                    using (var br = new SolidBrush(palette[0])) g.FillRectangle(br, 1, 1, w, r.Height - 1);
                }
                else
                {
                    List<Segment> segs = it.CopySegments();
                    for (int i = 0; i < segs.Count; i++)
                    {
                        Segment s = segs[i];
                        int x = 1 + (int)((double)s.Start / it.TotalSize * w);
                        int ww = (int)((double)s.Downloaded / it.TotalSize * w);
                        if (s.Downloaded > 0 && ww < 1) ww = 1;
                        using (var br = new SolidBrush(palette[i % palette.Length]))
                            g.FillRectangle(br, x, 1, ww, r.Height - 1);
                        int xe = 1 + (int)((double)(s.End + 1) / it.TotalSize * w);
                        g.DrawLine(Pens.Gainsboro, xe, 1, xe, r.Height - 1);
                    }
                }
            }
            g.DrawRectangle(Pens.Gray, r);
        }
    }
}
