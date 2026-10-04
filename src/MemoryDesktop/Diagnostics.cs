using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MemoryDesktop
{
    internal static class Diagnostics
    {
        internal static string LogPath { get { return Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath), "diagnostics.log"); } }
        internal static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024) File.WriteAllText(LogPath, "");
                File.AppendAllText(LogPath, DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        internal static string HostReport(bool decodeSelected)
        {
            var report = new StringBuilder();
            report.AppendLine("OS=" + Environment.OSVersion.Version);
            foreach (var screen in Screen.AllScreens) report.AppendLine("Display=" + screen.Bounds + " primary=" + screen.Primary);
            IntPtr host = NativeDesktop.FindHost(false);
            report.AppendLine("Host=" + NativeDesktop.Describe(host));
            IntPtr progman = NativeDesktop.FindWindow("Progman", null);
            report.AppendLine("Progman=" + NativeDesktop.Describe(progman));
            report.AppendLine("DefView=" + NativeDesktop.Describe(NativeDesktop.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null)));
            if (decodeSelected)
            {
                var settings = AppSettings.Load(AppSettings.DefaultPath);
                report.AppendLine("Paused=" + settings.Paused + " StartAtLoginPreference=" + settings.StartAtLogin);
                report.AppendLine("SelectedFolderExists=" + Directory.Exists(settings.PhotoFolder));
                try
                {
                    var source = PhotoSource.Scan(settings.PhotoFolder, CancellationToken.None, 7);
                    int candidates = source.Count, decoded = 0;
                    for (int i = 0; i < candidates; i++) { var photo = source.Next(CancellationToken.None); if (photo != null) decoded++; }
                    report.AppendLine("Candidates=" + candidates + " DecodedRequests=" + decoded + " RemainingUsable=" + source.Count);
                }
                catch (Exception e) { report.AppendLine("SelectedFolderFailure=" + e.GetType().Name); }
            }
            return report.ToString();
        }
    }
}
