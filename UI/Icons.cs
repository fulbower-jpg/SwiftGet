using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SwiftGet
{
    /// <summary>Draws toolbar/tree icons from the built-in Windows icon font, so no image files are needed.</summary>
    static class Icons
    {
        public const string Add = "\uE710", Play = "\uE768", Pause = "\uE769", Stop = "\uE71A", Delete = "\uE74D",
            Settings = "\uE713", Clock = "\uE823", Folder = "\uE8B7", Download = "\uE896", Check = "\uE73E",
            Page = "\uE8A5", Cancel = "\uE711", Open = "\uE8E5", Copy = "\uE8C8", Refresh = "\uE72C", Info = "\uE946";

        static string fontName;
        static bool probed;

        static string FontName
        {
            get
            {
                if (!probed)
                {
                    probed = true;
                    foreach (string wanted in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                        foreach (FontFamily f in FontFamily.Families)
                            if (f.Name == wanted) { fontName = wanted; return fontName; }
                }
                return fontName;
            }
        }

        public static Bitmap Glyph(string glyph, Color color, int size)
        {
            var bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                string fn = FontName;
                if (fn != null)
                {
                    using (var f = new Font(fn, size * 0.72f, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var br = new SolidBrush(color))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(glyph, f, br, new RectangleF(0, 0, size, size), sf);
                }
                else
                {
                    using (var br = new SolidBrush(color))
                        g.FillEllipse(br, size * 0.15f, size * 0.15f, size * 0.7f, size * 0.7f);
                }
            }
            return bmp;
        }

        /// <summary>The application / tray icon: a blue disc with a white download arrow.</summary>
        public static Icon AppIcon()
        {
            int s = 32;
            var bmp = new Bitmap(s, s);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new LinearGradientBrush(new Rectangle(0, 0, s, s), Color.FromArgb(0, 150, 255), Color.FromArgb(0, 80, 200), 90f))
                    g.FillEllipse(br, 1, 1, s - 2, s - 2);
                using (var pen = new Pen(Color.White, 3.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLine(pen, s / 2f, 8, s / 2f, 19);
                    g.DrawLines(pen, new[] { new PointF(10.5f, 14.5f), new PointF(s / 2f, 20.5f), new PointF(21.5f, 14.5f) });
                    g.DrawLine(pen, 9, 24.5f, 23, 24.5f);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        public static Color CategoryColor(string category)
        {
            switch (category)
            {
                case "Compressed": return Color.FromArgb(230, 140, 20);
                case "Documents": return Color.FromArgb(40, 110, 200);
                case "Music": return Color.FromArgb(150, 70, 190);
                case "Programs": return Color.FromArgb(40, 160, 80);
                case "Video": return Color.FromArgb(210, 60, 60);
                default: return Color.FromArgb(120, 120, 120);
            }
        }
    }
}
