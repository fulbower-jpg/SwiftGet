using System;
using System.IO;
using System.Text;

namespace SwiftGet
{
    public static class Fmt
    {
        public static string Size(long b)
        {
            if (b < 0) return "";
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = b; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return i == 0 ? b + " B" : v.ToString(v >= 100 ? "0" : "0.##") + " " + u[i];
        }

        public static string Speed(double bps)
        {
            return bps < 1 ? "" : Size((long)bps) + "/s";
        }

        public static string Time(double secs)
        {
            if (double.IsNaN(secs) || double.IsInfinity(secs) || secs < 0) return "";
            long s = (long)secs;
            if (s < 60) return s + " sec";
            if (s < 3600) return (s / 60) + " min " + (s % 60) + " sec";
            if (s < 86400) return (s / 3600) + " h " + ((s % 3600) / 60) + " min";
            return (s / 86400) + " d " + ((s % 86400) / 3600) + " h";
        }
    }

    public static class PathUtil
    {
        public static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "download";
            var sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char c in name) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            string r = sb.ToString().Trim().TrimEnd('.');
            return r.Length == 0 ? "download" : (r.Length > 180 ? r.Substring(r.Length - 180) : r);
        }

        public static string Unique(string dir, string fileName, Func<string, bool> taken)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            string path = Path.Combine(dir, fileName);
            int n = 1;
            while (File.Exists(path) || File.Exists(path + ".part") || (taken != null && taken(path)))
            {
                path = Path.Combine(dir, name + " (" + n + ")" + ext);
                n++;
            }
            return path;
        }
    }
}
