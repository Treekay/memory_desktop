using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace MemoryDesktop
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
                return SelfTests.Run(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "MemoryDesktop-tests"));
            if (args.Length > 0 && args[0] == "--diagnose-host")
            {
                string path = args.Length > 1 ? args[1] : "host-diagnostics.txt";
                File.WriteAllText(path, "OS: " + Environment.OSVersion.Version + Environment.NewLine + "Existing host found (read only): " + (NativeDesktop.FindHost(false) != IntPtr.Zero));
                return 0;
            }
            bool preview = args.Length > 0 && args[0] == "--preview";
            if (args.Length > 0 && !preview) return 2;
            if (preview && (args.Length < 2 || !Directory.Exists(args[1]))) return 2;
            using (var instance = new Mutex(false, preview ? "Local\\MemoryDesktop.Preview" : "Local\\MemoryDesktop.App"))
            {
                try { if (!instance.WaitOne(0)) return 0; } catch (AbandonedMutexException) { }
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    WallpaperController controller = null;
                    app.Startup += delegate { controller = new WallpaperController(app, args, preview); controller.Start(); };
                    app.Exit += delegate { if (controller != null) controller.Dispose(); };
                    app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                    {
                        MessageBox.Show("程序遇到错误并将退出。\n" + e.Exception.Message, "Memory Desktop", MessageBoxButton.OK, MessageBoxImage.Error);
                        e.Handled = true; app.Shutdown(1);
                    };
                    return app.Run();
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }
}
