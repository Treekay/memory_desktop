using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace MemoryDesktop
{
    // A single serialized decoder is shared by all displays. No image cache grows over time.
    internal sealed class PhotoSource
    {
        internal const int MaximumFiles = 4096;
        internal const int MaximumDecodeEdge = 1600;
        internal const long MaximumFileBytes = 64L * 1024 * 1024;
        private static readonly HashSet<string> Extensions = new HashSet<string>(new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" }, StringComparer.OrdinalIgnoreCase);
        private readonly List<string> files;
        private readonly Random random;
        private int cursor;
        private string previous;
        private readonly object selectionGate = new object();
        public int Count { get { return files.Count; } }
        public bool WasTruncated { get; private set; }

        private PhotoSource(List<string> paths, Random rng, bool truncated)
        { files = paths; random = rng; WasTruncated = truncated; Shuffle(); }

        public static PhotoSource Scan(string folder, CancellationToken cancellation, int seed)
        {
            var rng = new Random(seed);
            var candidates = new List<string>();
            int seen = 0;
            // Only the explicitly selected folder, no recursion or personal-folder discovery.
            foreach (string path in Directory.EnumerateFiles(folder))
            {
                cancellation.ThrowIfCancellationRequested();
                if (!Extensions.Contains(Path.GetExtension(path))) continue;
                seen++;
                if (candidates.Count < MaximumFiles) candidates.Add(path);
                else { int slot = rng.Next(seen); if (slot < MaximumFiles) candidates[slot] = path; }
            }
            return new PhotoSource(candidates, rng, seen > MaximumFiles);
        }

        private void Shuffle()
        {
            for (int i = files.Count - 1; i > 0; i--)
            { int j = random.Next(i + 1); string temp = files[i]; files[i] = files[j]; files[j] = temp; }
            if (files.Count > 1 && files[0] == previous)
            { string temp = files[0]; files[0] = files[1]; files[1] = temp; }
            cursor = 0;
        }

        public BitmapSource Next(CancellationToken cancellation)
        {
            int remaining = files.Count;
            while (remaining-- > 0 && files.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                if (cursor >= files.Count) Shuffle();
                string path = files[cursor++];
                BitmapSource bitmap = Decode(path);
                if (bitmap != null) { previous = path; return bitmap; }
                files.Remove(path); cursor--;
            }
            return null;
        }

        internal PhotoLease NextLease(PhotoLeases leases, CancellationToken cancellation)
        {
            lock (selectionGate)
            {
                int remaining = files.Count; string fallback = null;
                while (remaining-- > 0 && files.Count > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (cursor >= files.Count) Shuffle();
                    string path = files[cursor++];
                    if (files.Count > 1 && path == previous && remaining > 0) { fallback = path; continue; }
                    var lease = DecodeLease(path, leases, cancellation);
                    if (lease != null) return lease;
                }
                return fallback == null ? null : DecodeLease(fallback, leases, cancellation);
            }
        }
        private PhotoLease DecodeLease(string path, PhotoLeases leases, CancellationToken cancellation)
        {
            var lease = leases.TryAcquire(path, cancellation);
            if (lease == null) return null;
            bool accepted = false;
            try
            {
                cancellation.ThrowIfCancellationRequested(); lease.Image = Decode(path); cancellation.ThrowIfCancellationRequested();
                if (lease.Image != null) { previous = path; accepted = true; return lease; }
                files.Remove(path); cursor = Math.Max(0, cursor - 1); return null;
            }
            finally { if (!accepted) lease.Dispose(); }
        }

        internal static BitmapSource Decode(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length == 0 || info.Length > MaximumFileBytes) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    // DelayCreation lets us inspect dimensions without decoding the full frame.
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                    int width = decoder.Frames[0].PixelWidth, height = decoder.Frames[0].PixelHeight;
                    if (width <= 0 || height <= 0 || width > 30000 || height > 30000 || (long)width * height > 120000000) return null;
                    int orientation = ReadOrientation(decoder.Frames[0].Metadata as BitmapMetadata);
                    stream.Position = 0;
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    image.StreamSource = stream;
                    if (width >= height) image.DecodePixelWidth = Math.Min(width, MaximumDecodeEdge);
                    else image.DecodePixelHeight = Math.Min(height, MaximumDecodeEdge);
                    image.EndInit(); image.Freeze();
                    return Orient(image, orientation);
                }
            }
            catch (Exception e)
            {
                if (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException || e is System.Runtime.InteropServices.COMException || e is FileFormatException || e is InvalidOperationException) return null;
                throw;
            }
        }

        private static int ReadOrientation(BitmapMetadata metadata)
        {
            if (metadata == null) return 1;
            foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
            {
                try { object value = metadata.GetQuery(query); if (value != null) return Convert.ToInt32(value); }
                catch (NotSupportedException) { } catch (ArgumentException) { } catch (System.Runtime.InteropServices.COMException) { }
            }
            return 1;
        }

        private static BitmapSource Orient(BitmapSource image, int orientation)
        {
            if (orientation <= 1 || orientation > 8) return image;
            var transform = new TransformGroup();
            if (orientation == 2 || orientation == 5 || orientation == 7) transform.Children.Add(new ScaleTransform(-1, 1));
            if (orientation == 4) transform.Children.Add(new ScaleTransform(1, -1));
            if (orientation == 3) transform.Children.Add(new RotateTransform(180));
            if (orientation == 5 || orientation == 8) transform.Children.Add(new RotateTransform(270));
            if (orientation == 6 || orientation == 7) transform.Children.Add(new RotateTransform(90));
            transform.Freeze();
            var oriented = new TransformedBitmap(image, transform); oriented.Freeze(); return oriented;
        }
    }
}
