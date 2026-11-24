using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SwiftGet
{
    public class DownloadManager
    {
        readonly object sync = new object();
        readonly object saveLock = new object();
        readonly List<DownloadItem> items = new List<DownloadItem>();
        readonly Dictionary<string, DownloadJob> jobs = new Dictionary<string, DownloadJob>();
        readonly string dataDir;
        readonly string itemsFile;
        readonly string settingsFile;
        readonly System.Threading.Timer ticker;
        readonly Stopwatch tickClock = Stopwatch.StartNew();
        double lastTickAt;
        int tickCount;

        public Settings Settings { get; private set; }
        public HttpClient Http { get; private set; }
        public SpeedLimiter Limiter { get; private set; }
        public event Action<DownloadItem> Completed;

        public DownloadManager() : this(null) { }

        public DownloadManager(string dataDirectory)
        {
            dataDir = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SwiftGet");
            Directory.CreateDirectory(dataDir);
            itemsFile = Path.Combine(dataDir, "downloads.json");
            settingsFile = Path.Combine(dataDir, "settings.json");

            try { ServicePointManager.DefaultConnectionLimit = 256; ServicePointManager.Expect100Continue = false; } catch { }
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 20,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false
            };
            Http = new HttpClient(handler);
            Http.Timeout = Timeout.InfiniteTimeSpan;
            Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "identity");

            Limiter = new SpeedLimiter();
            Limiter.GetLimitBytesPerSec = delegate { return (long)Settings.SpeedLimitKBps * 1024; };

            LoadSettings();
            LoadItems();
            ticker = new System.Threading.Timer(Tick, null, 500, 500);
        }

        // ------------------------------------------------------------------ persistence

        static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }; }

        void LoadSettings()
        {
            Settings = new Settings();
            try
            {
                if (File.Exists(settingsFile))
                {
                    Settings s = Json().Deserialize<Settings>(File.ReadAllText(settingsFile));
                    if (s != null) Settings = s;
                }
            }
            catch { }
            Clamp();
        }

        void Clamp()
        {
            Settings.MaxConnections = Math.Max(1, Math.Min(32, Settings.MaxConnections));
            Settings.MaxConcurrent = Math.Max(1, Math.Min(10, Settings.MaxConcurrent));
            Settings.SpeedLimitKBps = Math.Max(0, Settings.SpeedLimitKBps);
            if (string.IsNullOrWhiteSpace(Settings.DefaultFolder)) Settings.DefaultFolder = new Settings().DefaultFolder;
        }

        public void SaveSettings()
        {
            Clamp();
            try { lock (saveLock) { File.WriteAllText(settingsFile, Json().Serialize(Settings)); } } catch { }
            Pump();
        }

        void LoadItems()
        {
            try
            {
                if (!File.Exists(itemsFile)) return;
                List<DownloadItem> list = Json().Deserialize<List<DownloadItem>>(File.ReadAllText(itemsFile));
                if (list == null) return;
                foreach (DownloadItem it in list)
                {
                    if (it.Segments == null) it.Segments = new List<Segment>();
                    if (it.SegLock == null) it.SegLock = new object();
                    if (it.Status != DownloadStatus.Complete && it.Status != DownloadStatus.Error) it.Status = DownloadStatus.Stopped;
                    it.LastBytes = it.GetDownloaded();
                    items.Add(it);
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                List<DownloadItem> snap;
                lock (sync) { snap = items.Select(i => i.Snapshot()).ToList(); }
                string json = Json().Serialize(snap);
                lock (saveLock)
                {
                    string tmp = itemsFile + ".tmp";
                    File.WriteAllText(tmp, json);
                    if (File.Exists(itemsFile)) File.Replace(tmp, itemsFile, null);
                    else File.Move(tmp, itemsFile);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ queries

        public List<DownloadItem> GetItems() { lock (sync) { return new List<DownloadItem>(items); } }

        public double TotalSpeed { get { lock (sync) { return items.Where(i => i.IsActive).Sum(i => i.Speed); } } }

        public int ActiveCount { get { lock (sync) { return items.Count(i => i.IsActive); } } }

        public bool IsPathTaken(string path)
        {
            lock (sync)
            {
                return items.Any(i => i.SavePath != null && string.Equals(i.SavePath, path, StringComparison.OrdinalIgnoreCase));
            }
        }

        public string ResolveFolder(DownloadItem it, string fileName)
        {
            string baseDir = string.IsNullOrWhiteSpace(it.Folder) ? Settings.DefaultFolder : it.Folder;
            return Settings.UseCategoryFolders ? Path.Combine(baseDir, Categories.For(fileName)) : baseDir;
        }

        // ------------------------------------------------------------------ commands

        public DownloadItem Add(string url, string folder, bool inQueue, string referer = null, string cookie = null, string userAgent = null)
        {
            var it = new DownloadItem
            {
                Url = url.Trim(),
                Folder = string.IsNullOrWhiteSpace(folder) ? Settings.DefaultFolder : folder,
                InQueue = inQueue,
                Referer = referer,
                Cookie = cookie,
                UserAgent = userAgent,
                Status = DownloadStatus.Stopped
            };
            Uri u;
            string guess = "download";
            if (Uri.TryCreate(it.Url, UriKind.Absolute, out u))
            {
                string last = u.AbsolutePath;
                int i = last.LastIndexOf('/');
                if (i >= 0) last = last.Substring(i + 1);
                try { last = Uri.UnescapeDataString(last); } catch { }
                if (!string.IsNullOrWhiteSpace(last)) guess = last;
            }
            it.FileName = PathUtil.SafeName(guess);
            lock (sync) { items.Add(it); }
            Save();
            return it;
        }

        public void Start(DownloadItem it)
        {
            lock (sync)
            {
                if (it.Status == DownloadStatus.Complete || jobs.ContainsKey(it.Id)) return;
                it.Status = DownloadStatus.Queued;
                it.Error = null;
            }
            Pump();
        }

        public void Stop(DownloadItem it)
        {
            lock (sync)
            {
                DownloadJob job;
                if (jobs.TryGetValue(it.Id, out job)) job.Cancel();
                else if (it.Status == DownloadStatus.Queued) it.Status = DownloadStatus.Stopped;
            }
            Save();
        }

        public void StopAll()
        {
            lock (sync)
            {
                foreach (DownloadJob j in jobs.Values) j.Cancel();
                foreach (DownloadItem i in items) if (i.Status == DownloadStatus.Queued) i.Status = DownloadStatus.Stopped;
            }
            Save();
        }

        public void StartQueue()
        {
            foreach (DownloadItem it in GetItems())
                if (it.InQueue && it.Status != DownloadStatus.Complete) Start(it);
        }

        public void StopQueue()
        {
            foreach (DownloadItem it in GetItems()) if (it.InQueue) Stop(it);
        }

        public async Task RemoveAsync(DownloadItem it, bool deleteFile)
        {
            DownloadJob job;
            lock (sync) { jobs.TryGetValue(it.Id, out job); }
            if (job != null)
            {
                job.Cancel();
                try { await job.Completion.ConfigureAwait(false); } catch { }
            }
            lock (sync) { items.Remove(it); }
            try
            {
                if (!string.IsNullOrEmpty(it.SavePath))
                {
                    if (it.PartPath != null && File.Exists(it.PartPath)) File.Delete(it.PartPath);
                    if (deleteFile && File.Exists(it.SavePath)) File.Delete(it.SavePath);
                }
            }
            catch { }
            Save();
        }

        public void RemoveCompleted()
        {
            lock (sync) { items.RemoveAll(i => i.Status == DownloadStatus.Complete); }
            Save();
        }

        /// <summary>Throw away partial data and start the file again from zero.</summary>
        public void Redownload(DownloadItem it)
        {
            lock (sync) { if (jobs.ContainsKey(it.Id)) return; }
            try
            {
                if (it.PartPath != null && File.Exists(it.PartPath)) File.Delete(it.PartPath);
                if (it.Status == DownloadStatus.Complete && File.Exists(it.SavePath)) File.Delete(it.SavePath);
            }
            catch { }
            lock (it.SegLock) { it.Segments.Clear(); }
            it.Probed = false;
            it.TotalSize = -1;
            it.SavePath = null;
            it.Status = DownloadStatus.Stopped;
            Start(it);
        }

        public void Shutdown()
        {
            StopAll();
            List<Task> waits;
            lock (sync) { waits = jobs.Values.Select(j => j.Completion).Where(t => t != null).ToList(); }
            try { Task.WaitAll(waits.ToArray(), 4000); } catch { }
            Save();
            SaveSettings();
        }

        // ------------------------------------------------------------------ scheduling

        void Pump()
        {
            lock (sync)
            {
                foreach (DownloadItem it in items)
                {
                    if (jobs.Count >= Settings.MaxConcurrent) break;
                    if (it.Status != DownloadStatus.Queued || jobs.ContainsKey(it.Id)) continue;
                    var job = new DownloadJob(this, it);
                    jobs[it.Id] = job;
                    it.Status = DownloadStatus.Connecting;
                    job.Start();
                }
            }
        }

        internal void OnJobFinished(DownloadItem it)
        {
            lock (sync) { jobs.Remove(it.Id); }
            Save();
            Pump();
        }

        internal void RaiseCompleted(DownloadItem it)
        {
            Action<DownloadItem> h = Completed;
            if (h != null) { try { h(it); } catch { } }
        }

        void Tick(object state)
        {
            try
            {
                double now = tickClock.Elapsed.TotalSeconds;
                double dt = Math.Max(0.1, now - lastTickAt);
                lastTickAt = now;
                foreach (DownloadItem it in GetItems())
                {
                    if (!it.IsActive) { it.Speed = 0; continue; }
                    long cur = it.GetDownloaded();
                    double inst = Math.Max(0, (cur - it.LastBytes) / dt);
                    it.Speed = it.Speed <= 0 ? inst : it.Speed * 0.7 + inst * 0.3;
                    it.LastBytes = cur;
                }
                tickCount++;
                if (tickCount % 6 == 0 && ActiveCount > 0) Save();
            }
            catch { }
        }
    }
}
