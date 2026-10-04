using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MemoryDesktop
{
    internal static class SelfTests
    {
        private static readonly List<string> results = new List<string>();
        private static void Check(bool passed, string name) { if (!passed) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); }
        internal static int Run(string output)
        {
            Directory.CreateDirectory(output);
            string fixtures = Path.Combine(output, "fixtures"); Directory.CreateDirectory(fixtures);
            try
            {
                MakeFixture(Path.Combine(fixtures, "01-landscape.png"), 1800, 1000, 0);
                MakeFixture(Path.Combine(fixtures, "02-portrait.png"), 800, 1400, 1);
                MakeFixture(Path.Combine(fixtures, "03-wide.png"), 2200, 700, 2);
                File.WriteAllText(Path.Combine(fixtures, "corrupt.jpg"), "This is a synthetic invalid image.");
                File.WriteAllText(Path.Combine(fixtures, "not-an-image.txt"), "Excluded fixture.");
                string nested = Path.Combine(fixtures, "nested"); Directory.CreateDirectory(nested);
                MakeFixture(Path.Combine(nested, "excluded.png"), 32, 32, 0);
                var source = PhotoSource.Scan(fixtures, CancellationToken.None, 7);
                Check(source.Count == 4, "explicit folder only; extensions filtered; no recursive scan");
                string previousPixel = null;
                for (int i = 0; i < 12; i++)
                {
                    var image = source.Next(CancellationToken.None);
                    Check(image != null && image.IsFrozen && Math.Max(image.PixelWidth, image.PixelHeight) <= PhotoSource.MaximumDecodeEdge, "bounded frozen decoding round " + i);
                    string size = image.PixelWidth + "x" + image.PixelHeight;
                    Check(size != previousPixel, "shuffle has no immediate repeat " + i); previousPixel = size;
                }
                Check(source.Count == 3, "corrupt image removed without interrupting source");
                string rotatedFile = Path.Combine(output, "orientation-6.jpg");
                var rotatedMetadata = new BitmapMetadata("jpg"); rotatedMetadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
                var jpeg = new JpegBitmapEncoder();
                jpeg.Frames.Add(BitmapFrame.Create(PhotoSource.Decode(Path.Combine(fixtures, "01-landscape.png")), null, rotatedMetadata, null));
                using (var outputImage = File.Create(rotatedFile)) jpeg.Save(outputImage);
                var rotated = PhotoSource.Decode(rotatedFile);
                Check(rotated != null && rotated.PixelHeight > rotated.PixelWidth && rotated.IsFrozen, "EXIF phone-photo orientation restored with bounded decode");
                string original = Path.Combine(fixtures, "01-landscape.png"), moved = original + ".moved";
                File.Move(original, moved); File.Move(moved, original);
                using (File.Open(original, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                Check(true, "decoder releases image file handles");
                string empty = Path.Combine(output, "empty"); Directory.CreateDirectory(empty);
                Check(PhotoSource.Scan(empty, CancellationToken.None, 1).Next(CancellationToken.None) == null, "empty folder returns no photo");
                var cancelled = new CancellationToken(true);
                bool cancelledCorrectly = false;
                try { PhotoSource.Scan(fixtures, cancelled, 1); } catch (OperationCanceledException) { cancelledCorrectly = true; }
                Check(cancelledCorrectly, "cancelled folder scan stops");
                string large = Path.Combine(output, "large-folder"); Directory.CreateDirectory(large);
                for (int i = 0; i < PhotoSource.MaximumFiles + 5; i++) File.WriteAllText(Path.Combine(large, i.ToString("0000") + ".jpg"), "fixture");
                var sampled = PhotoSource.Scan(large, CancellationToken.None, 9);
                Check(sampled.Count == PhotoSource.MaximumFiles && sampled.WasTruncated, "large folders use a bounded random reservoir");
                string oversizeDimension = Path.Combine(output, "too-wide.png");
                MakeFixture(oversizeDimension, 30001, 1, 0);
                Check(PhotoSource.Decode(oversizeDimension) == null, "extreme source dimensions rejected before photo decoding");
                bool missing = false;
                try { PhotoSource.Scan(Path.Combine(output, "missing"), CancellationToken.None, 1); } catch (DirectoryNotFoundException) { missing = true; }
                Check(missing, "missing folder reported without discovery elsewhere");
                Check(MemoryEffect.Opacity(0) == 0 && MemoryEffect.Opacity(46) == 0 && MemoryEffect.Opacity(20) > .85, "slow overlap envelope endpoints and peak");
                double maxStep = 0;
                for (double t = .1; t <= 46; t += .1) maxStep = Math.Max(maxStep, Math.Abs(MemoryEffect.Opacity(t) - MemoryEffect.Opacity(t - .1)));
                Check(maxStep < .012, "fade continuity at 100ms steps");
                foreach (var dimensions in new[] { new Size(3000, 500), new Size(500, 3000), new Size(1200, 1200) })
                {
                    Rect fit = MemoryEffect.Fit(dimensions, new Size(1920, 1080), .55, .5, 20, 1);
                    Check(Math.Abs(fit.Width / fit.Height - dimensions.Width / dimensions.Height) < .0001 && fit.Width < 940 && fit.Height < 700, "aspect ratio and negative space " + dimensions);
                }
                var photo = source.Next(CancellationToken.None); var effect = new MemoryEffect(7);
                bool bounded = true;
                for (int t = 0; t < 240; t++)
                {
                    using (var drawing = new DrawingVisual().RenderOpen()) effect.Draw(drawing, new Size(1920, 1080), t);
                    if (effect.NeedsPhoto(t)) effect.Add(photo, t);
                    bounded &= effect.ActiveCount <= 3;
                }
                Check(bounded, "active fragments bounded through four minutes of overlapping animation");
                effect.Clear(); Check(effect.ActiveCount == 0 && effect.NeedsPhoto(0), "switch/reset releases fragments and starts new timeline");
                var surface = new PhotoSurface(new MemoryEffect(1)); int requests = 0;
                surface.PhotoRequested += delegate { requests++; };
                surface.Tick(.03); surface.Tick(.03);
                Check(requests == 1 && surface.RequestPending, "one pending decode per surface");
                surface.Suspended = true; double beforePause = surface.Time; surface.Tick(10);
                Check(surface.Time == beforePause && requests == 1, "paused surface freezes animation and decode requests");
                surface.Reset(); Check(!surface.RequestPending && surface.Time == 0, "folder reset invalidates pending surface requests");
                surface.Suspended = false; int serialBefore = surface.RequestSerial; surface.Tick(.03);
                Check(surface.RequestSerial > serialBefore && requests == 2, "new source request has a distinct serial for rejecting late results");
                string settingsPath = Path.Combine(output, "isolated-settings.xml");
                Check(!AppSettings.Load(settingsPath).StartAtLogin, "login startup disabled by default");
                var settings = new AppSettings { PhotoFolder = fixtures, Paused = true, StartAtLogin = false };
                settings.Save(settingsPath); settings.PhotoFolder = empty; settings.Save(settingsPath);
                var reloaded = AppSettings.Load(settingsPath);
                Check(reloaded.PhotoFolder == empty && reloaded.Paused && !reloaded.StartAtLogin, "atomic isolated settings roundtrip and replace");
                File.WriteAllText(settingsPath, "invalid XML");
                Check(!AppSettings.Load(settingsPath).StartAtLogin, "corrupt settings recover safely with startup off");
                // No registry writes and no Explorer messages are exercised by tests.
                results.Add("TESTED OS: " + Environment.OSVersion.Version);
                File.WriteAllLines(Path.Combine(output, "test-report.txt"), results.ToArray());
                return 0;
            }
            catch (Exception e)
            { results.Add(e.ToString()); File.WriteAllLines(Path.Combine(output, "test-report.txt"), results.ToArray()); return 1; }
        }

        private static void MakeFixture(string path, int width, int height, int theme)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                Color top = theme == 1 ? Color.FromRgb(31, 47, 64) : theme == 2 ? Color.FromRgb(101, 104, 95) : Color.FromRgb(112, 145, 151);
                Color bottom = theme == 1 ? Color.FromRgb(121, 108, 105) : theme == 2 ? Color.FromRgb(209, 171, 138) : Color.FromRgb(232, 194, 157);
                dc.DrawRectangle(new LinearGradientBrush(top, bottom, 90), null, new Rect(0, 0, width, height));
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(190, 251, 224, 189)), null, new Point(width * .69, height * .29), width * .06, width * .06);
                for (int i = 0; i < 4; i++)
                {
                    var geometry = new StreamGeometry();
                    using (var contour = geometry.Open())
                    {
                        contour.BeginFigure(new Point(0, height * (.65 + .06 * i)), true, true);
                        contour.BezierTo(new Point(width * .3, height * (.3 + .09 * i)), new Point(width * .55, height * (.9 - .06 * i)), new Point(width, height * (.55 + .07 * i)), true, false);
                        contour.LineTo(new Point(width, height), true, false); contour.LineTo(new Point(0, height), true, false);
                    }
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb((byte)(53 - i * 8), (byte)(78 - i * 9), (byte)(84 - i * 8))), null, geometry);
                }
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
        }
    }
}
