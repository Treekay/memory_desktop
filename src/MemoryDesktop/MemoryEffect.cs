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
        bool Add(BitmapSource photo, double time, PhotoLease lease);
        void Draw(DrawingContext drawing, Size size, double time);
        void DrawNative(System.Drawing.Graphics drawing, Size size, double time);
        long NativeFrameKey(Size size, double time);
        void SetDwell(PhotoDwell preset);
        void SetViewport(Size size);
        void Clear();
        int ActiveCount { get; }
    }

    internal sealed class MemoryEffect : IPhotoEffect
    {
        internal const double FadeIn = 2.8, FadeOut = 3.8;
        internal const double Lifetime = FadeIn + 6 + FadeOut;
        private sealed class Fragment
        { public BitmapSource Photo; public System.Drawing.Bitmap NativePhoto; public NativeImage Sprite; public PhotoLease Lease; public int SpriteWidth, SpriteHeight; public PhotoPlacement Placement; public double Birth, Life, Phase; }
        private readonly List<Fragment> fragments = new List<Fragment>();
        private readonly Random random;
        private double nextArrival, nextTargetChange;
        private double lastAttempt = Double.NegativeInfinity;
        private int targetCount;
        private Size viewport = new Size(1920, 1080);
        private PhotoDwell dwellPreset;
        public int ActiveCount { get { return fragments.Count; } }
        internal double[] ExpiryTimes { get { return fragments.ConvertAll(fragment => fragment.Birth + fragment.Life).ToArray(); } }
        internal double[] BirthTimes { get { return fragments.ConvertAll(fragment => fragment.Birth).ToArray(); } }
        internal double[] DwellTimes { get { return fragments.ConvertAll(fragment => fragment.Life - FadeIn - FadeOut).ToArray(); } }
        public void SetDwell(PhotoDwell preset) { dwellPreset = preset; }
        public void SetViewport(Size size) { if (size.Width > 0 && size.Height > 0) viewport = size; }
        internal Rect[] Reservations { get { return fragments.ConvertAll(fragment => fragment.Placement.Reservation).ToArray(); } }
        internal Rect[] FrameRects(double time) { return fragments.ConvertAll(fragment => fragment.Placement.Frame(new Size(fragment.Photo.PixelWidth, fragment.Photo.PixelHeight), viewport, time - fragment.Birth, fragment.Phase)).ToArray(); }
        internal int TargetCount { get { return targetCount; } }

        public MemoryEffect(int seed)
        {
            random = new Random(seed);
            ResetSchedule();
        }

        public bool NeedsPhoto(double time)
        {
            RemoveExpired(time);
            if (time >= nextTargetChange) { targetCount = 2 + random.Next(3); nextTargetChange = time + 18 + random.NextDouble() * 16; }
            bool keepVisible = fragments.Count == 1 && fragments[0].Birth + fragments[0].Life - time < FadeIn + 2;
            return fragments.Count < targetCount && fragments.Count < 4 && (time >= nextArrival || (keepVisible && time - lastAttempt >= 2));
        }
        private void RemoveExpired(double time)
        {
            for (int i = fragments.Count - 1; i >= 0; i--)
                if (time - fragments[i].Birth >= fragments[i].Life) { Release(fragments[i]); fragments.RemoveAt(i); }
        }
        internal bool Add(BitmapSource photo, double time) { return Add(photo, time, null); }
        public bool Add(BitmapSource photo, double time, PhotoLease lease)
        {
            if (!NeedsPhoto(time)) return false;
            lastAttempt = time;
            if (photo == null) { nextArrival = time + 2; return false; }
            PhotoPlacement placement;
            if (!PhotoLayout.TryPlace(new Size(photo.PixelWidth, photo.PixelHeight), viewport, Reservations, random, out placement))
            { nextArrival = time + 2 + random.NextDouble() * 2; return false; }
            double minimum, maximum; DwellOptions.Bounds(dwellPreset, out minimum, out maximum);
            double life = FadeIn + minimum + random.NextDouble() * (maximum - minimum) + FadeOut;
            double birth = time;
            // Space arrivals apart while retaining each exact dwell range. A
            // future reservation is still part of collision and memory bounds.
            for (int pass = 0; pass < 4; pass++)
                foreach (var active in fragments)
                    if (Math.Abs(birth + life - active.Birth - active.Life) < 1.2) birth = active.Birth + active.Life + 1.2 - life;
            // Reserve a future arrival when needed, rather than extending the
            // chosen fully-visible dwell beyond its advertised range.
            fragments.Add(new Fragment { Photo = photo, NativePhoto = NativeBitmap.FromSource(photo), Lease = lease, Placement = placement, Birth = birth, Life = life, Phase = random.NextDouble() * Math.PI * 2 });
            // Each occupied region owns its expiry and its next arrival. There is
            // no global batch replacement or common fade clock.
            nextArrival = time + 2.8 + random.NextDouble() * 2.2;
            return true;
        }
        private void ResetSchedule()
        { nextArrival = 0; lastAttempt = Double.NegativeInfinity; targetCount = 2 + random.Next(3); nextTargetChange = 18 + random.NextDouble() * 16; }
        private static void Release(Fragment fragment) { fragment.NativePhoto.Dispose(); if (fragment.Sprite != null) fragment.Sprite.Dispose(); if (fragment.Lease != null) fragment.Lease.Dispose(); }
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
        { return age < 0 || age >= life ? 0 : .96 * Smooth(age / FadeIn) * Smooth((life - age) / FadeOut); }
        public void Draw(DrawingContext drawing, Size size, double time)
        {
            RemoveExpired(time);
            foreach (var fragment in fragments)
            {
                double age = time - fragment.Birth;
                Rect rect = fragment.Placement.Frame(new Size(fragment.Photo.PixelWidth, fragment.Photo.PixelHeight), size, age, fragment.Phase);
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
                Rect rect = fragment.Placement.Frame(new Size(fragment.Photo.PixelWidth, fragment.Photo.PixelHeight), size, age, fragment.Phase);
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
                Rect rect = fragment.Placement.Frame(new Size(fragment.Photo.PixelWidth, fragment.Photo.PixelHeight), size, time - fragment.Birth, fragment.Phase);
                unchecked { key = key * 31 + (long)(fragment.Birth * 1000); key = key * 31 + (int)Math.Round(rect.X); key = key * 31 + (int)Math.Round(rect.Y); key = key * 31 + (int)Math.Round(Opacity(time - fragment.Birth, fragment.Life) * 255); }
            }
            return key;
        }
    }

    internal sealed class PhotoSurface : FrameworkElement
    {
        private readonly IPhotoEffect effect;
        private readonly BackgroundScene background = new BackgroundScene();
        private PhotoDwell dwellPreset;
        internal PhotoDwell DwellPreset
        { get { return dwellPreset; } set { dwellPreset = value; effect.SetDwell(value); } }
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
        internal void SetViewport(Size size) { effect.SetViewport(size); }
        public void Tick(double elapsed)
        {
            if (Suspended) return;
            Time += Math.Min(1, elapsed);
            InvalidateVisual();
            if (FrameInvalidated != null) FrameInvalidated();
            if (!RequestPending && effect.NeedsPhoto(Time) && PhotoRequested != null)
            { RequestPending = true; RequestSerial++; PhotoRequested(this); }
        }
        public void Deliver(BitmapSource photo) { effect.Add(photo, Time, null); RequestPending = false; InvalidateVisual(); if (FrameInvalidated != null) FrameInvalidated(); }
        internal bool DeliverLease(PhotoLease lease)
        { bool accepted = effect.Add(lease == null ? null : lease.Image, Time, lease); RequestPending = false; InvalidateVisual(); if (FrameInvalidated != null) FrameInvalidated(); return accepted; }
        internal BitmapSource Prepare(BitmapSource photo) { return effect.Prepare(photo); }
        public void Reset() { effect.Clear(); Time = 0; RequestSerial++; RequestPending = false; InvalidateVisual(); if (FrameInvalidated != null) FrameInvalidated(); }
        protected override void OnRender(DrawingContext drawing) { effect.SetViewport(RenderSize); background.Draw(drawing, RenderSize, Time); effect.Draw(drawing, RenderSize, Time); }
        internal void DrawNative(System.Drawing.Graphics drawing, int width, int height)
        { var size = new Size(width, height); background.DrawNative(drawing, size, Time); effect.DrawNative(drawing, size, Time); }
        internal long NativeFrameKey(int width, int height)
        { var size = new Size(width, height); unchecked { return (effect.NativeFrameKey(size, Time) * 397 ^ background.NativeFrameKey(size, Time)) * 397 ^ width * 397 ^ height; } }
        internal void Release() { effect.Clear(); background.Dispose(); }
    }
}
