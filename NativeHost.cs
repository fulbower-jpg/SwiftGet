using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SwiftGet
{
    /// <summary>
    /// Chrome / Edge "native messaging" host. The browser starts SwiftGet.exe with the extension's
    /// origin as an argument and speaks length-prefixed JSON on stdin/stdout.
    /// </summary>
    static class NativeHost
    {
        public static bool IsHostLaunch(string[] args)
        {
            foreach (string a in args)
                if (a == "--native-host" || a.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static int Run()
        {
            Stream input = Console.OpenStandardInput();
            Stream output = Console.OpenStandardOutput();
            var json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };

            while (true)
            {
                byte[] hdr = ReadExact(input, 4);
                if (hdr == null) return 0;
                int len = BitConverter.ToInt32(hdr, 0);
                if (len <= 0 || len > 4 * 1024 * 1024) return 1;
                byte[] body = ReadExact(input, len);
                if (body == null) return 0;

                Dictionary<string, object> req = null;
                try { req = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(body)); } catch { }

                Dictionary<string, object> resp = Handle(req, json);
                byte[] outBytes = Encoding.UTF8.GetBytes(json.Serialize(resp));
                output.Write(BitConverter.GetBytes(outBytes.Length), 0, 4);
                output.Write(outBytes, 0, outBytes.Length);
                output.Flush();
            }
        }

        static Dictionary<string, object> Handle(Dictionary<string, object> req, JavaScriptSerializer json)
        {
            var r = new Dictionary<string, object>();
            string cmd = Str(req, "cmd");
            try
            {
                switch (cmd)
                {
                    case "ping":
                        r["ok"] = true;
                        r["version"] = "1.0";
                        r["running"] = Ipc.IsRunning();
                        break;

                    case "show":
                        if (!EnsureRunning()) { r["ok"] = false; r["error"] = "Could not start SwiftGet."; break; }
                        r["ok"] = Ipc.Send("SHOW", 3000);
                        break;

                    case "add":
                        string url = Str(req, "url");
                        Uri u;
                        if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
                        {
                            r["ok"] = false; r["error"] = "Not an http(s) link."; break;
                        }
                        if (!EnsureRunning()) { r["ok"] = false; r["error"] = "Could not start SwiftGet."; break; }
                        var msg = new Dictionary<string, object>
                        {
                            { "cmd", "add" }, { "url", url },
                            { "referer", Str(req, "referer") }, { "cookie", Str(req, "cookie") }, { "userAgent", Str(req, "userAgent") }
                        };
                        bool sent = Ipc.Send(json.Serialize(msg), 3000);
                        r["ok"] = sent;
                        if (!sent) r["error"] = "SwiftGet did not answer.";
                        break;

                    default:
                        r["ok"] = false;
                        r["error"] = "Unknown command.";
                        break;
                }
            }
            catch (Exception ex)
            {
                r["ok"] = false;
                r["error"] = ex.Message;
            }
            return r;
        }

        static bool EnsureRunning()
        {
            if (Ipc.IsRunning()) return true;
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                Process.Start(new ProcessStartInfo(exe, "--tray") { UseShellExecute = false });
            }
            catch { return false; }
            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(250);
                if (Ipc.IsRunning()) return true;
            }
            return false;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        static byte[] ReadExact(Stream s, int count)
        {
            var buf = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buf, got, count - got);
                if (n <= 0) return null;
                got += n;
            }
            return buf;
        }
    }
}
