using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Media.Imaging;

namespace MemoryDesktop
{
    internal sealed class PhotoLease : IDisposable
    {
        private PhotoLeases owner;
        internal readonly string Identity;
        internal string Fingerprint;
        internal BitmapSource Image;
        internal PhotoLease(PhotoLeases registry, string identity) { owner = registry; Identity = identity; }
        public void Dispose()
        { var registry = Interlocked.Exchange(ref owner, null); if (registry != null) registry.Release(this); Image = null; }
    }

    // Global across displays and source rescans, including pending loads. Hash
    // only selected candidates; retain at most 512 metadata-keyed fingerprints.
    internal sealed class PhotoLeases
    {
        private sealed class CachedHash { internal long Length, Stamp; internal string Hash; }
        private readonly object gate = new object();
        private readonly HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> hashes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, CachedHash> cache = new Dictionary<string, CachedHash>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> cachedOrder = new Queue<string>();
        internal int ActiveCount { get { lock (gate) return paths.Count; } }
        internal PhotoLease TryAcquire(string path, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string identity = Path.GetFullPath(path);
            lock (gate) if (!paths.Add(identity)) return null;
            var lease = new PhotoLease(this, identity); bool accepted = false;
            try
            {
                string fingerprint = Fingerprint(identity, cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (fingerprint == null) return null;
                lock (gate) { if (!hashes.Add(fingerprint)) return null; lease.Fingerprint = fingerprint; }
                accepted = true; return lease;
            }
            finally { if (!accepted) lease.Dispose(); }
        }
        private string Fingerprint(string path, CancellationToken cancellation)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > PhotoSource.MaximumFileBytes) return null;
                CachedHash existing;
                lock (gate) if (cache.TryGetValue(path, out existing) && existing.Length == info.Length && existing.Stamp == info.LastWriteTimeUtc.Ticks) return existing.Hash;
                string value;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sha = SHA256.Create())
                {
                    byte[] buffer = new byte[65536]; int read; long total=0;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    { cancellation.ThrowIfCancellationRequested(); total+=read; if(total>PhotoSource.MaximumFileBytes) return null; sha.TransformBlock(buffer, 0, read, buffer, 0); }
                    sha.TransformFinalBlock(new byte[0], 0, 0); value = BitConverter.ToString(sha.Hash);
                }
                lock (gate)
                {
                    if (!cache.ContainsKey(path))
                    { while (cache.Count >= 512 && cachedOrder.Count > 0) cache.Remove(cachedOrder.Dequeue()); cachedOrder.Enqueue(path); }
                    cache[path] = new CachedHash { Length = info.Length, Stamp = info.LastWriteTimeUtc.Ticks, Hash = value };
                }
                return value;
            }
            catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; }
        }
        internal void Release(PhotoLease lease)
        { lock (gate) { paths.Remove(lease.Identity); if (lease.Fingerprint != null) hashes.Remove(lease.Fingerprint); } }
    }
}
