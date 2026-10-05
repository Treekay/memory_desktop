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
            Surface.SetViewport(new System.Windows.Size(display.Bounds.Width, display.Bounds.Height));
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
        private readonly double[] visibleSeconds=new double[5];
        private double lastPaintTime,threeRunStart,fourRunStart,longestThree,longestFour;
        private int lastVisibleCount,threeScenes,fourScenes;
        internal string Metrics { get { return String.Format(System.Globalization.CultureInfo.InvariantCulture, "paintFrames={0} avgPaintMs={1:F2} maxPaintMs={2:F2} clearOpacity=0.60 visibleSeconds0..4={3} threeScenes={4} fourScenes={5} longestThreeSeconds={6:F1} longestFourSeconds={7:F1}", paintedFrames, totalPaintMilliseconds / Math.Max(1, paintedFrames), maximumPaintMilliseconds,String.Join(",",Array.ConvertAll(visibleSeconds,value=>value.ToString("F1",System.Globalization.CultureInfo.InvariantCulture))),threeScenes,fourScenes,longestThree,longestFour); } }
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
            if(surface.Time<lastPaintTime) {Array.Clear(visibleSeconds,0,visibleSeconds.Length);lastPaintTime=surface.Time;lastVisibleCount=0;threeRunStart=surface.Time;fourRunStart=surface.Time;threeScenes=0;fourScenes=0;longestThree=0;longestFour=0;}
            double interval=Math.Max(0,surface.Time-lastPaintTime); visibleSeconds[lastVisibleCount]+=interval;
            int count=surface.VisibleCount;
            if(count>=3 && lastVisibleCount<3) threeScenes++;
            if(count==4 && lastVisibleCount<4) fourScenes++;
            if(count>=3){if(lastVisibleCount<3) threeRunStart=surface.Time;longestThree=Math.Max(longestThree,surface.Time-threeRunStart);}
            if(count==4){if(lastVisibleCount!=4) fourRunStart=surface.Time;longestFour=Math.Max(longestFour,surface.Time-fourRunStart);}
            lastPaintTime=surface.Time;lastVisibleCount=count;
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
