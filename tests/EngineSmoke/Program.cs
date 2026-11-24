using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SwiftGet;

namespace SmokeTests
{
    static class Program
    {
        static int failures;
        static int checks;

        static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "swiftget-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            Console.WriteLine("SwiftGet engine smoke tests  (work folder: " + root + ")");
            Console.WriteLine();

            using (var server = new TestServer())
            {
                Test("range download, 8 connections", root, server, RangeBig);
                Test("small file", root, server, SmallFile);
                Test("server without Range support", root, server, NoRange);
                Test("unknown size (no Content-Length)", root, server, UnknownSize);
                Test("file name from Content-Disposition", root, server, NamedFile);
                Test("empty file", root, server, EmptyFile);
                Test("404 becomes an Error status", root, server, NotFound);
                Test("connections that drop half way", root, server, Flaky);
                Test("same name twice gets (1)", root, server, SameName);
                Test("pause, restart app, resume", root, server, PauseResume);
            }

            try { Directory.Delete(root, true); } catch { }
            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL " + checks + " CHECKS PASSED" : failures + " OF " + checks + " CHECKS FAILED");
            return failures == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ helpers

        static void Test(string name, string root, TestServer server, Action<string, TestServer> body)
        {
            Console.WriteLine("- " + name);
            string dir = Path.Combine(root, Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            try { body(dir, server); }
            catch (Exception ex) { Check(false, "test crashed: " + ex); }
        }

        static void Check(bool ok, string what)
        {
            checks++;
            if (!ok) failures++;
            Console.WriteLine("    " + (ok ? "PASS  " : "FAIL  ") + what);
        }

        static DownloadManager NewManager(string dir, int connections)
        {
            var m = new DownloadManager(Path.Combine(dir, "data"));
            m.Settings.DefaultFolder = Path.Combine(dir, "out");
            m.Settings.MaxConnections = connections;
            m.Settings.SpeedLimitKBps = 0;
            return m;
        }

        static bool WaitFor(Func<bool> condition, int seconds)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                if (condition()) return true;
                Thread.Sleep(30);
            }
            return condition();
        }

        static bool Finished(DownloadItem it) { return it.Status == DownloadStatus.Complete || it.Status == DownloadStatus.Error; }

        static DownloadItem Run(DownloadManager m, string url, int seconds)
        {
            DownloadItem it = m.Add(url, null, false);
            m.Start(it);
            WaitFor(() => Finished(it), seconds);
            return it;
        }

        /// <summary>True when every byte of the file matches what the test server generates.</summary>
        static bool ContentIsCorrect(string path, long size)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
            using (var f = File.OpenRead(path))
            {
                var buf = new byte[1 << 20];
                long pos = 0;
                int n;
                while ((n = f.Read(buf, 0, buf.Length)) > 0)
                {
                    for (int i = 0; i < n; i++)
                        if (buf[i] != TestServer.At(pos + i)) return false;
                    pos += n;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ tests

        static void RangeBig(string dir, TestServer s)
        {
            const long size = 12000000;
            var m = NewManager(dir, 8);
            DownloadItem it = Run(m, s.Url("range/" + size + "/movie.mkv"), 60);
            Check(it.Status == DownloadStatus.Complete, "finished (status " + it.Status + " " + it.Error + ")");
            Check(it.SupportsRange && it.TotalSize == size, "size and range support detected");
            Check(it.SavePath != null && it.SavePath.Contains("Video"), "sorted into the Video folder");
            Check(ContentIsCorrect(it.SavePath, size), "every byte is correct");
            Check(!File.Exists(it.SavePath + ".part"), ".part file was renamed away");
            m.Shutdown();
        }

        static void SmallFile(string dir, TestServer s)
        {
            var m = NewManager(dir, 8);
            DownloadItem it = Run(m, s.Url("range/100000/notes.pdf"), 30);
            Check(it.Status == DownloadStatus.Complete && ContentIsCorrect(it.SavePath, 100000), "100 KB file is correct");
            Check(it.SavePath != null && it.SavePath.Contains("Documents"), "sorted into the Documents folder");
            m.Shutdown();
        }

        static void NoRange(string dir, TestServer s)
        {
            var m = NewManager(dir, 8);
            DownloadItem it = Run(m, s.Url("norange/3000000/setup.exe"), 30);
            Check(it.Status == DownloadStatus.Complete, "finished (status " + it.Status + " " + it.Error + ")");
            Check(!it.SupportsRange, "knows the server cannot resume");
            Check(ContentIsCorrect(it.SavePath, 3000000), "every byte is correct");
            m.Shutdown();
        }

        static void UnknownSize(string dir, TestServer s)
        {
            var m = NewManager(dir, 8);
            DownloadItem it = Run(m, s.Url("unknown/2000000/stream.bin"), 30);
            Check(it.Status == DownloadStatus.Complete, "finished (status " + it.Status + " " + it.Error + ")");
            Check(it.TotalSize == 2000000, "size learned at the end");
            Check(ContentIsCorrect(it.SavePath, 2000000), "every byte is correct");
            m.Shutdown();
        }

        static void NamedFile(string dir, TestServer s)
        {
            var m = NewManager(dir, 8);
            DownloadItem it = Run(m, s.Url("named/150000/download?id=7"), 30);
            Check(it.Status == DownloadStatus.Complete, "finished (status " + it.Status + " " + it.Error + ")");
            Check(it.FileName == "my report (final).pdf", "file name taken from the header: " + it.FileName);
            m.Shutdown();
        }

        static void EmptyFile(string dir, TestServer s)
        {
            var m = NewManager(dir, 8);
            DownloadItem it = Run(m, s.Url("empty/0/blank.txt"), 30);
            Check(it.Status == DownloadStatus.Complete && File.Exists(it.SavePath) && new FileInfo(it.SavePath).Length == 0, "0-byte file created");
            m.Shutdown();
        }

        static void NotFound(string dir, TestServer s)
        {
            var m = NewManager(dir, 8);
            DownloadItem it = Run(m, s.Url("missing/0/nothing.zip"), 30);
            Check(it.Status == DownloadStatus.Error, "status is Error");
            Check(it.Error != null && it.Error.Contains("404"), "message mentions 404: " + it.Error);
            m.Shutdown();
        }

        static void Flaky(string dir, TestServer s)
        {
            const long size = 16000000;
            var m = NewManager(dir, 8);
            DownloadItem it = Run(m, s.Url("flaky/" + size + "/big.zip"), 90);
            Check(it.Status == DownloadStatus.Complete, "survived cut-off connections (status " + it.Status + " " + it.Error + ")");
            Check(ContentIsCorrect(it.SavePath, size), "every byte is correct");
            m.Shutdown();
        }

        static void SameName(string dir, TestServer s)
        {
            var m = NewManager(dir, 4);
            DownloadItem a = Run(m, s.Url("range/50000/same.zip"), 30);
            DownloadItem b = Run(m, s.Url("range/50000/same.zip"), 30);
            Check(a.Status == DownloadStatus.Complete && b.Status == DownloadStatus.Complete, "both finished");
            Check(a.SavePath != b.SavePath && b.FileName == "same (1).zip", "second file named: " + b.FileName);
            m.Shutdown();
        }

        static void PauseResume(string dir, TestServer s)
        {
            const long size = 24000000;
            var m = NewManager(dir, 8);
            m.Settings.SpeedLimitKBps = 4000;     // slow enough that we can pause in the middle
            DownloadItem it = m.Add(s.Url("range/" + size + "/big.zip"), null, false);
            m.Start(it);
            bool progressed = WaitFor(() => it.GetDownloaded() > 5000000, 30);
            Check(progressed, "download is running");

            m.Stop(it);
            bool stopped = WaitFor(() => it.Status == DownloadStatus.Stopped, 10);
            long partial = it.GetDownloaded();
            Check(stopped && partial > 0 && partial < size, "paused part way: " + Fmt.Size(partial));
            m.Shutdown();                           // like closing the app

            var m2 = new DownloadManager(Path.Combine(dir, "data"));   // like opening it again
            m2.Settings.SpeedLimitKBps = 0;
            DownloadItem it2 = m2.GetItems().FirstOrDefault();
            Check(it2 != null && it2.Status == DownloadStatus.Stopped && it2.GetDownloaded() == partial, "progress was remembered");
            if (it2 == null) return;
            m2.Start(it2);
            WaitFor(() => Finished(it2), 60);
            Check(it2.Status == DownloadStatus.Complete, "resumed and finished (status " + it2.Status + " " + it2.Error + ")");
            Check(ContentIsCorrect(it2.SavePath, size), "every byte is correct after resume");
            m2.Shutdown();
        }
    }
}
