using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace SwiftGet
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Started by Chrome/Edge as a native-messaging host: no window, just talk over stdin/stdout.
            if (NativeHost.IsHostLaunch(args)) return NativeHost.Run();

            bool first;
            using (var mutex = new Mutex(true, "SwiftGet.SingleInstance.Mutex", out first))
            {
                if (!first)
                {
                    MainForm.SendToRunningInstance(args);
                    return 0;
                }
                BrowserIntegration.RegisterNativeHost(Assembly.GetExecutingAssembly().Location);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args));
            }
            return 0;
        }
    }
}
