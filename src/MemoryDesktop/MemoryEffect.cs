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
        void Add(BitmapSource photo, double time);
        void Draw(DrawingContext drawing, Size size, double time);
        void Clear();
        int ActiveCount { get; }
    }

    internal sealed class MemoryEffect : IPhotoEffect
    {
        internal const double Lifetime = 46;
        internal const double Interval = 18;
        private sealed class Fragment
        { public BitmapSource Photo; public double Birth, X, Y, Phase; }
        private readonly List<Fragment> fragments = new List<Fragment>();
        private readonly Random random;
        private readonly Brush horizontalMask, verticalMask, background;
        private double nextPhoto;
        public int ActiveCount { get { return fragments.Count; } }

        public MemoryEffect(int seed)
        {
            random = new Random(seed);
            horizontalMask = Feather(new Point(0, .5), new Point(1, .5));
            verticalMask = Feather(new Point(.5, 0), new Point(.5, 1));
            var gradient = new RadialGradientBrush(Color.FromRgb(29, 35, 41), Color.FromRgb(10, 14, 20));
            gradient.Center = new Point(.57, .44); gradient.GradientOrigin = gradient.Center;
            gradient.RadiusX = .8; gradient.RadiusY = .9; gradient.Freeze(); background = gradient;
        }

        private static Brush Feather(Point start, Point end)
        {
            var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            brush.GradientStops.Add(new GradientStop(Colors.White, .055));
            brush.GradientStops.Add(new GradientStop(Colors.White, .945));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1)); brush.Freeze(); return brush;
        }

        public bool NeedsPhoto(double time) { return time >= nextPhoto && fragments.Count < 3; }
        public void Add(BitmapSource photo, double time)
        {
            if (photo == null) { nextPhoto = time + 30; return; }
            if (fragments.Count >= 3) return;
            fragments.Add(new Fragment { Photo = photo, Birth = time, X = .42 + random.NextDouble() * .26, Y = .36 + random.NextDouble() * .28, Phase = random.NextDouble() * Math.PI * 2 });
            nextPhoto = time + Interval;
        }
        public void Clear() { fragments.Clear(); nextPhoto = 0; }
        internal static double Smooth(double value) { value = Math.Max(0, Math.Min(1, value)); return value * value * (3 - 2 * value); }
        internal static double Opacity(double age)
        { return age < 0 || age >= Lifetime ? 0 : .88 * Smooth(age / 12) * Smooth((Lifetime - age) / 14); }
        internal static Rect Fit(Size photo, Size viewport, double x, double y, double age, double phase)
        {
            double scale = Math.Min(viewport.Width * .48 / photo.Width, viewport.Height * .63 / photo.Height);
            scale *= 1 + .015 * Smooth(age / Lifetime);
            double width = photo.Width * scale, height = photo.Height * scale;
            double driftX = Math.Sin(age / 36 + phase) * Math.Min(12, viewport.Width * .008);
            double driftY = Math.Cos(age / 42 + phase) * Math.Min(9, viewport.Height * .008);
            return new Rect(viewport.Width * x - width / 2 + driftX, viewport.Height * y - height / 2 + driftY, width, height);
        }

        public void Draw(DrawingContext drawing, Size size, double time)
        {
            drawing.DrawRectangle(background, null, new Rect(size));
            fragments.RemoveAll(fragment => time - fragment.Birth >= Lifetime);
            foreach (var fragment in fragments)
            {
                double age = time - fragment.Birth;
                Rect rect = Fit(new Size(fragment.Photo.PixelWidth, fragment.Photo.PixelHeight), size, fragment.X, fragment.Y, age, fragment.Phase);
                drawing.PushOpacity(Opacity(age));
                drawing.PushTransform(new TranslateTransform(rect.X, rect.Y));
                // Two independent masks multiply to feather all four sides without cropping.
                drawing.PushOpacityMask(horizontalMask);
                drawing.PushOpacityMask(verticalMask);
                drawing.DrawImage(fragment.Photo, new Rect(0, 0, rect.Width, rect.Height));
                drawing.Pop(); drawing.Pop(); drawing.Pop(); drawing.Pop();
            }
        }
    }

    internal sealed class PhotoSurface : FrameworkElement
    {
        private readonly IPhotoEffect effect;
        internal double Time { get; private set; }
        public event Action<PhotoSurface> PhotoRequested;
        internal bool RequestPending { get; set; }
        internal int RequestSerial { get; private set; }
        internal bool Suspended { get; set; }
        internal int ActiveCount { get { return effect.ActiveCount; } }
        public PhotoSurface(IPhotoEffect photoEffect) { effect = photoEffect; IsHitTestVisible = false; ClipToBounds = true; }
        public void Tick(double elapsed)
        {
            if (Suspended) return;
            Time += Math.Min(.25, elapsed);
            InvalidateVisual();
            if (!RequestPending && effect.NeedsPhoto(Time) && PhotoRequested != null)
            { RequestPending = true; RequestSerial++; PhotoRequested(this); }
        }
        public void Deliver(BitmapSource photo) { effect.Add(photo, Time); RequestPending = false; InvalidateVisual(); }
        public void Reset() { effect.Clear(); Time = 0; RequestSerial++; RequestPending = false; InvalidateVisual(); }
        protected override void OnRender(DrawingContext drawing) { effect.Draw(drawing, RenderSize, Time); }
    }
}
