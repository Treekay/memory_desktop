using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MemoryDesktop
{
    internal interface IPhotoEffect
    {
        bool NeedsPhoto(double time);
        BitmapSource Prepare(BitmapSource photo);
        void Add(BitmapSource photo, double time);
        void Draw(DrawingContext drawing, Size size, double time);
        void DrawNative(System.Drawing.Graphics drawing, Size size, double time);
        long NativeFrameKey(Size size, double time);
        void Clear();
        int ActiveCount { get; }
    }

    internal sealed class MemoryEffect : IPhotoEffect
    {
        internal const double Lifetime = 18;
        private sealed class Fragment
        { public BitmapSource Photo; public System.Drawing.Bitmap NativePhoto; public NativeImage Sprite; public int SpriteWidth, SpriteHeight; public int Slot; public double Birth, Life, X, Y, Phase, Scale; }
        private readonly List<Fragment> fragments = new List<Fragment>();
        private readonly Random random;
        private readonly double[] due = new double[2];
        private static readonly Rect[] regions = { new Rect(.07, .10, .41, .76), new Rect(.53, .16, .40, .76) };
        public int ActiveCount { get { return fragments.Count; } }
        internal double[] ExpiryTimes { get { return fragments.ConvertAll(fragment => fragment.Birth + fragment.Life).ToArray(); } }

        public MemoryEffect(int seed)
        {
            random = new Random(seed);
            ResetSchedule();
        }

        public bool NeedsPhoto(double time)
        { RemoveExpired(time); return NextSlot(time) >= 0; }
        private void RemoveExpired(double time)
        {
            for (int i = fragments.Count - 1; i >= 0; i--)
                if (time - fragments[i].Birth >= fragments[i].Life) { Release(fragments[i]); fragments.RemoveAt(i); }
        }
        private int NextSlot(double time)
        {
            int selected = -1;
            for (int slot = 0; slot < due.Length; slot++)
                if (time >= due[slot] && !fragments.Exists(fragment => fragment.Slot == slot) && (selected < 0 || due[slot] < due[selected])) selected = slot;
            return selected;
        }
        public void Add(BitmapSource photo, double time)
        {
            int slot = NextSlot(time); if (slot < 0) return;
            if (photo == null) { due[slot] = time + 30; return; }
            double life = Lifetime + random.NextDouble() * 6;
            foreach (var active in fragments)
                if (Math.Abs(time + life - active.Birth - active.Life) < 6) life = active.Birth + active.Life + 7 - time;
            fragments.Add(new Fragment { Photo = photo, NativePhoto = NativeBitmap.FromSource(photo), Slot = slot, Birth = time, Life = life,
                X = random.NextDouble(), Y = random.NextDouble(), Scale = .82 + random.NextDouble() * .16, Phase = random.NextDouble() * Math.PI * 2 });
            // Each occupied region owns its expiry and its next arrival. There is
            // no global batch replacement or common fade clock.
            due[slot] = time + life + .6 + random.NextDouble() * 1.2;
        }
        private void ResetSchedule()
        { int first = random.Next(2); due[first] = 0; due[(first + 1) % 2] = 7 + random.NextDouble() * 2; }
        private static void Release(Fragment fragment) { fragment.NativePhoto.Dispose(); if (fragment.Sprite != null) fragment.Sprite.Dispose(); }
        public void Clear() { foreach (var fragment in fragments) Release(fragment); fragments.Clear(); ResetSchedule(); }
        public BitmapSource Prepare(BitmapSource photo)
        {
            if (photo == null) return null;
            // CPU preparation runs in the decode worker; only the outer edges are
            // feathered. Interior pixels retain their original sharpness.
            int width = photo.PixelWidth, height = photo.PixelHeight, stride = width * 4;
            var converted = new FormatConvertedBitmap(photo, PixelFormats.Pbgra32, null, 0);
            byte[] pixels = new byte[stride * height]; converted.CopyPixels(pixels, stride, 0);
            double[] horizontal = new double[width];
            for (int x = 0; x < width; x++) horizontal[x] = Smooth(Math.Min(x, width - 1 - x) / Math.Max(1.0, width * .055));
            for (int y = 0; y < height; y++)
            {
                double vertical = Smooth(Math.Min(y, height - 1 - y) / Math.Max(1.0, height * .055));
                for (int x = 0; x < width; x++)
                {
                    double mask = horizontal[x] * vertical; if (mask >= 1) continue;
                    int offset = y * stride + x * 4;
                    for (int channel = 0; channel < 4; channel++) pixels[offset + channel] = (byte)(pixels[offset + channel] * mask);
                }
            }
            var prepared = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            prepared.Freeze(); return prepared;
        }
        internal static double Smooth(double value) { value = Math.Max(0, Math.Min(1, value)); return value * value * (3 - 2 * value); }
        internal static double Opacity(double age)
        { return Opacity(age, Lifetime); }
        internal static double Opacity(double age, double life)
        { return age < 0 || age >= life ? 0 : .96 * Smooth(age / 2.8) * Smooth((life - age) / 3.8); }
        internal static Rect Layout(int slot, Size photo, Size viewport, double x, double y, double sizeFactor, double age, double phase)
        {
            Rect region = regions[slot];
            var reserve = new Rect(region.X * viewport.Width, region.Y * viewport.Height, region.Width * viewport.Width, region.Height * viewport.Height);
            // Reserve the full swept rectangle, including maximum zoom, drift and
            // a visible gutter. Regions stay reserved until complete disappearance.
            double dx = Math.Min(12, viewport.Width * .008), dy = Math.Min(9, viewport.Height * .008);
            double marginX = viewport.Width * .012 + dx, marginY = viewport.Height * .012 + dy;
            double scale = Math.Min(Math.Max(1, reserve.Width - 2 * marginX) / photo.Width, Math.Max(1, reserve.Height - 2 * marginY) / photo.Height) * sizeFactor / 1.015;
            double maxWidth = photo.Width * scale * 1.015, maxHeight = photo.Height * scale * 1.015;
            double centerX = reserve.Left + marginX + maxWidth / 2 + x * Math.Max(0, reserve.Width - 2 * marginX - maxWidth);
            double centerY = reserve.Top + marginY + maxHeight / 2 + y * Math.Max(0, reserve.Height - 2 * marginY - maxHeight);
            const double zoom = 1; // fixed sampling avoids shimmer; slow drift carries the motion
            return new Rect(centerX - photo.Width * scale * zoom / 2 + Math.Sin(age / 36 + phase) * dx,
                centerY - photo.Height * scale * zoom / 2 + Math.Cos(age / 42 + phase) * dy, photo.Width * scale * zoom, photo.Height * scale * zoom);
        }

        public void Draw(DrawingContext drawing, Size size, double time)
        {
            RemoveExpired(time);
            foreach (var fragment in fragments)
            {
                double age = time - fragment.Birth;
                Rect rect = Layout(fragment.Slot, new Size(fragment.Photo.PixelWidth, fragment.Photo.PixelHeight), size, fragment.X, fragment.Y, fragment.Scale, age, fragment.Phase);
                drawing.PushOpacity(Opacity(age, fragment.Life));
                drawing.PushTransform(new TranslateTransform(rect.X, rect.Y));
                drawing.DrawImage(fragment.Photo, new Rect(0, 0, rect.Width, rect.Height));
                drawing.Pop(); drawing.Pop();
            }
        }
        public void DrawNative(System.Drawing.Graphics drawing, Size size, double time)
        {
            RemoveExpired(time);
            foreach (var fragment in fragments)
            {
                double age = time - fragment.Birth;
                Rect rect = Layout(fragment.Slot, new Size(fragment.Photo.PixelWidth, fragment.Photo.PixelHeight), size, fragment.X, fragment.Y, fragment.Scale, age, fragment.Phase);
                int width = Math.Max(1, (int)Math.Round(rect.Width)), height = Math.Max(1, (int)Math.Round(rect.Height));
                if (fragment.Sprite == null || fragment.SpriteWidth != width || fragment.SpriteHeight != height)
                {
                    if (fragment.Sprite != null) fragment.Sprite.Dispose();
                    using (var scaled = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                    {
                        using (var graphics = System.Drawing.Graphics.FromImage(scaled))
                        { graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; graphics.DrawImage(fragment.NativePhoto, new System.Drawing.Rectangle(0, 0, width, height)); }
                        fragment.Sprite = new NativeImage(scaled);
                    }
                    fragment.SpriteWidth = width; fragment.SpriteHeight = height;
                }
                fragment.Sprite.Draw(drawing, (int)Math.Round(rect.X), (int)Math.Round(rect.Y), Opacity(age, fragment.Life), false);
            }
        }
        public long NativeFrameKey(Size size, double time)
        {
            long key = fragments.Count;
            foreach (var fragment in fragments)
            {
                Rect rect = Layout(fragment.Slot, new Size(fragment.Photo.PixelWidth, fragment.Photo.PixelHeight), size, fragment.X, fragment.Y, fragment.Scale, time - fragment.Birth, fragment.Phase);
                unchecked { key = key * 31 + (long)(fragment.Birth * 1000); key = key * 31 + (int)Math.Round(rect.X); key = key * 31 + (int)Math.Round(rect.Y); key = key * 31 + (int)Math.Round(Opacity(time - fragment.Birth, fragment.Life) * 255); }
            }
            return key;
        }
    }

    internal sealed class PhotoSurface : FrameworkElement
    {
        private readonly IPhotoEffect effect;
        private readonly BackgroundScene background = new BackgroundScene();
        internal bool DreamyBackground
        { get { return background.Dreamy; } set { background.Dreamy = value; InvalidateVisual(); if (FrameInvalidated != null) FrameInvalidated(); } }
        internal double Time { get; private set; }
        public event Action<PhotoSurface> PhotoRequested;
        internal event Action FrameInvalidated;
        internal bool RequestPending { get; set; }
        internal int RequestSerial { get; private set; }
        internal bool Suspended { get; set; }
        internal int ActiveCount { get { return effect.ActiveCount; } }
        public PhotoSurface(IPhotoEffect photoEffect) { effect = photoEffect; IsHitTestVisible = false; ClipToBounds = true; }
        public void Tick(double elapsed)
        {
            if (Suspended) return;
            Time += Math.Min(1, elapsed);
            InvalidateVisual();
            if (FrameInvalidated != null) FrameInvalidated();
            if (!RequestPending && effect.NeedsPhoto(Time) && PhotoRequested != null)
            { RequestPending = true; RequestSerial++; PhotoRequested(this); }
        }
        public void Deliver(BitmapSource photo) { effect.Add(photo, Time); RequestPending = false; InvalidateVisual(); if (FrameInvalidated != null) FrameInvalidated(); }
        internal BitmapSource Prepare(BitmapSource photo) { return effect.Prepare(photo); }
        public void Reset() { effect.Clear(); Time = 0; RequestSerial++; RequestPending = false; InvalidateVisual(); if (FrameInvalidated != null) FrameInvalidated(); }
        protected override void OnRender(DrawingContext drawing) { background.Draw(drawing, RenderSize, Time); effect.Draw(drawing, RenderSize, Time); }
        internal void DrawNative(System.Drawing.Graphics drawing, int width, int height)
        { var size = new Size(width, height); background.DrawNative(drawing, size, Time); effect.DrawNative(drawing, size, Time); }
        internal long NativeFrameKey(int width, int height)
        { var size = new Size(width, height); unchecked { return (effect.NativeFrameKey(size, Time) * 397 ^ background.NativeFrameKey(size, Time)) * 397 ^ width * 397 ^ height; } }
        internal void Release() { effect.Clear(); background.Dispose(); }
    }
}
