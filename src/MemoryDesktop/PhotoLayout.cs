using System;
using System.Collections.Generic;
using System.Windows;

namespace MemoryDesktop
{
    internal sealed class PhotoPlacement
    {
        internal Rect Base, Reservation;
        internal Rect Frame(Size photo, Size viewport, double age, double phase)
        {
            var area=new Rect(Base.X*viewport.Width,Base.Y*viewport.Height,Base.Width*viewport.Width,Base.Height*viewport.Height);
            double scale=Math.Min(area.Width/photo.Width,area.Height/photo.Height);
            double width=photo.Width*scale,height=photo.Height*scale;
            double dx=Math.Sin(age/36+phase)*Math.Min(12,viewport.Width*.008);
            double dy=Math.Cos(age/42+phase)*Math.Min(9,viewport.Height*.008);
            return new Rect(area.X+(area.Width-width)/2+dx,area.Y+(area.Height-height)/2+dy,width,height);
        }
    }
    internal static class PhotoLayout
    {
        // Includes maximum normalized drift (.008), rounding and a visible gap.
        internal const double Margin=.018;
        internal const double ReadableEdge=.38;
        private static readonly Rect Bounds=new Rect(.025,.025,.95,.95);
        internal static bool TryPlace(Size photo,Size viewport,IList<Rect> occupied,Random random,out PhotoPlacement placement)
        { return TryPlace(photo,viewport,occupied,random,4,out placement); }
        internal static bool TryPlace(Size photo,Size viewport,IList<Rect> occupied,Random random,int target,out PhotoPlacement placement)
        {
            placement=null;
            if(viewport.Width<1||viewport.Height<1||photo.Width<1||photo.Height<1) return false;
            var free=Free(occupied); if(free.Count==0) return false;
            double shortEdge=Math.Min(viewport.Width,viewport.Height),minimum=shortEdge*ReadableEdge,bestScore=Double.NegativeInfinity;
            double minScale=minimum/Math.Max(photo.Width,photo.Height);
            var minimumReservation=new Size(photo.Width*minScale/viewport.Width+2*Margin,photo.Height*minScale/viewport.Height+2*Margin);
            for(int attempt=0;attempt<192;attempt++)
            {
                Rect space=free[random.Next(free.Count)];
                double longEdge=shortEdge*(target>=3 ? .38+random.NextDouble()*.22 : .43+random.NextDouble()*.25);
                double scale=Math.Min(longEdge/Math.Max(photo.Width,photo.Height),Math.Min((space.Width-2*Margin)*viewport.Width/photo.Width,(space.Height-2*Margin)*viewport.Height/photo.Height));
                scale=Math.Min(scale,Math.Min(.47*viewport.Width/photo.Width,.68*viewport.Height/photo.Height));
                if(scale<minScale-.0000001) continue;
                double width=photo.Width*scale/viewport.Width,height=photo.Height*scale/viewport.Height;
                double x=space.X+Margin+random.NextDouble()*Math.Max(0,space.Width-width-2*Margin);
                double y=space.Y+Margin+random.NextDouble()*Math.Max(0,space.Height-height-2*Margin);
                var basis=new Rect(x,y,width,height); var reserve=basis; reserve.Inflate(Margin,Margin);
                if(!Bounds.Contains(reserve)) continue;
                bool collision=false; foreach(var other in occupied) if(reserve.IntersectsWith(other)){collision=true;break;}
                if(collision) continue;
                var combined=new List<Rect>(occupied); combined.Add(reserve);
                // Packing is evaluated ahead, but actual placements remain varied.
                // Existing photos are never moved or resized to admit a new one.
                int capacity=Capacity(combined,minimumReservation,Math.Max(0,4-combined.Count));
                var remaining=Free(combined); double largest=0;
                foreach(var area in remaining) largest=Math.Max(largest,area.Width*area.Height);
                double cx=x+width/2,cy=y+height/2;
                foreach(var other in occupied){cx+=other.X+other.Width/2;cy+=other.Y+other.Height/2;}
                cx/=combined.Count;cy/=combined.Count;
                double score=capacity+largest*.25+random.NextDouble()*.28-(Math.Abs(cx-.5)+Math.Abs(cy-.5))*.20+width*height*.15;
                if(score>bestScore){bestScore=score;placement=new PhotoPlacement{Base=basis,Reservation=reserve};}
            }
            return placement!=null;
        }
        private static int Capacity(List<Rect> occupied,Size size,int maximum)
        {
            var trial=new List<Rect>(occupied);int count=0;
            while(count<maximum)
            {
                var free=Free(trial);Rect? chosen=null;double waste=Double.MaxValue;
                foreach(var area in free)
                    if(area.Width>=size.Width && area.Height>=size.Height)
                    {double value=area.Width*area.Height-size.Width*size.Height;if(value<waste){waste=value;chosen=area;}}
                if(!chosen.HasValue) break;
                trial.Add(new Rect(chosen.Value.X,chosen.Value.Y,size.Width,size.Height));count++;
            }
            return count;
        }
        private static List<Rect> Free(IList<Rect> occupied)
        {
            var free=new List<Rect>{Bounds};
            foreach(var used in occupied)
            {
                var next=new List<Rect>();
                foreach(var area in free)
                {
                    if(!area.IntersectsWith(used)){next.Add(area);continue;}
                    if(used.Left>area.Left) next.Add(new Rect(area.Left,area.Top,used.Left-area.Left,area.Height));
                    if(used.Right<area.Right) next.Add(new Rect(used.Right,area.Top,area.Right-used.Right,area.Height));
                    if(used.Top>area.Top) next.Add(new Rect(area.Left,area.Top,area.Width,used.Top-area.Top));
                    if(used.Bottom<area.Bottom) next.Add(new Rect(area.Left,used.Bottom,area.Width,area.Bottom-used.Bottom));
                }
                for(int i=next.Count-1;i>=0;i--) for(int j=0;j<next.Count;j++)
                    if(i!=j && next[j].Contains(next[i]) && (!next[i].Equals(next[j])||j<i)){next.RemoveAt(i);break;}
                free=next;
            }
            return free;
        }
    }
}
