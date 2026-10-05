using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace MemoryDesktop
{
    internal static class CompositionDiagnostics
    {
        // Read only the selected source; retain at most 64 identity leases and
        // one decoded image at a time. Reports contain counts and sizes only.
        internal static string SelectedReport()
        {
            var settings=AppSettings.Load(AppSettings.DefaultPath); var report=new StringBuilder();
            var pool=new PhotoLeases(); var held=new List<PhotoLease>(); var images=new List<BitmapSource>();
            try
            {
                var source=PhotoSource.Scan(settings.PhotoFolder,CancellationToken.None,7);
                int candidates=source.Count; report.AppendLine("Candidates="+candidates+" Dwell="+settings.DwellPreset);
                while(held.Count<64)
                {
                    var lease=source.NextLease(pool,CancellationToken.None); if(lease==null) break;
                    report.AppendLine("UniqueShape="+lease.Image.PixelWidth+"x"+lease.Image.PixelHeight);
                    images.Add(Synthetic(new Size(lease.Image.PixelWidth,lease.Image.PixelHeight))); lease.Image=null; held.Add(lease);
                }
                report.AppendLine("UniqueUsable="+held.Count+" complete="+(held.Count<64)+" remainingCandidates="+source.Count);
                var viewports=Forms.Screen.AllScreens.Take(8).Select(screen=>new Size(screen.Bounds.Width,screen.Bounds.Height)).ToArray();
                if(images.Count>0) foreach(int seed in new[]{17,37,91}) report.AppendLine(Simulate(images.ToArray(),viewports,settings.DwellPreset,seed,600,null));
                return report.ToString();
            }
            finally { foreach(var lease in held) lease.Dispose(); }
        }
        internal static BitmapSource Synthetic(Size shape)
        {
            double scale=96/Math.Max(shape.Width,shape.Height); int w=Math.Max(1,(int)Math.Round(shape.Width*scale)),h=Math.Max(1,(int)Math.Round(shape.Height*scale));
            byte[] pixels=new byte[w*h*4]; for(int p=0;p<pixels.Length;p+=4) {pixels[p]=110;pixels[p+1]=160;pixels[p+2]=190;pixels[p+3]=255;}
            var image=BitmapSource.Create(w,h,96,96,PixelFormats.Pbgra32,null,pixels,w*4); image.Freeze(); return image;
        }
        internal static string Simulate(BitmapSource[] images,Size[] screens,PhotoDwell dwell,int seed,double seconds,Action<int,int[],int[]> inspect)
        {
            var effects=screens.Select((screen,index)=>new MemoryEffect(seed+index*179)).ToArray();
            var occupied=new Dictionary<int,double>(); var rng=new Random(seed); int previous=-1;
            var visible=screens.Select(screen=>new int[5]).ToArray(); var reserved=screens.Select(screen=>new int[5]).ToArray();
            for(int i=0;i<effects.Length;i++){effects[i].SetViewport(screens[i]);effects[i].SetDwell(dwell);}
            for(double t=0;t<seconds;t+=.2)
            {
                foreach(int id in occupied.Where(pair=>pair.Value<=t).Select(pair=>pair.Key).ToArray()) occupied.Remove(id);
                // Rotate service order so one display cannot monopolize a small album.
                for(int n=0;n<effects.Length;n++)
                {
                    int i=(n+(int)(t*5))%effects.Length; var effect=effects[i];
                    if(effect.NeedsPhoto(t))
                    {
                        var free=Enumerable.Range(0,images.Length).Where(id=>!occupied.ContainsKey(id)).ToList();
                        if(free.Count>1) free.Remove(previous);
                        if(free.Count==0) effect.Add(null,t);
                        else {int id=free[rng.Next(free.Count)];previous=id;if(effect.Add(images[id],t)) occupied[id]=effect.ExpiryTimes.Last();}
                    }
                    if(t>30){visible[i][effect.VisibleCount(t,.60)]++;reserved[i][effect.ActiveCount]++;}
                }
            }
            var report=new StringBuilder("OCCUPANCY seed="+seed+" dwell="+dwell+" threshold=0.60 ");
            for(int i=0;i<effects.Length;i++)
            {
                report.Append("screen="+screens[i]+" visible0..4="+String.Join(",",visible[i])+" reserved0..4="+String.Join(",",reserved[i])+"; ");
                if(inspect!=null) inspect(i,visible[i],reserved[i]); effects[i].Clear();
            }
            return report.ToString();
        }
    }
}
