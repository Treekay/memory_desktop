using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MemoryDesktop
{
    internal sealed class WallpaperController : IDisposable
    {
        private readonly Application app;
        private readonly bool preview;
        private readonly string[] args;
        private readonly List<WallpaperWindow> windows = new List<WallpaperWindow>();
        private readonly SemaphoreSlim decoder = new SemaphoreSlim(1, 1);
        private readonly PhotoLeases leases = new PhotoLeases();
        private readonly DispatcherTimer frames = new DispatcherTimer(DispatcherPriority.Background);
        private readonly DispatcherTimer maintenance = new DispatcherTimer(DispatcherPriority.Background);
        private readonly DispatcherTimer rescanTimer = new DispatcherTimer(DispatcherPriority.Background);
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly Stopwatch previewElapsed = Stopwatch.StartNew();
        private AppSettings settings;
        private PhotoSource source;
        private CancellationTokenSource sourceCancellation = new CancellationTokenSource();
        private CancellationTokenSource scanCancellation = new CancellationTokenSource();
        private Forms.NotifyIcon tray;
        private Forms.ToolStripMenuItem pauseItem, startupItem, folderItem, darkBackgroundItem, dreamyBackgroundItem, dwellMenu;
        private FileSystemWatcher watcher;
        private ShellMessages messages;
        private IntPtr host;
        private int generation, scanRevision;
        private bool disposed, sessionLocked, powerSuspended, hostErrorShown, noUsablePhotosShown, creatingWindows;
        private double lastFrame, previewSeconds = 40, nextCapture = 4;
        private int captureIndex;
        private string captureDirectory;

        public WallpaperController(Application application, string[] arguments, bool isPreview)
        { app = application; args = arguments; preview = isPreview; }

        public void Start()
        {
            settings = preview ? new AppSettings() : AppSettings.Load(AppSettings.DefaultPath);
            if (!preview) Diagnostics.Log("Start version=" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + " dwellPreset=" + settings.DwellPreset);
            frames.Interval = TimeSpan.FromMilliseconds(1000.0 / 30);
            frames.Tick += Frame;
            maintenance.Interval = TimeSpan.FromSeconds(3);
            maintenance.Tick += Maintain;
            rescanTimer.Interval = TimeSpan.FromSeconds(2);
            rescanTimer.Tick += delegate { rescanTimer.Stop(); if (!String.IsNullOrEmpty(settings.PhotoFolder)) SelectFolder(settings.PhotoFolder, false); };
            if (preview)
            {
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] == "--seconds" && i + 1 < args.Length) { double value; if (Double.TryParse(args[++i], out value)) previewSeconds = Math.Max(5, Math.Min(300, value)); }
                    else if (args[i] == "--capture" && i + 1 < args.Length) captureDirectory = args[++i];
                    else if (args[i] == "--dreamy") settings.DreamyBackground = true;
                }
                if (captureDirectory != null) Directory.CreateDirectory(captureDirectory);
                CreateWindows(); SelectFolder(args[1], true);
            }
            else
            {
                CreateTray(); messages = new ShellMessages(); messages.ExplorerRestarted += OnExplorerRestart;
                SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
                SystemEvents.SessionSwitch += OnSessionSwitch;
                SystemEvents.PowerModeChanged += OnPowerChanged;
                if (!String.IsNullOrEmpty(settings.PhotoFolder)) SelectFolder(settings.PhotoFolder, true);
                else tray.ShowBalloonTip(5000, "Memory Desktop", "右键托盘图标，选择照片文件夹后开始。", Forms.ToolTipIcon.Info);
            }
            frames.Start(); maintenance.Start();
        }

        private void CreateTray()
        {
            var menu = new Forms.ContextMenuStrip();
            folderItem = new Forms.ToolStripMenuItem("选择照片文件夹…", null, delegate { PickFolder(); });
            pauseItem = new Forms.ToolStripMenuItem("暂停", null, delegate { settings.Paused = !settings.Paused; SaveSettings(); UpdateMenu(); });
            startupItem = new Forms.ToolStripMenuItem("登录 Windows 时启动") { CheckOnClick = false };
            startupItem.Click += delegate
            {
                try
                {
                    bool enabled = !LoginStartup.IsEnabled(); LoginStartup.SetEnabled(enabled);
                    settings.StartAtLogin = enabled; SaveSettings(); UpdateMenu();
                }
                catch (Exception e) { ShowError("无法更改登录启动设置。", e); }
            };
            menu.Items.Add(folderItem); menu.Items.Add(pauseItem); menu.Items.Add(new Forms.ToolStripSeparator());
            var backgroundMenu = new Forms.ToolStripMenuItem("背景");
            darkBackgroundItem = new Forms.ToolStripMenuItem("深色留白", null, delegate { SetBackground(false); });
            dreamyBackgroundItem = new Forms.ToolStripMenuItem("梦境星空", null, delegate { SetBackground(true); });
            backgroundMenu.DropDownItems.Add(darkBackgroundItem); backgroundMenu.DropDownItems.Add(dreamyBackgroundItem);
            menu.Items.Add(backgroundMenu);
            dwellMenu = DwellOptions.CreateMenu(SetDwellPreset); menu.Items.Add(dwellMenu);
            menu.Items.Add(startupItem); menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { app.Shutdown(); });
            menu.Opening += delegate { UpdateMenu(); };
            tray = new Forms.NotifyIcon { Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location), Text = "Memory Desktop", ContextMenuStrip = menu, Visible = true };
            UpdateMenu();
        }
        private void UpdateMenu()
        {
            if (tray == null) return;
            pauseItem.Text = settings.Paused ? "继续" : "暂停";
            pauseItem.Checked = settings.Paused;
            darkBackgroundItem.Checked = !settings.DreamyBackground; dreamyBackgroundItem.Checked = settings.DreamyBackground;
            DwellOptions.UpdateChecks(dwellMenu, settings.DwellPreset);
            try { startupItem.Checked = LoginStartup.IsEnabled(); } catch { startupItem.Checked = settings.StartAtLogin; }
            folderItem.ToolTipText = settings.PhotoFolder ?? "尚未选择文件夹";
            tray.Text = settings.Paused ? "Memory Desktop · 已暂停" : "Memory Desktop";
        }
        private void SaveSettings()
        { if (preview) return; try { settings.Save(AppSettings.DefaultPath); } catch (Exception e) { ShowError("无法保存本地设置。", e); } }
        private void SetBackground(bool dreamy)
        { settings.DreamyBackground = dreamy; foreach (var window in windows) window.Surface.DreamyBackground = dreamy; SaveSettings(); UpdateMenu(); }
        private void SetDwellPreset(PhotoDwell preset)
        { settings.DwellPreset = preset; foreach (var window in windows) window.Surface.DwellPreset = preset; SaveSettings(); UpdateMenu(); }
        private void PickFolder()
        {
            using (var dialog = new Forms.FolderBrowserDialog { Description = "选择本地照片文件夹（只读取该文件夹，不包含子文件夹）", ShowNewFolderButton = false })
            {
                if (Directory.Exists(settings.PhotoFolder)) dialog.SelectedPath = settings.PhotoFolder;
                if (dialog.ShowDialog() == Forms.DialogResult.OK) SelectFolder(dialog.SelectedPath, true);
            }
        }

        private async void SelectFolder(string folder, bool replace)
        {
            int revision = ++scanRevision;
            scanCancellation.Cancel(); scanCancellation.Dispose(); scanCancellation = new CancellationTokenSource();
            var cancellation = scanCancellation.Token;
            try
            {
                PhotoSource selected = await Task.Run(() => PhotoSource.Scan(folder, cancellation, Environment.TickCount));
                if (disposed || revision != scanRevision) return;
                sourceCancellation.Cancel(); sourceCancellation.Dispose(); sourceCancellation = new CancellationTokenSource();
                generation++; source = selected;
                if (!preview) Diagnostics.Log("Source selected count=" + selected.Count + " replacement=" + replace);
                if (selected.Count > 0) noUsablePhotosShown = false;
                settings.PhotoFolder = folder;
                if (replace)
                {
                    foreach (var window in windows) window.Surface.Reset();
                    SaveSettings(); WatchFolder(folder);
                }
                if (windows.Count == 0 && selected.Count > 0) CreateWindows();
                if (replace && selected.Count == 0 && !preview)
                    tray.ShowBalloonTip(5000, "没有可用照片", "该文件夹没有支持的照片。添加 JPG、PNG 等图片后会自动重试。", Forms.ToolTipIcon.Info);
                if (replace && selected.WasTruncated && !preview)
                    tray.ShowBalloonTip(5000, "大型照片文件夹", "每次随机取样最多 4096 个文件，控制内存占用。", Forms.ToolTipIcon.Info);
                UpdateMenu();
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (disposed || revision != scanRevision) return;
                if (replace) ShowError("无法读取照片文件夹，请重新选择。", e);
            }
        }

        private void WatchFolder(string folder)
        {
            if (watcher != null) { watcher.Dispose(); watcher = null; }
            try
            {
                watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, InternalBufferSize = 8192 };
                FileSystemEventHandler changed = delegate { QueueRescan(); };
                watcher.Created += changed; watcher.Deleted += changed; watcher.Changed += changed;
                watcher.Renamed += delegate { QueueRescan(); }; watcher.Error += delegate { QueueRescan(); };
                watcher.EnableRaisingEvents = true;
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        private void QueueRescan()
        {
            app.Dispatcher.BeginInvoke(new Action(delegate { if (!disposed) { rescanTimer.Stop(); rescanTimer.Start(); } }));
        }

        private void CreateWindows()
        {
            if (creatingWindows) return;
            creatingWindows = true;
            try
            {
            if (!preview)
            {
                host = NativeDesktop.FindHost(true);
                Diagnostics.Log("CreateWindows host=" + NativeDesktop.Describe(host));
                if (!NativeDesktop.IsExplorerHost(host))
                {
                    if (!hostErrorShown) { hostErrorShown = true; ShowError("找不到图标后方的 Explorer 壁纸宿主。程序保留在托盘并自动重试；不会显示覆盖桌面的窗口。", null); }
                    return;
                }
            }
            var displays = preview ? new[] { Forms.Screen.PrimaryScreen } : Forms.Screen.AllScreens.Take(8).ToArray();
            try
            {
                foreach (var display in displays)
                {
                    var window = new WallpaperWindow(display, preview, Environment.TickCount + windows.Count * 179, !preview && NativeDesktop.ClassName(host) == "Progman", host);
                    window.Surface.PhotoRequested += RequestPhoto;
                    window.Surface.DreamyBackground = settings.DreamyBackground;
                    window.Surface.DwellPreset = settings.DwellPreset;
                    windows.Add(window);
                    if (preview) window.Closed += delegate { if (!disposed) app.Shutdown(); };
                    window.Show();
                    if (!preview) NativeDesktop.Attach(window.Handle, host, display.Bounds);
                    if (!preview) Diagnostics.Log("Attached display=" + display.Bounds + " window=" + NativeDesktop.Describe(window.Handle));
                }
                hostErrorShown = false;
            }
            catch (Exception e) { CloseWindows(); if (!hostErrorShown) { hostErrorShown = true; ShowError("无法创建桌面壁纸。", e); } }
            }
            finally { creatingWindows = false; }
        }
        private async void RequestPhoto(PhotoSurface surface)
        {
            PhotoSource selected = source; int requestGeneration = generation;
            int requestSerial = surface.RequestSerial;
            var cancellation = sourceCancellation.Token;
            BitmapSource image = null;
            PhotoLease lease = null;
            try
            {
                if (selected != null)
                {
                    await decoder.WaitAsync(cancellation);
                    try
                    {
                        lease = await Task.Run(() => selected.NextLease(leases, cancellation));
                        if (lease != null) { lease.Image = await Task.Run(() => surface.Prepare(lease.Image)); image = lease.Image; }
                    }
                    finally { decoder.Release(); }
                }
                if (!disposed && requestGeneration == generation && requestSerial == surface.RequestSerial && windows.Any(window => window.Surface == surface))
                {
                    if (surface.DeliverLease(lease)) lease = null;
                    if (!preview) Diagnostics.Log("Delivered generation=" + generation + " decoded=" + (image != null) + " photoPixels=" + (image == null ? "none" : image.PixelWidth + "x" + image.PixelHeight));
                    if (image == null && selected != null && selected.Count == 0 && !preview && !noUsablePhotosShown)
                    {
                        noUsablePhotosShown = true;
                        tray.ShowBalloonTip(5000, "没有可解码的照片", "照片可能已移除、损坏或超过大小限制。可添加图片或右键选择其他文件夹。", Forms.ToolTipIcon.Info);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (!disposed && requestGeneration == generation && requestSerial == surface.RequestSerial)
                { surface.Deliver(null); ShowError("无法解码照片。", e); }
            }
            finally
            {
                if (lease != null) lease.Dispose();
                if (!disposed && requestSerial == surface.RequestSerial) surface.RequestPending = false;
            }
        }

        private void Frame(object sender, EventArgs e)
        {
            double now = elapsed.Elapsed.TotalSeconds, delta = now - lastFrame; lastFrame = now;
            foreach (var window in windows)
            { window.Surface.Suspended = settings.Paused || sessionLocked || powerSuspended || (!preview && NativeDesktop.IsCovered(window.Display)); window.Surface.Tick(delta); }
            if (!preview && windows.Any(window => !window.Surface.Suspended))
                frames.Interval = TimeSpan.FromMilliseconds(delta > .5 ? 1000.0 / 30 : Math.Max(1, Math.Min(1000.0 / 30, frames.Interval.TotalMilliseconds + 1000.0 / 30 - delta * 1000)));
            if (preview)
            {
                if (captureDirectory != null && previewElapsed.Elapsed.TotalSeconds >= nextCapture)
                { CapturePreview(); nextCapture += 12; }
                if (previewElapsed.Elapsed.TotalSeconds >= previewSeconds) app.Shutdown();
            }
        }
        private void CapturePreview()
        {
            if (windows.Count == 0) return;
            var surface = windows[0].Surface;
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)surface.ActualWidth), Math.Max(1, (int)surface.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            string name = "preview-" + (++captureIndex).ToString("00") + ".png";
            using (var output = File.Create(Path.Combine(captureDirectory, name))) png.Save(output);
            using (var process = Process.GetCurrentProcess())
                File.AppendAllText(Path.Combine(captureDirectory, "preview-metrics.txt"), String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1}s fragments={1} workingSetMB={2:F1} handles={3}\r\n", previewElapsed.Elapsed.TotalSeconds, surface.ActiveCount, process.WorkingSet64 / 1048576.0, process.HandleCount));
        }
        private int maintenanceCount;
        private void Maintain(object sender, EventArgs e)
        {
            if (preview || disposed) return;
            if (maintenanceCount % 5 == 0)
                foreach (var window in windows) Diagnostics.Log("State time=" + window.Surface.Time.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " fragments=" + window.Surface.ActiveCount + " globalLeases=" + leases.ActiveCount + " suspended=" + window.Surface.Suspended + " pending=" + window.Surface.RequestPending + " window=" + NativeDesktop.Describe(window.Handle) + " " + window.PaintMetrics);
            bool active = !settings.Paused && !sessionLocked && !powerSuspended && windows.Any(window => !NativeDesktop.IsCovered(window.Display));
            frames.Interval = TimeSpan.FromMilliseconds(active ? 1000.0 / 30 : 1000);
            if (source != null && source.Count > 0 && (!NativeDesktop.IsExplorerHost(host) || windows.Count == 0 || windows.Any(window => !NativeDesktop.IsWindow(window.Handle))))
            { CloseWindows(); CreateWindows(); }
            // Recheck selected folder periodically: removable disks and watcher overflow recover.
            if (++maintenanceCount % 40 == 0 && !sessionLocked && !powerSuspended && !String.IsNullOrEmpty(settings.PhotoFolder)) SelectFolder(settings.PhotoFolder, false);
        }
        private void OnExplorerRestart()
        { app.Dispatcher.BeginInvoke(new Action(delegate { if (!disposed) { CloseWindows(); host = IntPtr.Zero; } })); }
        private void OnDisplayChanged(object sender, EventArgs e)
        { app.Dispatcher.BeginInvoke(new Action(delegate { if (!disposed) { CloseWindows(); if (source != null && source.Count > 0) CreateWindows(); } })); }
        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        { app.Dispatcher.BeginInvoke(new Action(delegate { if (e.Reason == SessionSwitchReason.SessionLock) sessionLocked = true; else if (e.Reason == SessionSwitchReason.SessionUnlock) sessionLocked = false; })); }
        private void OnPowerChanged(object sender, PowerModeChangedEventArgs e)
        { app.Dispatcher.BeginInvoke(new Action(delegate { if (e.Mode == PowerModes.Suspend) powerSuspended = true; else if (e.Mode == PowerModes.Resume) { powerSuspended = false; lastFrame = elapsed.Elapsed.TotalSeconds; } })); }
        private void CloseWindows()
        {
            foreach (var window in windows) { window.Surface.PhotoRequested -= RequestPhoto; window.Surface.Reset(); window.Close(); }
            windows.Clear();
        }
        private void ShowError(string message, Exception error)
        {
            if (!preview) Diagnostics.Log("Error type=" + (error == null ? "HostUnavailable" : error.GetType().Name) + " hresult=" + (error == null ? 0 : error.HResult));
            MessageBox.Show(message + (error == null ? "" : "\n" + error.Message), "Memory Desktop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (!preview) Diagnostics.Log("Exit requested; releasing wallpaper windows and tray.");
            frames.Stop(); maintenance.Stop(); rescanTimer.Stop();
            sourceCancellation.Cancel(); scanCancellation.Cancel();
            sourceCancellation.Dispose(); scanCancellation.Dispose();
            if (watcher != null) watcher.Dispose();
            if (!preview)
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch; SystemEvents.PowerModeChanged -= OnPowerChanged;
            }
            if (messages != null) messages.Dispose();
            CloseWindows(); source = null;
            if (tray != null) { tray.Visible = false; var icon = tray.Icon; var menu = tray.ContextMenuStrip; tray.Dispose(); icon.Dispose(); menu.Dispose(); }
        }
    }

    // Hidden top-level receiver gets Explorer's TaskbarCreated broadcast; never visible.
    internal sealed class ShellMessages : Forms.NativeWindow, IDisposable
    {
        private readonly uint taskbarCreated = NativeDesktop.RegisterWindowMessage("TaskbarCreated");
        public event Action ExplorerRestarted;
        public ShellMessages() { CreateHandle(new Forms.CreateParams { Caption = "MemoryDesktop.ShellEvents", Style = 0, ExStyle = 0x80 }); }
        protected override void WndProc(ref Forms.Message message)
        { if ((uint)message.Msg == taskbarCreated && ExplorerRestarted != null) ExplorerRestarted(); base.WndProc(ref message); }
        public void Dispose() { DestroyHandle(); }
    }
}
