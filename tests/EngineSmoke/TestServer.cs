using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SmokeTests
{
    /// <summary>
    /// A tiny HTTP server for tests. Paths look like  /KIND/SIZE/anyname.ext  where KIND is:
    ///   range    - normal file with Range support
    ///   norange  - ignores Range, always sends the whole file
    ///   unknown  - no Content-Length (streams until the connection closes)
    ///   named    - range support + Content-Disposition file name
    ///   flaky    - range support, but the first few big requests are cut off halfway
    ///   empty    - a 0-byte file          missing - 404
    /// The file content is generated from the byte position (see At), so tests can verify any byte.
    /// </summary>
    class TestServer : IDisposable
    {
        readonly TcpListener listener;
        volatile bool stopped;
        int flakyDrops;

        public int Port { get; private set; }
        public string Url(string path) { return "http://127.0.0.1:" + Port + "/" + path; }

        public TestServer()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task.Run(new Func<Task>(AcceptLoop));
        }

        public static byte At(long i) { return (byte)((i * 131 + (i >> 8) * 7 + 13) & 0xFF); }

        async Task AcceptLoop()
        {
            while (!stopped)
            {
                TcpClient c;
                try { c = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch { return; }
                var client = c;
                Task.Run(() => Handle(client));
            }
        }

        void Handle(TcpClient client)
        {
            try
            {
                using (client)
                {
                    NetworkStream ns = client.GetStream();
                    string head = ReadHead(ns);
                    if (head == null) return;
                    string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    string[] req = lines[0].Split(' ');
                    string range = null;
                    foreach (string l in lines)
                        if (l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = l.Substring(6).Trim();
                    Respond(ns, req[1], range);
                }
            }
            catch { }
        }

        static string ReadHead(NetworkStream ns)
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            while (sb.Length < 16384)
            {
                if (ns.Read(one, 0, 1) <= 0) return null;
                sb.Append((char)one[0]);
                if (sb.Length >= 4 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\r' && sb[sb.Length - 3] == '\n' && sb[sb.Length - 4] == '\r')
                    return sb.ToString(0, sb.Length - 4);
            }
            return null;
        }

        static void Send(NetworkStream ns, string text)
        {
            byte[] b = Encoding.ASCII.GetBytes(text);
            ns.Write(b, 0, b.Length);
        }

        void Respond(NetworkStream ns, string path, string rangeHeader)
        {
            string[] parts = path.Trim('/').Split('/');
            string kind = parts[0];
            long size = parts.Length > 1 ? long.Parse(parts[1]) : 0;

            if (kind == "missing") { Send(ns, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"); return; }
            if (kind == "empty") { Send(ns, "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"); return; }

            bool ranged = kind == "range" || kind == "named" || kind == "flaky";
            long start = 0, end = size - 1;
            bool partial = false;
            if (ranged && rangeHeader != null && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                string[] r = rangeHeader.Substring(6).Split('-');
                start = long.Parse(r[0]);
                if (r.Length > 1 && r[1].Length > 0) end = Math.Min(size - 1, long.Parse(r[1]));
                partial = true;
            }

            var h = new StringBuilder();
            h.Append(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n");
            h.Append("Content-Type: application/octet-stream\r\nConnection: close\r\n");
            if (kind != "unknown") h.Append("Content-Length: " + (end - start + 1) + "\r\n");
            if (ranged) h.Append("Accept-Ranges: bytes\r\n");
            if (partial) h.Append("Content-Range: bytes " + start + "-" + end + "/" + size + "\r\n");
            if (kind == "named") h.Append("Content-Disposition: attachment; filename=\"my report (final).pdf\"\r\n");
            h.Append("\r\n");
            Send(ns, h.ToString());

            long total = end - start + 1;
            long stopAfter = total;
            if (kind == "flaky" && total > 400000 && Interlocked.Increment(ref flakyDrops) <= 6) stopAfter = total * 4 / 10;

            var buf = new byte[32 * 1024];
            long sent = 0;
            while (sent < stopAfter)
            {
                int n = (int)Math.Min(buf.Length, stopAfter - sent);
                for (int i = 0; i < n; i++) buf[i] = At(start + sent + i);
                ns.Write(buf, 0, n);
                sent += n;
            }
            // when "flaky" stops early the caller closes the socket, so the client sees a cut-off download
        }

        public void Dispose()
        {
            stopped = true;
            try { listener.Stop(); } catch { }
        }
    }
}
