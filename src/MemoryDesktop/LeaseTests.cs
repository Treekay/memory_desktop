using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MemoryDesktop
{
    internal static class LeaseTests
    {
        internal static void Run(string output, string fixtures, Action<bool,string> check, List<string> results)
        {
            string[] names={"01-landscape.png","02-portrait.png","03-wide.png"};
            for(int count=1;count<=3;count++)
            {
                string folder=Path.Combine(output,"album-"+count); Directory.CreateDirectory(folder);
                for(int i=0;i<count;i++) File.Copy(Path.Combine(fixtures,names[i]),Path.Combine(folder,names[i]),true);
                var registry=new PhotoLeases(); var a=PhotoSource.Scan(folder,CancellationToken.None,1); var b=PhotoSource.Scan(folder,CancellationToken.None,2);
                var held=new List<PhotoLease>(); var seen=new HashSet<string>();
                for(int i=0;i<count;i++) { var lease=(i%2==0?a:b).NextLease(registry,CancellationToken.None); check(lease!=null && seen.Add(lease.Identity),"cross-display unique photo "+count+"/"+i); held.Add(lease); }
                check(a.NextLease(registry,CancellationToken.None)==null && b.NextLease(registry,CancellationToken.None)==null && registry.ActiveCount==count,"small album shows fewer fragments instead of concurrent repeats: "+count);
                var rescan=PhotoSource.Scan(folder,CancellationToken.None,3);
                check(rescan.NextLease(registry,CancellationToken.None)==null,"rescan preserves active reservations: "+count);
                string available=held[0].Identity; held[0].Dispose(); held.RemoveAt(0);
                var next=rescan.NextLease(registry,CancellationToken.None); check(next!=null && next.Identity==available,"only available photo is reusable without starvation: "+count); next.Dispose();
                foreach(var lease in held) lease.Dispose();
                seen.Clear(); string previous=null; bool immediate=true;
                for(int i=0;i<30;i++) { var lease=a.NextLease(registry,CancellationToken.None); if(lease==null) throw new Exception("Unexpected empty fair rotation"); seen.Add(lease.Identity); if(count>1) immediate &= previous!=lease.Identity; previous=lease.Identity; lease.Dispose(); }
                check(seen.Count==count && immediate && registry.ActiveCount==0,"fair rotation avoids immediate reuse when alternatives are free: "+count);
            }
            var pool=new PhotoLeases(); string path=Path.Combine(fixtures,names[0]);
            var race=new PhotoLease[12]; var tasks=new Task[12];
            for(int i=0;i<tasks.Length;i++) { int slot=i; tasks[i]=Task.Run(delegate { race[slot]=pool.TryAcquire(path,CancellationToken.None); }); }
            Task.WaitAll(tasks); int winners=0; foreach(var lease in race) if(lease!=null) winners++;
            check(winners==1 && pool.ActiveCount==1,"atomic reservation prevents racing pending decoders on the same photo");
            check(pool.TryAcquire(path.ToUpperInvariant(),CancellationToken.None)==null,"Windows path identity is case insensitive");
            string alias=Path.Combine(output,"byte-identical-alias.png"); File.Copy(path,alias,true);
            check(pool.TryAcquire(alias,CancellationToken.None)==null && pool.ActiveCount==1,"identical encoded bytes under different filenames are deduplicated");
            foreach(var lease in race) if(lease!=null) { lease.Dispose(); lease.Dispose(); }
            check(pool.ActiveCount==0,"leases release idempotently after race and duplicate rejection");
            File.Copy(Path.Combine(fixtures,names[1]),alias,true);
            var first=pool.TryAcquire(path,CancellationToken.None); var changed=pool.TryAcquire(alias,CancellationToken.None);
            check(first!=null && changed!=null,"changed file invalidates metadata-keyed fingerprint cache"); first.Dispose(); changed.Dispose();
            check(pool.TryAcquire(Path.Combine(output,"missing-photo.png"),CancellationToken.None)==null && pool.ActiveCount==0,"failed fingerprint releases its pending reservation");
            string broken=Path.Combine(output,"broken-album"); Directory.CreateDirectory(broken); File.WriteAllText(Path.Combine(broken,"bad.jpg"),"invalid synthetic image");
            check(PhotoSource.Scan(broken,CancellationToken.None,7).NextLease(pool,CancellationToken.None)==null && pool.ActiveCount==0,"corrupt decode releases path and content reservations");
            bool cancelled=false; try { pool.TryAcquire(path,new CancellationToken(true)); } catch(OperationCanceledException) { cancelled=true; }
            check(cancelled && pool.ActiveCount==0,"cancelled acquisition retains no lease");
            string hashing=Path.Combine(output,"cancel-during-hash.bin");
            using(var stream=File.Create(hashing)) { var block=new byte[1024*1024]; for(int i=0;i<32;i++) stream.Write(block,0,block.Length); }
            using(var cancellation=new CancellationTokenSource())
            {
                bool stopped=false;
                var hashingTask=Task.Run(delegate { try { var lease=pool.TryAcquire(hashing,cancellation.Token); if(lease!=null) lease.Dispose(); } catch(OperationCanceledException) { stopped=true; } });
                SpinWait.SpinUntil(delegate { return pool.ActiveCount>0 || hashingTask.IsCompleted; },5000); cancellation.Cancel(); hashingTask.Wait();
                check(stopped && pool.ActiveCount==0,"cancellation during hashing releases an already-reserved pending photo");
            }
            var source=PhotoSource.Scan(fixtures,CancellationToken.None,1); var owned=source.NextLease(pool,CancellationToken.None); var effect=new MemoryEffect(3);
            check(effect.Add(owned.Image,0,owned) && pool.ActiveCount==1,"effect owns accepted lease across fades");
            effect.NeedsPhoto(40); check(pool.ActiveCount==0,"expiry releases photo for all displays");
            owned=source.NextLease(pool,CancellationToken.None); effect.Add(owned.Image,41,owned); effect.Clear();
            check(pool.ActiveCount==0,"folder switch and clean exit reset release active leases");
            results.Add("DEDUP scope=normalized case-insensitive path and exact encoded-byte SHA256; no perceptual matching; fingerprint cache<=512");
        }
    }
}
