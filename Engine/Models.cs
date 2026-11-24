using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace SwiftGet
{
    public enum DownloadStatus { Stopped = 0, Queued = 1, Connecting = 2, Downloading = 3, Complete = 4, Error = 5 }

    /// <summary>One byte range [Start, End] of the file, downloaded by one connection.</summary>
    public class Segment
    {
        public long Start { get; set; }
        public long End { get; set; }
        public long Downloaded { get; set; }

        [ScriptIgnore] public bool Assigned { get; set; }

        [ScriptIgnore]
        public long Remaining { get { lock (this) { return End - (Start + Downloaded) + 1; } } }

        public Segment Clone()
        {
            lock (this) { return new Segment { Start = Start, End = End, Downloaded = Downloaded }; }
        }
    }

    public class DownloadItem
    {
        public string Id { get; set; }
        public string Url { get; set; }
        public string Referer { get; set; }
        public string Cookie { get; set; }
        public string UserAgent { get; set; }
        public string FileName { get; set; }
        public string Folder { get; set; }
        public string SavePath { get; set; }
        public long TotalSize { get; set; }
        public bool SupportsRange { get; set; }
        public bool Probed { get; set; }
        public DownloadStatus Status { get; set; }
        public string Error { get; set; }
        public bool InQueue { get; set; }
        public long AddedTicks { get; set; }
        public long LastTryTicks { get; set; }
        public List<Segment> Segments { get; set; }

        [ScriptIgnore] public object SegLock { get; set; }
        [ScriptIgnore] public double Speed { get; set; }
        [ScriptIgnore] public long LastBytes { get; set; }

        public DownloadItem()
        {
            Id = Guid.NewGuid().ToString("N");
            TotalSize = -1;
            Segments = new List<Segment>();
            SegLock = new object();
            AddedTicks = DateTime.Now.Ticks;
        }

        [ScriptIgnore] public string PartPath { get { return SavePath + ".part"; } }
        [ScriptIgnore] public string Category { get { return Categories.For(FileName); } }
        [ScriptIgnore]
        public DateTime LastTry
        {
            get { return LastTryTicks <= 0 ? DateTime.MinValue : new DateTime(LastTryTicks); }
            set { LastTryTicks = value.Ticks; }
        }
        [ScriptIgnore]
        public bool IsActive
        {
            get { return Status == DownloadStatus.Connecting || Status == DownloadStatus.Downloading; }
        }

        public long GetDownloaded()
        {
            if (Status == DownloadStatus.Complete) return Math.Max(0, TotalSize);
            long sum = 0;
            lock (SegLock)
            {
                foreach (Segment s in Segments) lock (s) { sum += s.Downloaded; }
            }
            return sum;
        }

        public List<Segment> CopySegments()
        {
            lock (SegLock) { return Segments.Select(s => s.Clone()).ToList(); }
        }

        public DownloadItem Snapshot()
        {
            DownloadItem c = (DownloadItem)MemberwiseClone();
            c.SegLock = new object();
            c.Segments = CopySegments();
            return c;
        }
    }

    public class Settings
    {
        public string DefaultFolder { get; set; }
        public bool UseCategoryFolders { get; set; }
        public int MaxConnections { get; set; }
        public int MaxConcurrent { get; set; }
        public int SpeedLimitKBps { get; set; }
        public bool MonitorClipboard { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool ShowProgressDialog { get; set; }

        public Settings()
        {
            DefaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            UseCategoryFolders = true;
            MaxConnections = 8;
            MaxConcurrent = 3;
            SpeedLimitKBps = 0;
            MonitorClipboard = true;
            MinimizeToTray = true;
            ShowProgressDialog = true;
        }
    }

    public static class Categories
    {
        public static readonly string[] Names = { "Compressed", "Documents", "Music", "Programs", "Video", "Other" };

        static readonly Dictionary<string, string> map = Build();

        static Dictionary<string, string> Build()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Add(d, "Compressed", "zip rar 7z gz tgz tar bz2 xz zst cab");
            Add(d, "Documents", "pdf doc docx xls xlsx ppt pptx txt rtf epub mobi csv odt");
            Add(d, "Music", "mp3 wav flac aac ogg m4a wma opus");
            Add(d, "Programs", "exe msi apk dmg iso jar bat msix appx deb rpm img bin");
            Add(d, "Video", "mp4 mkv avi mov wmv flv webm m4v 3gp mpg mpeg ts");
            return d;
        }

        static void Add(Dictionary<string, string> d, string cat, string exts)
        {
            foreach (string e in exts.Split(' ')) d[e] = cat;
        }

        public static string For(string fileName)
        {
            string ext = Path.GetExtension(fileName ?? "").TrimStart('.');
            string cat;
            return map.TryGetValue(ext, out cat) ? cat : "Other";
        }

        public static bool IsKnownExtension(string fileName)
        {
            return For(fileName) != "Other";
        }
    }
}
