using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MemoryDesktop
{
    internal static class LayoutTests
    {
        internal static void Run(string output, Action<bool, string> check, List<string> results)
        {
            var images = new List<BitmapSource>();
            foreach (var shape in new[] { new Size(160,90), new Size(80,140), new Size(220,70), new Size(100,100), new Size(240,30), new Size(20,180) })
            {
                int w = (int)shape.Width, h = (int)shape.Height;
                var pixels = new byte[w*h*4];
                for (int p=0; p<pixels.Length; p+=4) { pixels[p]=(byte)(80+images.Count*20); pixels[p+1]=140; pixels[p+2]=190; pixels[p+3]=255; }
                var image = BitmapSource.Create(w,h,96,96,PixelFormats.Pbgra32,null,pixels,w*4); image.Freeze(); images.Add(image);
            }
            foreach (var size in new[] { new Size(1920,1080), new Size(2240,1400), new Size(1080,1920), new Size(7680,2160), new Size(320,240) })
            {
                var effect = new MemoryEffect(37); effect.SetViewport(size);
                var rng = new Random(42); var histogram = new int[5]; var bins = new int[9]; var targets = new HashSet<int>();
                bool safe=true, readable=true, timing=true, stationary=true; int added=0;
                var originalFrames=new Dictionary<double,Rect>();
                for (double t=0; t<600; t+=.25)
                {
                    if (effect.NeedsPhoto(t) && effect.Add(images[rng.Next(images.Count)],t))
                    {
                        added++; var r=effect.Reservations[effect.ActiveCount-1];
                        int bx=Math.Min(2,(int)((r.X+r.Width/2)*3)), by=Math.Min(2,(int)((r.Y+r.Height/2)*3)); bins[by*3+bx]++;
                    }
                    targets.Add(effect.TargetCount);
                    var reserves=effect.Reservations; var frames=effect.FrameRects(t); var ends=effect.ExpiryTimes; var births=effect.BirthTimes;
                    int showing=0;
                    for (int i=0;i<frames.Length;i++)
                    {
                        var frame=frames[i];
                        Rect originalFrame;
                        if(originalFrames.TryGetValue(births[i],out originalFrame)) stationary &= frame.Equals(originalFrame);
                        else originalFrames.Add(births[i],frame);
                        var normalized=new Rect(frame.X/size.Width,frame.Y/size.Height,frame.Width/size.Width,frame.Height/size.Height);
                        safe &= new Rect(0,0,1,1).Contains(reserves[i]) && reserves[i].Contains(normalized);
                        readable &= Math.Max(frame.Width,frame.Height)>=Math.Min(size.Width,size.Height)*.38-.01;
                        if (t>births[i] && t<ends[i]) showing++;
                        for(int j=0;j<i;j++) { safe &= !reserves[i].IntersectsWith(reserves[j]) && !frames[i].IntersectsWith(frames[j]); timing &= Math.Abs(ends[i]-ends[j])>=1.2-.000001; }
                    }
                    if(t>20) histogram[showing]++;
                    foreach(double dwell in effect.DwellTimes) timing &= dwell>=6-.000001 && dwell<=8+.000001;
                }
                int occupiedBins=0; foreach(int count in bins) if(count>0) occupiedBins++;
                check(safe && effect.ActiveCount<=4,"reservations prevent collisions throughout all fades at "+size);
                check(stationary,"photo position and size stay fixed for each entire lifetime at "+size);
                check(readable && added>35,"readable photo scale and continuing arrivals at "+size);
                check(timing && targets.Count==3,"independent expiry, exact dwell, varied count targets at "+size);
                check(occupiedBins>=5,"organic placement reaches at least five screen regions at "+size);
                check(histogram[2]>0 && histogram[3]>0 && histogram[4]>0 && histogram[0]==0,"composition varies between two, three and four without empty steady-state frames at "+size);
                results.Add("LAYOUT "+size+" accepted="+added+" countHistogram0..4="+String.Join(",",histogram)+" regionBins="+String.Join(",",bins));
                effect.Clear();
            }
            // Save compositions using the real native renderer and synthetic scenery only.
            var scenic = new[] { PhotoSource.Decode(Path.Combine(output,"fixtures","01-landscape.png")), PhotoSource.Decode(Path.Combine(output,"fixtures","02-portrait.png")), PhotoSource.Decode(Path.Combine(output,"fixtures","03-wide.png")) };
            var demo=new MemoryEffect(91); demo.SetViewport(new Size(1920,1080)); var choice=new Random(8); var saved=new HashSet<int>();
            for(int i=0;i<scenic.Length;i++) scenic[i]=demo.Prepare(scenic[i]);
            for(double t=0;t<180;t+=.5)
            {
                if(demo.NeedsPhoto(t)) demo.Add(scenic[choice.Next(scenic.Length)],t);
                int visible=0; var births=demo.BirthTimes; var ends=demo.ExpiryTimes;
                for(int i=0;i<births.Length;i++) if(MemoryEffect.Opacity(t-births[i],ends[i]-births[i])>=.60) visible++;
                if(visible>=2 && !saved.Contains(visible))
                {
                    using(var bitmap=new System.Drawing.Bitmap(1920,1080)) using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
                    { var sky=new BackgroundScene { Dreamy=true }; sky.DrawNative(graphics,new Size(1920,1080),t); demo.DrawNative(graphics,new Size(1920,1080),t); bitmap.Save(Path.Combine(output,"composition-"+visible+".png")); sky.Dispose(); }
                    saved.Add(visible);
                }
            }
            check(saved.Count>0,"native synthetic organic composition exported for visual inspection"); demo.Clear();
            foreach(var dwell in new[]{PhotoDwell.Short,PhotoDwell.Brief})
            foreach(var size in new[]{new Size(2240,1400),new Size(1920,1080)})
            {
                var representative=new BitmapSource[28];
                for(int i=0;i<representative.Length;i++) representative[i]=CompositionDiagnostics.Synthetic(i%3==0?new Size(4,3):new Size(3,4));
                bool recurring=true;
                results.Add(CompositionDiagnostics.Simulate(representative,new[]{size},dwell,37,600,delegate(int screen,int[] visible,int[] reserved){int total=0;foreach(int value in visible)total+=value; recurring &= (visible[3]+visible[4])>total*.25 && visible[4]>total*.015;}));
                check(recurring,"meaningfully visible three and four photo scenes recur with representative source at "+size+" / "+dwell);
            }
        }
    }
}
