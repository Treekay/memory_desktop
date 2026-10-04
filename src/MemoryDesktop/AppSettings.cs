using System;
using System.IO;
using System.Xml.Serialization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MemoryDesktop
{
    public sealed class AppSettings
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existing, string destination, uint flags);
        public string PhotoFolder { get; set; }
        public bool Paused { get; set; }
        public bool StartAtLogin { get; set; }
        public bool DreamyBackground { get; set; }
        public PhotoDwell DwellPreset { get; set; }
        public static string DefaultPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoryDesktop", "settings.xml"); } }

        public static AppSettings Load(string path)
        {
            try { using (var input = File.OpenRead(path)) return (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(input); }
            catch (Exception e) { if (!(e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)) throw; return new AppSettings(); }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                { new XmlSerializer(typeof(AppSettings)).Serialize(output, this); output.Flush(true); }
                // Atomic same-directory rename; avoids File.Replace's ACL/metadata merge.
                if (!MoveFileEx(temporary, path, 1 | 8)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal static class LoginStartup
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "MemoryDesktop";
        // Registry changes happen only in response to an explicit tray toggle.
        public static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Key))
                return key != null && String.Equals(key.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
        internal static string Command { get { return "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\""; } }
        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            { if (enabled) key.SetValue(Name, Command, RegistryValueKind.String); else key.DeleteValue(Name, false); }
        }
    }
}
