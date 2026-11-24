using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace SwiftGet
{
    /// <summary>Registers SwiftGet as a native-messaging host and unpacks the bundled extension.</summary>
    static class BrowserIntegration
    {
        // Fixed by the "key" inside extension/manifest.json, so the same ID is used on every PC.
        public const string ExtensionId = "omhegfoodfdkjlejdkibljilhcnmdlgb";
        public const string HostName = "com.swiftget.host";

        static readonly string[] RegistryRoots =
        {
            @"Software\Google\Chrome\NativeMessagingHosts\",
            @"Software\Microsoft\Edge\NativeMessagingHosts\",
            @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\"
        };

        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SwiftGet"); }
        }

        public static string ExtensionFolder { get { return Path.Combine(DataDir, "extension"); } }

        /// <summary>Safe to call on every start: rewrites the host manifest so it always points at this exe.</summary>
        public static void RegisterNativeHost(string exePath)
        {
            try
            {
                string dir = Path.Combine(DataDir, "native");
                Directory.CreateDirectory(dir);
                string manifestPath = Path.Combine(dir, HostName + ".json");

                var manifest = new Dictionary<string, object>
                {
                    { "name", HostName },
                    { "description", "SwiftGet download manager" },
                    { "path", exePath },
                    { "type", "stdio" },
                    { "allowed_origins", new[] { "chrome-extension://" + ExtensionId + "/" } }
                };
                File.WriteAllText(manifestPath, new JavaScriptSerializer().Serialize(manifest));

                foreach (string root in RegistryRoots)
                {
                    try
                    {
                        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(root + HostName))
                            if (k != null) k.SetValue("", manifestPath);
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>Copies the extension files that are embedded in SwiftGet.exe to a normal folder.</summary>
        public static string ExtractExtension()
        {
            string target = ExtensionFolder;
            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (!name.StartsWith("ext/", StringComparison.Ordinal)) continue;
                string rel = name.Substring(4).Replace('\\', '/');
                string dest = Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                using (Stream s = asm.GetManifestResourceStream(name))
                using (FileStream f = new FileStream(dest, FileMode.Create, FileAccess.Write))
                    s.CopyTo(f);
            }
            return target;
        }
    }
}
