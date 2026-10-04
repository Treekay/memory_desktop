using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace MemoryDesktop
{
    // WPF retains the effect engine. A native GDI presenter provides a compatible
    // layered child surface for Explorer's raised desktop composition.
    internal sealed class WallpaperWindow
    {
        internal PhotoSurface Surface { get; private set; }
        internal IntPtr Handle { get; private set; }
        internal Forms.Screen Display { get; private set; }
        internal string PaintMetrics { get { return presenter == null ? "preview" : presenter.Metrics; } }
        private readonly Window previewWindow;
        private readonly DesktopPresenter presenter;
        internal event EventHandler Closed;
        internal WallpaperWindow(Forms.Screen display, bool preview, int seed, bool layered, IntPtr host)
        {
            Display = display;
            Surface = new PhotoSurface(new MemoryEffect(seed));
            if (preview)
            {
                previewWindow = new Window { Title = "Memory Desktop — 临时预览", Width = 1100, Height = 680,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = Surface,
                    Background = System.Windows.Media.Brushes.Black };
                previewWindow.Closed += delegate { if (Closed != null) Closed(this, EventArgs.Empty); };
            }
            else presenter = new DesktopPresenter(Surface, display.Bounds.Size, layered, host);
        }
        internal void Show()
        {
            if (previewWindow != null) { previewWindow.Show(); Handle = new WindowInteropHelper(previewWindow).Handle; }
            else { presenter.Show(); Handle = presenter.Handle; }
        }
        internal void Close()
        { if (previewWindow != null) previewWindow.Close(); else { presenter.Close(); presenter.Dispose(); } Surface.Release(); }
    }

    internal sealed class DesktopPresenter : Forms.Form
    {
        private readonly PhotoSurface surface;
        private readonly bool layered;
        private readonly IntPtr desktopHost;
        private long paintedFrames;
        private long lastFrameKey = Int64.MinValue;
        private double totalPaintMilliseconds, maximumPaintMilliseconds;
        internal string Metrics { get { return String.Format(System.Globalization.CultureInfo.InvariantCulture, "paintFrames={0} avgPaintMs={1:F2} maxPaintMs={2:F2}", paintedFrames, totalPaintMilliseconds / Math.Max(1, paintedFrames), maximumPaintMilliseconds); } }
        internal DesktopPresenter(PhotoSurface photoSurface, System.Drawing.Size displaySize, bool useLayered, IntPtr host)
        {
            surface = photoSurface; layered = useLayered; desktopHost = host;
            AutoScaleMode = Forms.AutoScaleMode.None;
            FormBorderStyle = Forms.FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = Forms.FormStartPosition.Manual;
            Location = new System.Drawing.Point(-32000, -32000);
            ClientSize = displaySize;
            SetStyle(Forms.ControlStyles.UserPaint | Forms.ControlStyles.AllPaintingInWmPaint | Forms.ControlStyles.OptimizedDoubleBuffer | Forms.ControlStyles.Opaque, true);
            surface.FrameInvalidated += InvalidateFrame;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override Forms.CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                // Create in Explorer's hierarchy from the outset so DWM redirects
                // this child correctly on raised Windows 11 desktops.
                parameters.Parent = desktopHost;
                parameters.Style = (parameters.Style & ~unchecked((int)0x80000000)) | 0x40000000;
                parameters.ExStyle |= 0x00000080 | 0x08000000 | 0x00000020 | (layered ? 0x00080000 : 0);
                return parameters;
            }
        }
        private void InvalidateFrame()
        {
            if (IsDisposed) return;
            long key = surface.NativeFrameKey(ClientSize.Width, ClientSize.Height);
            if (key != lastFrameKey) { lastFrameKey = key; Invalidate(); }
        }
        protected override void OnPaintBackground(Forms.PaintEventArgs e) { }
        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            if (ClientSize.Width < 1 || ClientSize.Height < 1) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            e.Graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            surface.DrawNative(e.Graphics, ClientSize.Width, ClientSize.Height);
            double paintMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            paintedFrames++; totalPaintMilliseconds += paintMilliseconds; maximumPaintMilliseconds = Math.Max(maximumPaintMilliseconds, paintMilliseconds);
        }
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            { surface.FrameInvalidated -= InvalidateFrame; }
            base.Dispose(disposing);
        }
    }
}
