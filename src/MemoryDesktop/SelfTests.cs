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
                Check(NativeDesktop.UseRaisedDesktopParent(0x00200000, 0x00080000, true, true), "Win11 raised desktop chooses layered sibling rather than occluded WorkerW child");
                Check(!NativeDesktop.UseRaisedDesktopParent(0, 0, true, true), "classic desktop retains WorkerW attachment");
                Check(!NativeDesktop.UseRaisedDesktopParent(0x00200000, 0, true, true) && !NativeDesktop.UseRaisedDesktopParent(0x00200000, 0x00080000, false, true), "partial shell hierarchies are not accepted as raised hosts");
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
                Check(MemoryEffect.Opacity(0) == 0 && MemoryEffect.Opacity(MemoryEffect.Lifetime) == 0 && MemoryEffect.Opacity(8) > .95, "slow fade envelope endpoints and peak");
                Check(MemoryEffect.Opacity(2.8, 12.6) > .95 && MemoryEffect.Opacity(8.8, 12.6) > .95 && MemoryEffect.FadeIn == 2.8 && MemoryEffect.FadeOut == 3.8, "accepted fade durations preserved with six-second dwell");
                double maxStep = 0;
                for (double t = .1; t <= MemoryEffect.Lifetime; t += .1) maxStep = Math.Max(maxStep, Math.Abs(MemoryEffect.Opacity(t) - MemoryEffect.Opacity(t - .1)));
                Check(maxStep < .053, "gentle shorter fade continuity at 100ms steps");
                foreach (var dimensions in new[] { new Size(3000, 500), new Size(500, 3000), new Size(1200, 1200) })
                {
                    PhotoPlacement placement;
                    bool placed = PhotoLayout.TryPlace(dimensions, new Size(1920, 1080), new Rect[0], new Random(19), out placement);
                    Rect fit = placement.Frame(dimensions, new Size(1920, 1080), 20, 1);
                    Check(placed && Math.Abs(fit.Width / fit.Height - dimensions.Width / dimensions.Height) < .0001 && fit.Width < 940 && fit.Height < 770, "aspect ratio and negative space " + dimensions);
                }
                var photo = source.Next(CancellationToken.None); var effect = new MemoryEffect(7);
                bool bounded = true;
                for (int t = 0; t < 240; t++)
                {
                    using (var drawing = new DrawingVisual().RenderOpen()) effect.Draw(drawing, new Size(1920, 1080), t);
                    if (effect.NeedsPhoto(t)) effect.Add(photo, t);
                    bounded &= effect.ActiveCount <= 4;
                }
                Check(bounded, "organic composition bounds simultaneous photo count to four");
                effect.Clear(); effect.Add(photo, 0); effect.Add(photo, 0);
                Check(effect.ActiveCount == 1 && !effect.NeedsPhoto(.8), "startup arrivals stagger rather than form a batch");
                effect.Add(photo, 10);
                double[] expiry = effect.ExpiryTimes;
                Check(effect.ActiveCount == 2 && Math.Abs(expiry[1] - expiry[0]) >= 1.2 - .000001, "each photo owns a distinct independent expiry with interleaved turnover");
                bool continuous = true; int turnovers = 0; double shortestLife = Double.MaxValue, longestLife = 0;
                for (int t = 28; t < 600; t++)
                { if (effect.NeedsPhoto(t) && effect.Add(photo, t)) { turnovers++; double life = effect.DwellTimes[effect.ActiveCount - 1] + MemoryEffect.FadeIn + MemoryEffect.FadeOut; shortestLife = Math.Min(shortestLife, life); longestLife = Math.Max(longestLife, life); } continuous &= effect.ActiveCount <= 4; }
                results.Add(String.Format(System.Globalization.CultureInfo.InvariantCulture, "SCHEDULE observedLifeSeconds={0:F2}..{1:F2} observedDwellSeconds={2:F2}..{3:F2}", shortestLife, longestLife, shortestLife - 6.6, longestLife - 6.6));
                Check(continuous && turnovers > 15, "independent bounded turnover continues over ten minutes");
                LayoutTests.Run(output, Check, results);
                var nativePhoto = NativeBitmap.FromSource(photo);
                Check(nativePhoto.Width == photo.PixelWidth && nativePhoto.Height == photo.PixelHeight, "native photo preserves decoded resolution without a low-resolution intermediate canvas");
                nativePhoto.Dispose();
                var preparedPhoto = effect.Prepare(photo);
                byte[] originalCenter = new byte[4], preparedCenter = new byte[4];
                var convertedPhoto = new FormatConvertedBitmap(photo, PixelFormats.Pbgra32, null, 0);
                var center = new Int32Rect(photo.PixelWidth / 2, photo.PixelHeight / 2, 1, 1);
                convertedPhoto.CopyPixels(center, originalCenter, 4, 0); preparedPhoto.CopyPixels(center, preparedCenter, 4, 0);
                Check(preparedPhoto.IsFrozen && Convert.ToBase64String(originalCenter) == Convert.ToBase64String(preparedCenter), "worker preparation leaves interior pixels sharp and unchanged");
                using (var nativeFrame = new System.Drawing.Bitmap(2240, 1400))
                using (var graphics = System.Drawing.Graphics.FromImage(nativeFrame))
                {
                    effect.Clear(); effect.Add(preparedPhoto, 0); effect.Add(preparedPhoto, 10);
                    var benchmarkSky = new BackgroundScene { Dreamy = true };
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                    benchmarkSky.DrawNative(graphics, new Size(2240, 1400), 12);
                    nativeFrame.Save(Path.Combine(output, "native-background.png"), System.Drawing.Imaging.ImageFormat.Png);
                    effect.DrawNative(graphics, new Size(2240, 1400), 12);
                    nativeFrame.Save(Path.Combine(output, "native-render.png"), System.Drawing.Imaging.ImageFormat.Png);
                    var benchmark = System.Diagnostics.Stopwatch.StartNew();
                    for (int frameIndex = 0; frameIndex < 12; frameIndex++) { benchmarkSky.DrawNative(graphics, new Size(2240, 1400), 12); effect.DrawNative(graphics, new Size(2240, 1400), 12); }
                    results.Add("NATIVE FRAME BENCHMARK avgMs=" + (benchmark.Elapsed.TotalMilliseconds / 12).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                    benchmarkSky.Dispose();
                }
                using (var backgroundFrame = new System.Drawing.Bitmap(Path.Combine(output, "native-background.png")))
                using (var photoFrame = new System.Drawing.Bitmap(Path.Combine(output, "native-render.png")))
                {
                    int changedPixels = 0;
                    for (int y = 0; y < photoFrame.Height; y += 8) for (int x = 0; x < photoFrame.Width; x += 8)
                        if (backgroundFrame.GetPixel(x, y) != photoFrame.GetPixel(x, y)) changedPixels++;
                    Check(changedPixels > 2000, "native alpha composition actually paints visible photos over the background");
                }
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
                var delayedEffect=new MemoryEffect(91); var delayedSurface=new PhotoSurface(delayedEffect); delayedSurface.SetViewport(new Size(2240,1400));
                double due=Double.PositiveInfinity; int clearFour=0,clearThree=0,decodeRequests=0;
                delayedSurface.PhotoRequested+=delegate { due=delayedSurface.Time+.6;decodeRequests++; };
                for(int step=0;step<3000;step++)
                {
                    delayedSurface.Tick(.2);
                    if(delayedSurface.RequestPending && delayedSurface.Time>=due){delayedSurface.Deliver(photo);due=Double.PositiveInfinity;}
                    if(step>150){if(delayedSurface.VisibleCount>=3)clearThree++;if(delayedSurface.VisibleCount==4)clearFour++;}
                }
                Check(clearThree>700 && clearFour>40 && decodeRequests<400,"surface compensates bounded asynchronous preparation delay while recurring clear three/four scenes remain bounded");
                results.Add("DELAYED SURFACE clearThreeOrFourFrames="+clearThree+" clearFourFrames="+clearFour+" requests="+decodeRequests); delayedSurface.Release();
                string settingsPath = Path.Combine(output, "isolated-settings.xml");
                Check(!AppSettings.Load(settingsPath).StartAtLogin, "login startup disabled by default");
                var settings = new AppSettings { PhotoFolder = fixtures, Paused = true, StartAtLogin = false, DreamyBackground = true };
                settings.Save(settingsPath); settings.PhotoFolder = empty; settings.Save(settingsPath);
                var reloaded = AppSettings.Load(settingsPath);
                Check(reloaded.PhotoFolder == empty && reloaded.Paused && !reloaded.StartAtLogin && reloaded.DreamyBackground, "atomic isolated settings roundtrip including background preference");
                byte[] tinyPixels = { 128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255 };
                var tiny = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Pbgra32, null, tinyPixels, 8); tiny.Freeze();
                foreach (var preset in DwellOptions.Presets)
                {
                    double minimum, maximum; DwellOptions.Bounds(preset, out minimum, out maximum);
                    var timed = new MemoryEffect(21); timed.SetDwell(preset);
                    bool dwellBounded = true, independent = true, visible = true; int arrivals = 0;
                    for (double time = 0; time < 600; time += .25)
                    {
                        if (timed.NeedsPhoto(time)) { timed.Add(tiny, time); arrivals++; }
                        foreach (double dwell in timed.DwellTimes) dwellBounded &= dwell >= minimum - .000001 && dwell <= maximum + .000001;
                        double[] ends = timed.ExpiryTimes, births = timed.BirthTimes;
                        for (int i = 0; i < ends.Length; i++) for (int j = 0; j < i; j++) independent &= Math.Abs(ends[i] - ends[j]) >= 1.2 - .000001;
                        int showing = 0; for (int i = 0; i < ends.Length; i++) if (time > births[i] && time < ends[i]) showing++;
                        if (time > 15) visible &= showing <= 4;
                    }
                    Check(dwellBounded, "advertised fully-visible dwell bounds remain exact for " + preset);
                    Check(independent && visible && arrivals > 20, "independent continuous turnover without batch expiry for " + preset);
                    settings.DwellPreset = preset; settings.Save(settingsPath);
                    Check(AppSettings.Load(settingsPath).DwellPreset == preset, "dwell preset persists for " + preset);
                    timed.Clear();
                }
                var menuEffect = new MemoryEffect(18); menuEffect.SetDwell(PhotoDwell.Long); menuEffect.Add(tiny, 0);
                var menuSurface = new PhotoSurface(menuEffect); double existingExpiry = menuEffect.ExpiryTimes[0];
                using (var dwellMenu = DwellOptions.CreateMenu(delegate(PhotoDwell selected) { menuSurface.DwellPreset = selected; settings.DwellPreset = selected; settings.Save(settingsPath); }))
                {
                    DwellOptions.UpdateChecks(dwellMenu, PhotoDwell.Long);
                    int shortIndex=Array.IndexOf(DwellOptions.Presets,PhotoDwell.Short);
                    ((System.Windows.Forms.ToolStripMenuItem)dwellMenu.DropDownItems[shortIndex]).PerformClick();
                    Check(menuEffect.ExpiryTimes[0] == existingExpiry && menuSurface.Time == 0 && menuEffect.ActiveCount == 1, "tray dwell change preserves the current photo and clock without a pop or batch reset");
                    bool checks = true;
                    for (int i = 0; i < DwellOptions.Presets.Length; i++) checks &= ((System.Windows.Forms.ToolStripMenuItem)dwellMenu.DropDownItems[i]).Checked == (i == shortIndex);
                    Check(checks && menuSurface.DwellPreset == PhotoDwell.Short && AppSettings.Load(settingsPath).DwellPreset == PhotoDwell.Short, "tray click applies and saves exactly one visibly checked preset");
                    menuEffect.NeedsPhoto(existingExpiry + 1); menuEffect.Add(tiny, existingExpiry + 1);
                    Check(menuEffect.DwellTimes[0] >= 6 && menuEffect.DwellTimes[0] <= 8, "changed tray preset applies to the next independent photo");
                    int briefIndex=Array.IndexOf(DwellOptions.Presets,PhotoDwell.Brief);
                    double shortExpiry=menuEffect.ExpiryTimes[0];
                    ((System.Windows.Forms.ToolStripMenuItem)dwellMenu.DropDownItems[briefIndex]).PerformClick();
                    DwellOptions.UpdateChecks(dwellMenu,settings.DwellPreset);
                    checks=true; for(int i=0;i<DwellOptions.Presets.Length;i++) checks &= ((System.Windows.Forms.ToolStripMenuItem)dwellMenu.DropDownItems[i]).Checked==(i==briefIndex);
                    Check(checks && menuSurface.DwellPreset==PhotoDwell.Brief && AppSettings.Load(settingsPath).DwellPreset==PhotoDwell.Brief,"3-5 second tray choice applies, persists and has the unique checkmark");
                    Check(menuEffect.ExpiryTimes[0]==shortExpiry && menuEffect.ActiveCount==1,"adding the brief choice preserves the current selection and active photo until user clicks it");
                }
                menuEffect.Clear();
                File.WriteAllText(settingsPath, "<AppSettings><PhotoFolder>legacy-fixture</PhotoFolder><Paused>true</Paused><StartAtLogin>false</StartAtLogin><DreamyBackground>true</DreamyBackground></AppSettings>");
                var legacy = AppSettings.Load(settingsPath);
                Check(legacy.DwellPreset == PhotoDwell.Short && legacy.PhotoFolder == "legacy-fixture" && legacy.Paused && legacy.DreamyBackground && !legacy.StartAtLogin, "legacy settings gain short dwell while preserving existing preferences");
                LeaseTests.Run(output, fixtures, Check, results);
                var sky = new BackgroundScene { Dreamy = true };
                var skyVisual = new DrawingVisual();
                using (var drawing = skyVisual.RenderOpen()) sky.Draw(drawing, new Size(320, 200), 20);
                var skyBitmap = new RenderTargetBitmap(320, 200, 96, 96, PixelFormats.Pbgra32); skyBitmap.Render(skyVisual);
                byte[] firstSky = new byte[320 * 200 * 4]; skyBitmap.CopyPixels(firstSky, 320 * 4, 0);
                using (var drawing = skyVisual.RenderOpen()) sky.Draw(drawing, new Size(320, 200), 20);
                skyBitmap.Clear(); skyBitmap.Render(skyVisual);
                byte[] pausedSky = new byte[firstSky.Length]; skyBitmap.CopyPixels(pausedSky, 320 * 4, 0);
                Check(Convert.ToBase64String(firstSky) == Convert.ToBase64String(pausedSky), "dreamy background is deterministic and stays still on a frozen pause clock");
                sky.Dreamy = false; using (var drawing = skyVisual.RenderOpen()) sky.Draw(drawing, new Size(320, 200), 20);
                skyBitmap.Clear(); skyBitmap.Render(skyVisual); skyBitmap.CopyPixels(pausedSky, 320 * 4, 0);
                Check(Convert.ToBase64String(firstSky) != Convert.ToBase64String(pausedSky), "background switches independently of photo scheduling");
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
