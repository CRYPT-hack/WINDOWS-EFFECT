using System;
using System.Collections.Generic;
using System.Windows;

namespace WindowWeather {
    internal enum LedgeKind { Window, Icon, Taskbar, Ground }

    internal struct Ledge {
        internal long Key;
        internal double Left, Right, Y;
        internal int Depth;
        internal LedgeKind Kind;
        internal Ledge(long key,double left,double right,double y,int depth,LedgeKind kind) {
            Key=key;Left=left;Right=right;Y=y;Depth=depth;Kind=kind;
        }
    }

    // Loose powder and denser packed snow share the same horizontal grid.
    // Heights are in display-independent pixels. Each node spans about eight pixels.
    internal sealed class Drift {
        internal Ledge Edge;
        internal double[] Loose,Packed;
        internal double Step;
        internal bool Active;
        internal Drift(Ledge ledge) {
            Edge=ledge;
            int nodes=Math.Max(2,1+(int)Math.Ceiling((ledge.Right-ledge.Left)/8));
            Loose=new double[nodes];Packed=new double[nodes];
            Step=(ledge.Right-ledge.Left)/(nodes-1);
        }
        internal double Height(int i){return Loose[i]+Packed[i]*.63;}
        internal double X(int i){return Edge.Left+Step*i;}
        internal double HeightAt(double x){
            double t=Math.Max(0,Math.Min(Loose.Length-1,(x-Edge.Left)/Step));
            int i=(int)t,j=Math.Min(i+1,Loose.Length-1);
            return Height(i)+(Height(j)-Height(i))*(t-i);
        }
        internal void Deposit(double x,double mass){
            double t=Math.Max(0,Math.Min(Loose.Length-1,(x-Edge.Left)/Step));
            int i=(int)t,j=Math.Min(Loose.Length-1,i+1);
            Loose[i]+=mass*(1-(t-i));Loose[j]+=mass*(t-i);Active=true;
        }
        internal double Remove(int i,double amount) {
            double a=Math.Min(Loose[i],amount),b=Math.Min(Packed[i],amount-a);
            Loose[i]-=a;Packed[i]-=b;return a+b;
        }
    }

    internal sealed class Snowpack {
        private const int BucketWidth=80;
        private Dictionary<long,Drift> drifts=new Dictionary<long,Drift>();
        private List<Drift>[] buckets=new List<Drift>[0];
        private List<Ledge> layout=new List<Ledge>();
        private readonly Action<double,double,double,double,int> shed;
        private double elapsed;
        internal Point LastImpact {get;private set;}
        internal int ImpactDepth {get;private set;}
        internal Snowpack(Action<double,double,double,double,int> shedSnow){shed=shedSnow;}
        internal Drift Find(long key){Drift d;return drifts.TryGetValue(key,out d) ? d : null;}
        internal static bool Snowing(Weather mode){return mode==Weather.Snow || mode==Weather.Sleet || mode==Weather.Blizzard;}
        private static bool Same(Ledge a,Ledge b){
            return a.Key==b.Key && a.Left==b.Left && a.Right==b.Right && a.Y==b.Y && a.Depth==b.Depth && a.Kind==b.Kind;
        }
        internal void SetLedges(List<Ledge> next,double width){
            int count=Math.Max(1,(int)Math.Ceiling(width/BucketWidth));
            bool same=layout.Count==next.Count && buckets.Length==count;
            for(int i=0;same && i<next.Count;i++)same=Same(layout[i],next[i]);
            if(same)return;
            var current=new Dictionary<long,Drift>();
            buckets=new List<Drift>[count];
            for(int i=0;i<count;i++)buckets[i]=new List<Drift>();
            foreach(var e in next){
                if(e.Right-e.Left<2)continue;
                Drift d;
                if(drifts.TryGetValue(e.Key,out d)){
                    var old=d.Edge;
                    bool shift=Math.Abs(e.Left-old.Left)>18 || Math.Abs(e.Y-old.Y)>12;
                    if(shift && d.Active)Release(d,.52,e.Left-old.Left);
                    if(Math.Abs((e.Right-e.Left)-(old.Right-old.Left))>.1){
                        var resized=new Drift(e);
                        if(d.Active){
                            double dx=e.Left-old.Left;
                            for(int i=0;i<d.Loose.Length;i++){
                                double x=d.X(i)+dx, mass=d.Loose[i]+d.Packed[i];
                                if(x<=e.Right)resized.Deposit(x,mass);
                                else if(mass>.1)shed(d.X(i),old.Y+1,mass,10,old.Depth);
                            }
                        }
                        d=resized;
                    }
                    d.Edge=e;
                } else d=new Drift(e);
                current[e.Key]=d;
                int first=Math.Max(0,(int)Math.Floor(e.Left/BucketWidth));
                int last=Math.Min(count-1,(int)Math.Floor(e.Right/BucketWidth));
                for(int k=first;k<=last;k++)buckets[k].Add(d);
            }
            foreach(var pair in drifts)if(!current.ContainsKey(pair.Key) && pair.Value.Active)Release(pair.Value,1,0);
            drifts=current;layout=next;
        }
        private void Release(Drift d,double fraction,double dx){
            for(int i=0;i<d.Loose.Length;i++){
                double mass=d.Remove(i,(d.Loose[i]+d.Packed[i])*fraction);
                if(mass>.13)shed(d.X(i),d.Edge.Y+1,mass,Math.Max(-70,Math.Min(70,-dx*.5)),d.Edge.Depth);
            }
        }
        private static bool Exposed(List<Surface> windows,int depth,double x,double y){
            for(int i=0;i<depth && i<windows.Count;i++)if(windows[i].Box.Contains(x,y))return false;
            return true;
        }
        internal bool Land(Particle p,double oldX,double oldY,List<Surface> windows){
            if(buckets.Length==0 || p.Y<=oldY)return false;
            int from=Math.Max(0,(int)Math.Floor(Math.Min(oldX,p.X)/BucketWidth));
            int to=Math.Min(buckets.Length-1,(int)Math.Floor(Math.Max(oldX,p.X)/BucketWidth));
            Drift hit=null;double first=2,landingX=0;
            for(int k=from;k<=to;k++)foreach(var d in buckets[k]){
                Ledge e=d.Edge;
                if(e.Kind==LedgeKind.Window && p.Layer>e.Depth)continue;
                if(oldY>e.Y+1 || p.Y<e.Y-60)continue;
                double x=Math.Max(e.Left,Math.Min(e.Right,p.X));
                double y=e.Y-d.HeightAt(x);
                double t=(y-oldY)/(p.Y-oldY);
                if(t<0 && oldY<=e.Y+.1)t=0;
                if(t<0 || t>1 || t>=first)continue;
                x=oldX+(p.X-oldX)*t;
                if(x<e.Left || x>e.Right || !Exposed(windows,e.Depth,x,y-.5))continue;
                first=t;hit=d;landingX=x;
            }
            if(hit==null)return false;
            LastImpact=new Point(landingX,hit.Edge.Y-hit.HeightAt(landingX));ImpactDepth=hit.Edge.Depth;
            hit.Deposit(landingX,p.Mass>0 ? p.Mass : 2.1*p.Size*p.Size);
            return true;
        }
        internal void Brush(Point previous,Point cursor,List<Surface> windows){
            Vector swipe=cursor-previous;
            if(double.IsNaN(swipe.X) || swipe.Length<1 || swipe.Length>190)return;
            int direction=swipe.X>=0 ? 1 : -1;
            foreach(var d in drifts.Values){
                if(!d.Active)continue;
                if(Math.Min(previous.Y,cursor.Y)>d.Edge.Y+12 || Math.Max(previous.Y,cursor.Y)<d.Edge.Y-50)continue;
                double lo=Math.Min(previous.X,cursor.X)-12,hi=Math.Max(previous.X,cursor.X)+12;
                int start=Math.Max(0,(int)Math.Floor((lo-d.Edge.Left)/d.Step));
                int end=Math.Min(d.Loose.Length-1,(int)Math.Ceiling((hi-d.Edge.Left)/d.Step));
                // Walk against the swipe so the same snow moves only once.
                for(int j=start;j<=end;j++){
                    int i=direction>0 ? end-(j-start) : j;
                    double x=d.X(i), y=d.Edge.Y-d.Height(i);
                    double t=Math.Max(0,Math.Min(1,Vector.Multiply(new Point(x,y)-previous,swipe)/swipe.LengthSquared));
                    Point near=previous+swipe*t;
                    if((new Point(x,y)-near).Length>15 || !Exposed(windows,d.Edge.Depth,x,y-.5))continue;
                    double mass=d.Remove(i,Math.Min(9,swipe.Length*.40));
                    if(mass<.01)continue;
                    int dest=i+direction*Math.Max(1,(int)Math.Round(Math.Abs(swipe.X)/d.Step));
                    if(dest<0 || dest>=d.Loose.Length)shed(direction>0 ? d.Edge.Right+2 : d.Edge.Left-2,y,mass,swipe.X*3,d.Edge.Depth);
                    else d.Loose[dest]+=mass;
                }
            }
        }
        internal void Step(double dt,Weather mode,double wind) {
            elapsed+=dt;
            if(elapsed<.09)return;
            double step=Math.Min(.25,elapsed);elapsed=0;
            double thaw=Snowing(mode) ? .004 : mode==Weather.Sunlight ? .95 : .46;
            foreach(var d in drifts.Values){
                if(!d.Active)continue;
                double total=0;
                double max=d.Edge.Kind==LedgeKind.Icon ? 15 : 29;
                for(int i=0;i<d.Loose.Length;i++){
                    double compact=d.Loose[i]*(1-Math.Exp(-step*.075));
                    d.Loose[i]-=compact; d.Packed[i]+=compact;
                    d.Remove(i,thaw*step);
                    if(i+1<d.Loose.Length){
                        double slope=d.Height(i)-d.Height(i+1)+wind*.006;
                        double excess=Math.Abs(slope)-d.Step*.44;
                        if(excess>0){
                            int high=slope>0 ? i : i+1,low=slope>0 ? i+1 : i;
                            double flow=d.Remove(high,excess*Math.Min(.45,step*2));
                            d.Loose[low]+=flow;
                        }
                    }
                    double excessHeight=d.Height(i)-max;
                    if(excessHeight>0){
                        double excess=d.Remove(i,excessHeight);
                        int target=i+(i<d.Loose.Length/2 ? -1 : 1);
                        if(target>=0 && target<d.Loose.Length)d.Loose[target]+=excess;
                        else shed(target<0 ? d.Edge.Left-2 : d.Edge.Right+2,d.Edge.Y+1,excess,target<0 ? -22 : 22,d.Edge.Depth);
                    }
                    total+=d.Loose[i]+d.Packed[i];
                }
                if(mode==Weather.Blizzard && Math.Abs(wind)>78 && d.Edge.Kind!=LedgeKind.Ground){
                    int edge=wind>0 ? d.Loose.Length-1 : 0;
                    if(d.Loose[edge]>2){
                        double blown=Math.Min(d.Loose[edge],step*(Math.Abs(wind)-60)*.1);
                        d.Loose[edge]-=blown;
                        shed(d.X(edge)+Math.Sign(wind)*2,d.Edge.Y-d.Height(edge),blown,wind*.72,d.Edge.Depth);
                    }
                }
                d.Active=total>.015;
            }
        }
        internal void Draw(WeatherCanvas dc){
            foreach(var d in drifts.Values){
                if(!d.Active)continue;
                if(d.Edge.Depth>0)dc.PushLayer(d.Edge.Depth);
                double left=d.Height(0);
                for(int i=1;i<d.Loose.Length;i++){
                    double right=d.Height(i);
                    if(Math.Max(left,right)>.06)dc.DrawSnow(d.X(i-1),d.X(i),d.Edge.Y,left,right);
                    left=right;
                }
                if(d.Edge.Depth>0)dc.Pop();
            }
        }
    }
}
