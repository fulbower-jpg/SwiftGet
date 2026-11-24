using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftGet
{
    class HttpStatusException : Exception
    {
        public int Code;
        public HttpStatusException(int code, string reason) : base("Server replied: " + code + " " + reason) { Code = code; }
    }

    class FatalException : Exception
    {
        public FatalException(string msg, Exception inner) : base(msg, inner) { }
    }

    /// <summary>
    /// Runs one download: probe the server, split the file into segments, run N parallel
    /// connections, split the biggest remaining segment whenever a connection becomes free
    /// (dynamic segmentation), then rename the .part file to the final name.
    /// </summary>
    class DownloadJob
    {
        const int BufSize = 64 * 1024;
        const long MinSplit = 256 * 1024;   // a segment is only split if it has at least 2x this left
        const int ReadTimeoutMs = 30000;
        const int MaxRetries = 8;

        readonly DownloadManager mgr;
        readonly DownloadItem item;
        readonly CancellationTokenSource cts = new CancellationTokenSource();
        readonly object errLock = new object();
        Exception firstError;

        public Task Completion { get; private set; }

        public DownloadJob(DownloadManager mgr, DownloadItem item)
        {
            this.mgr = mgr;
            this.item = item;
        }

        public void Start() { Completion = Task.Run(new Func<Task>(RunAsync)); }
        public void Cancel() { cts.Cancel(); }

        // ------------------------------------------------------------------ main flow

        async Task RunAsync()
        {
            CancellationToken ct = cts.Token;
            try
            {
                item.Status = DownloadStatus.Connecting;
                item.Error = null;
                item.LastTry = DateTime.Now;
                item.Speed = 0;
                item.LastBytes = item.GetDownloaded();

                if (!item.Probed) await ProbeAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                PrepareSegments();
                Directory.CreateDirectory(Path.GetDirectoryName(item.SavePath));

                if (item.TotalSize == 0)
                {
                    File.WriteAllBytes(item.PartPath, new byte[0]);
                }
                else if (item.TotalSize < 0)
                {
                    item.Status = DownloadStatus.Downloading;
                    await DownloadUnknownSizeAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    item.Status = DownloadStatus.Downloading;
                    await DownloadSegmentedAsync(ct).ConfigureAwait(false);
                    long got = item.GetDownloaded();
                    if (got != item.TotalSize)
                        throw new IOException("Download incomplete (" + got + " of " + item.TotalSize + " bytes).");
                }
                ct.ThrowIfCancellationRequested();
                FinishFile();
            }
            catch (OperationCanceledException)
            {
                item.Status = DownloadStatus.Stopped;
            }
            catch (Exception ex)
            {
                item.Status = DownloadStatus.Error;
                Exception e = ex is AggregateException ? ((AggregateException)ex).GetBaseException() : ex;
                item.Error = e.Message;
            }
            finally
            {
                item.Speed = 0;
                mgr.OnJobFinished(item);
            }
            if (item.Status == DownloadStatus.Complete) mgr.RaiseCompleted(item);
        }

        void FinishFile()
        {
            string part = item.PartPath;
            string dest = item.SavePath;
            if (File.Exists(dest))
            {
                dest = PathUtil.Unique(Path.GetDirectoryName(dest), Path.GetFileName(dest), mgr.IsPathTaken);
                item.SavePath = dest;
                item.FileName = Path.GetFileName(dest);
            }
            File.Move(part, dest);
            lock (item.SegLock) { item.Segments.Clear(); }
            item.Status = DownloadStatus.Complete;
            item.Error = null;
        }

        // ------------------------------------------------------------------ probing

        HttpRequestMessage NewRequest()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, item.Url);
            if (!string.IsNullOrEmpty(item.Referer))
            {
                Uri r;
                if (Uri.TryCreate(item.Referer, UriKind.Absolute, out r)) req.Headers.Referrer = r;
            }
            if (!string.IsNullOrEmpty(item.Cookie)) req.Headers.TryAddWithoutValidation("Cookie", item.Cookie);
            if (!string.IsNullOrEmpty(item.UserAgent))
            {
                req.Headers.UserAgent.Clear();
                req.Headers.TryAddWithoutValidation("User-Agent", item.UserAgent);
            }
            return req;
        }

        async Task ProbeAsync(CancellationToken ct)
        {
            using (HttpRequestMessage req = NewRequest())
            {
                req.Headers.Range = new RangeHeaderValue(0, 0);
                using (HttpResponseMessage res = await mgr.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    int code = (int)res.StatusCode;
                    long total = -1;
                    bool range = false;

                    if (code == 206)
                    {
                        ContentRangeHeaderValue cr = res.Content.Headers.ContentRange;
                        if (cr != null && cr.Length.HasValue) { total = cr.Length.Value; range = true; }
                    }
                    else if (code == 416)
                    {
                        total = 0;
                    }
                    else if (code >= 200 && code < 300)
                    {
                        total = res.Content.Headers.ContentLength.HasValue ? res.Content.Headers.ContentLength.Value : -1;
                    }
                    else
                    {
                        throw new HttpStatusException(code, res.ReasonPhrase);
                    }

                    string name = FileNameFromHeaders(res);
                    if (string.IsNullOrWhiteSpace(name)) name = FileNameFromUri(res.RequestMessage != null ? res.RequestMessage.RequestUri : null);
                    if (string.IsNullOrWhiteSpace(name)) name = FileNameFromUri(new Uri(item.Url));
                    name = PathUtil.SafeName(name);

                    item.TotalSize = total;
                    item.SupportsRange = range;
                    item.FileName = name;
                    string dir = mgr.ResolveFolder(item, name);
                    Directory.CreateDirectory(dir);
                    item.SavePath = PathUtil.Unique(dir, name, mgr.IsPathTaken);
                    item.FileName = Path.GetFileName(item.SavePath);
                    item.Probed = true;
                }
            }
            mgr.Save();
        }

        static string FileNameFromHeaders(HttpResponseMessage res)
        {
            string raw = null;
            try
            {
                IEnumerable<string> vals;
                if (res.Content.Headers.TryGetValues("Content-Disposition", out vals))
                    foreach (string v in vals) { raw = v; break; }
            }
            catch { }
            if (string.IsNullOrEmpty(raw)) return null;

            Match m = Regex.Match(raw, @"filename\*\s*=\s*([^']*)'[^']*'([^;]+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                try { return Uri.UnescapeDataString(m.Groups[2].Value.Trim().Trim('"')); } catch { }
            }
            m = Regex.Match(raw, "filename\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(raw, @"filename\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim().Trim('"');
            return null;
        }

        static string FileNameFromUri(Uri u)
        {
            if (u == null) return null;
            string last = u.AbsolutePath;
            int i = last.LastIndexOf('/');
            if (i >= 0) last = last.Substring(i + 1);
            try { last = Uri.UnescapeDataString(last); } catch { }
            return string.IsNullOrWhiteSpace(last) ? u.Host : last;
        }

        // ------------------------------------------------------------------ segments

        void PrepareSegments()
        {
            lock (item.SegLock)
            {
                bool resumable = item.SupportsRange && item.TotalSize > 0 && item.Segments.Count > 0 && File.Exists(item.PartPath)
                                 && new FileInfo(item.PartPath).Length >= item.TotalSize;
                foreach (Segment s in item.Segments) s.Assigned = false;
                if (resumable) return;

                item.Segments.Clear();
                try { if (File.Exists(item.PartPath)) File.Delete(item.PartPath); } catch { }
                if (item.TotalSize <= 0) return;

                long total = item.TotalSize;
                int n = 1;
                if (item.SupportsRange)
                    n = (int)Math.Max(1, Math.Min(mgr.Settings.MaxConnections, total / (512 * 1024)));
                long chunk = total / n;
                for (int i = 0; i < n; i++)
                {
                    long start = i * chunk;
                    long end = i == n - 1 ? total - 1 : start + chunk - 1;
                    item.Segments.Add(new Segment { Start = start, End = end });
                }
                using (var fs = new FileStream(item.PartPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                    fs.SetLength(total);
            }
        }

        Segment TakeSegment()
        {
            lock (item.SegLock)
            {
                foreach (Segment s in item.Segments)
                    if (!s.Assigned && s.Remaining > 0) { s.Assigned = true; return s; }

                // dynamic segmentation: steal the second half of the biggest running segment
                if (!item.SupportsRange) return null;
                Segment best = null; long bestRem = 0;
                foreach (Segment s in item.Segments)
                {
                    long r = s.Remaining;
                    if (r > bestRem) { best = s; bestRem = r; }
                }
                if (best == null) return null;

                long newStart, newEnd;
                lock (best)
                {
                    long pos = best.Start + best.Downloaded;
                    long rem = best.End - pos + 1;
                    if (rem < 2 * MinSplit) return null;
                    long mid = pos + rem / 2;
                    newStart = mid;
                    newEnd = best.End;
                    best.End = mid - 1;
                }
                var ns = new Segment { Start = newStart, End = newEnd, Assigned = true };
                item.Segments.Add(ns);
                return ns;
            }
        }

        FileStream OpenPart()
        {
            return new FileStream(item.PartPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, 4096, true);
        }

        // ------------------------------------------------------------------ downloading

        async Task DownloadSegmentedAsync(CancellationToken ct)
        {
            int workers = item.SupportsRange ? Math.Max(1, Math.Min(mgr.Settings.MaxConnections, 32)) : 1;
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var tasks = new List<Task>();
                for (int i = 0; i < workers; i++)
                {
                    CancellationToken lt = linked.Token;
                    tasks.Add(Task.Run(() => WorkerAsync(lt, linked)));
                }
                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (Exception)
                {
                    ct.ThrowIfCancellationRequested();
                    lock (errLock) { if (firstError != null) throw firstError; }
                    throw;
                }
                ct.ThrowIfCancellationRequested();
                lock (errLock) { if (firstError != null) throw firstError; }
            }
        }

        async Task WorkerAsync(CancellationToken ct, CancellationTokenSource linked)
        {
            try
            {
                byte[] buf = new byte[BufSize];
                using (FileStream fs = OpenPart())
                {
                    while (!ct.IsCancellationRequested)
                    {
                        Segment seg = TakeSegment();
                        if (seg == null) break;
                        await DownloadSegmentWithRetryAsync(seg, fs, buf, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                lock (errLock) { if (firstError == null) firstError = ex; }
                linked.Cancel();
            }
        }

        async Task DownloadSegmentWithRetryAsync(Segment seg, FileStream fs, byte[] buf, CancellationToken ct)
        {
            int attempts = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                long before;
                lock (seg)
                {
                    if (seg.Start + seg.Downloaded > seg.End) return;
                    before = seg.Downloaded;
                }
                try
                {
                    await FetchAsync(seg, fs, buf, ct, item.SupportsRange).ConfigureAwait(false);
                    lock (seg) { if (seg.Start + seg.Downloaded > seg.End) return; }
                    throw new IOException("Connection closed before the segment was finished.");
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    if (IsFatal(ex)) throw;
                    long now; lock (seg) { now = seg.Downloaded; }
                    if (now > before) attempts = 0;
                    attempts++;
                    if (attempts > MaxRetries) throw;
                    int wait = Math.Min(10000, 1000 * (1 << Math.Min(attempts - 1, 4)));
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                }
            }
        }

        static bool IsFatal(Exception ex)
        {
            if (ex is FatalException) return true;
            HttpStatusException h = ex as HttpStatusException;
            if (h != null) return h.Code >= 400 && h.Code < 500 && h.Code != 408 && h.Code != 429;
            return false;
        }

        async Task DownloadUnknownSizeAsync(CancellationToken ct)
        {
            var seg = new Segment { Start = 0, End = long.MaxValue / 4, Assigned = true };
            lock (item.SegLock) { item.Segments.Clear(); item.Segments.Add(seg); }
            byte[] buf = new byte[BufSize];
            using (var fs = new FileStream(item.PartPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, true))
                await FetchAsync(seg, fs, buf, ct, false).ConfigureAwait(false);
            long n; lock (seg) { n = seg.Downloaded; }
            item.TotalSize = n;
        }

        /// <summary>One HTTP request that streams its bytes into the part file at the right offset.</summary>
        async Task FetchAsync(Segment seg, FileStream fs, byte[] buf, CancellationToken ct, bool ranged)
        {
            long pos, end;
            lock (seg) { pos = seg.Start + seg.Downloaded; end = seg.End; }

            using (HttpRequestMessage req = NewRequest())
            {
                if (ranged) req.Headers.Range = new RangeHeaderValue(pos, end);

                using (HttpResponseMessage res = await mgr.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                using (CancellationTokenSource reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                using (reqCts.Token.Register(() => { try { res.Dispose(); } catch { } }))
                {
                    int code = (int)res.StatusCode;
                    if (code < 200 || code > 299) throw new HttpStatusException(code, res.ReasonPhrase);
                    if (ranged && pos > 0 && code != 206)
                        throw new FatalException("The server stopped supporting resume for this link. Use Redownload.", null);

                    Stream stream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    fs.Seek(ranged ? pos : 0, SeekOrigin.Begin);

                    while (true)
                    {
                        int want;
                        lock (seg)
                        {
                            long rem = seg.End - (seg.Start + seg.Downloaded) + 1;
                            if (rem <= 0) return;
                            want = (int)Math.Min(buf.Length, rem);
                        }

                        reqCts.CancelAfter(ReadTimeoutMs);
                        int n = await stream.ReadAsync(buf, 0, want, reqCts.Token).ConfigureAwait(false);
                        reqCts.CancelAfter(Timeout.Infinite);
                        if (n <= 0) return;

                        // another worker may have taken the tail of this segment while we were reading
                        lock (seg)
                        {
                            long rem = seg.End - (seg.Start + seg.Downloaded) + 1;
                            if (rem <= 0) return;
                            if (n > rem) n = (int)rem;
                        }

                        await mgr.Limiter.WaitAsync(n, ct).ConfigureAwait(false);
                        try { await fs.WriteAsync(buf, 0, n, ct).ConfigureAwait(false); }
                        catch (IOException ioex) { throw new FatalException("Cannot write to disk: " + ioex.Message, ioex); }
                        lock (seg) { seg.Downloaded += n; }
                    }
                }
            }
        }
    }
}
