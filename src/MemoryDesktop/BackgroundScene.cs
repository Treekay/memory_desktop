using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MemoryDesktop
{
    // Procedural and cached; the photo surface's pause clock also freezes the glow.
    internal sealed class BackgroundScene : IDisposable
    {
        private BitmapSource backdrop, aurora;
        private Size preparedSize;
        private bool preparedDreamy;
        private NativeImage nativeBackdrop, nativeAurora, nativeComposed;
        private int glowX = Int32.MinValue, glowY, glowAlpha;
        internal bool Dreamy { get; set; }
        private static Brush Haze(Color color)
        { var brush = new RadialGradientBrush(color, Colors.Transparent); brush.Freeze(); return brush; }
        private static BitmapSource Raster(DrawingVisual visual, int width, int height)
        { var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap; }
        private void Prepare(Size size)
        {
            double scale = Math.Min(1, 960 / Math.Max(size.Width, size.Height));
            int width = Math.Max(1, (int)(size.Width * scale)), height = Math.Max(1, (int)(size.Height * scale));
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                var baseBrush = new RadialGradientBrush(Dreamy ? Color.FromRgb(20, 25, 36) : Color.FromRgb(29, 35, 41), Dreamy ? Color.FromRgb(7, 10, 19) : Color.FromRgb(10, 14, 20));
                baseBrush.Center = new Point(.57, .44); baseBrush.GradientOrigin = baseBrush.Center; baseBrush.RadiusX = .8; baseBrush.RadiusY = .9;
                drawing.DrawRectangle(baseBrush, null, new Rect(0, 0, width, height));
                if (Dreamy)
                {
                    drawing.PushTransform(new RotateTransform(-24, width * .55, height * .45));
                    drawing.DrawEllipse(Haze(Color.FromArgb(65, 118, 126, 170)), null, new Point(width * .54, height * .45), width * .55, height * .19);
                    drawing.DrawEllipse(Haze(Color.FromArgb(42, 140, 107, 158)), null, new Point(width * .35, height * .40), width * .35, height * .10);
                    drawing.Pop();
                    var random = new Random(9217);
                    for (int star = 0; star < 120; star++)
                    {
                        double x = random.NextDouble() * width, y = random.NextDouble() * height, radius = .5 + random.NextDouble() * .6;
                        if (star % 6 == 0) drawing.DrawEllipse(Haze(Color.FromArgb(26, 132, 175, 220)), null, new Point(x, y), radius * 3, radius * 3);
                        drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(130 + random.Next(90)), 183, 205, 224)), null, new Point(x, y), radius, radius);
                    }
                }
            }
            backdrop = Raster(visual, width, height); aurora = null;
            if (Dreamy)
            {
                var glow = new DrawingVisual();
                using (var drawing = glow.RenderOpen())
                {
                    drawing.PushTransform(new RotateTransform(-12, width * .55, height * .25));
                    drawing.DrawEllipse(Haze(Color.FromArgb(80, 80, 151, 143)), null, new Point(width * .58, height * .24), width * .40, height * .10);
                    drawing.DrawEllipse(Haze(Color.FromArgb(52, 107, 108, 157)), null, new Point(width * .76, height * .32), width * .32, height * .075);
                    drawing.Pop();
                }
                aurora = Raster(glow, width, height);
            }
            if (nativeBackdrop != null) nativeBackdrop.Dispose(); if (nativeAurora != null) nativeAurora.Dispose();
            if (nativeComposed != null) { nativeComposed.Dispose(); nativeComposed = null; } glowX = Int32.MinValue;
            using (var small = NativeBitmap.FromSource(backdrop))
            {
                using (var full = new System.Drawing.Bitmap((int)size.Width, (int)size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                { using (var graphics = System.Drawing.Graphics.FromImage(full)) graphics.DrawImage(small, new System.Drawing.Rectangle(0, 0, full.Width, full.Height)); nativeBackdrop = new NativeImage(full); }
            }
            nativeAurora = null;
            if (aurora != null)
                using (var small = NativeBitmap.FromSource(aurora))
                using (var full = new System.Drawing.Bitmap((int)size.Width, (int)size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                { using (var graphics = System.Drawing.Graphics.FromImage(full)) graphics.DrawImage(small, new System.Drawing.Rectangle(0, 0, full.Width, full.Height)); nativeAurora = new NativeImage(full); }
            preparedSize = size; preparedDreamy = Dreamy;
        }
        internal void DrawNative(System.Drawing.Graphics drawing, Size size, double time)
        {
            if (backdrop == null || preparedSize != size || preparedDreamy != Dreamy) Prepare(size);
            if (nativeAurora == null) nativeBackdrop.Draw(drawing, 0, 0, 1, true);
            else
            {
                int x = (int)(Math.Sin(time / 90) * size.Width * .012), y = (int)(Math.Cos(time / 103) * size.Height * .008);
                int alpha = (int)Math.Round((.78 + .08 * Math.Sin(time / 67)) * 255);
                if (nativeComposed == null) nativeComposed = new NativeImage((int)size.Width, (int)size.Height);
                if (x != glowX || y != glowY || alpha != glowAlpha)
                { nativeComposed.Compose(nativeBackdrop, nativeAurora, x, y, (byte)alpha); glowX = x; glowY = y; glowAlpha = alpha; }
                nativeComposed.Draw(drawing, 0, 0, 1, true);
            }
        }
        internal long NativeFrameKey(Size size, double time)
        {
            if (!Dreamy) return 0;
            unchecked { return (((int)(Math.Sin(time / 90) * size.Width * .012) * 397L) ^ (int)(Math.Cos(time / 103) * size.Height * .008)) * 397 ^ (int)Math.Round((.78 + .08 * Math.Sin(time / 67)) * 255); }
        }
        public void Dispose()
        { if (nativeBackdrop != null) nativeBackdrop.Dispose(); if (nativeAurora != null) nativeAurora.Dispose(); if (nativeComposed != null) nativeComposed.Dispose(); nativeBackdrop = null; nativeAurora = null; nativeComposed = null; backdrop = null; aurora = null; }
        internal void Draw(DrawingContext drawing, Size size, double time)
        {
            if (size.Width <= 0 || size.Height <= 0) return;
            if (backdrop == null || preparedSize != size || preparedDreamy != Dreamy) Prepare(size);
            drawing.DrawImage(backdrop, new Rect(size));
            if (aurora != null)
            {
                drawing.PushOpacity(.78 + .08 * Math.Sin(time / 67));
                drawing.DrawImage(aurora, new Rect(Math.Sin(time / 90) * size.Width * .012, Math.Cos(time / 103) * size.Height * .008, size.Width, size.Height));
                drawing.Pop();
            }
        }
    }
}
