using System;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace SwiftGet
{
    /// <summary>Talks to the copy of SwiftGet that is already running (single-instance pipe).</summary>
    static class Ipc
    {
        public const string PipeName = "SwiftGet.Pipe";

        public static bool Send(string line, int timeoutMs)
        {
            try
            {
                using (var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    c.Connect(timeoutMs);
                    var w = new StreamWriter(c, new UTF8Encoding(false));
                    w.WriteLine(line);
                    w.Flush();
                    return true;
                }
            }
            catch { return false; }
        }

        public static bool IsRunning()
        {
            try
            {
                using (var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    c.Connect(300);
                    return true;
                }
            }
            catch { return false; }
        }
    }
}
