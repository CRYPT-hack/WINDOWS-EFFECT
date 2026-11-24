using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WindowWeather {
    public enum Weather { Rain, Snow, Sleet, Leaves, Sunlight, Fog }
    internal enum Material { Drop, Flake, Leaf }

    internal sealed class Particle {
        internal double X,Y,Speed,Size,Phase,Drift;
        internal int Layer, Tint;
        internal Material Kind;
    }
    internal sealed class Deposit {
        internal IntPtr Owner;
        internal double X, Age, Life, Size, Trail;
        internal bool Right;
        internal int Tint;
        internal Material Kind;
    }
    internal sealed class Scene : FrameworkElement {
        internal readonly Visitors Birds=new Visitors();
        private readonly Preferences prefs;
        private readonly Random random = new Random(Guid.NewGuid().GetHashCode());
        private readonly List<Particle> falling = new List<Particle>();
        private readonly List<Deposit> settled = new List<Deposit>();
        private List<Surface> windows = new List<Surface>();
        private readonly Dictionary<IntPtr,int> index = new Dictionary<IntPtr,int>();
        private readonly List<Geometry> clips = new List<Geometry>();
        private Size clipSize;
        internal int Revision {get; private set;}
        internal List<Surface> Surfaces {get{return windows;}}
        internal bool Direct;
        internal Point ScreenOrigin;
        internal double DisplayScale=1;
        internal bool TryGetSurface(IntPtr id,out int n){return index.TryGetValue(id,out n);}
        private Weather last;
        private double time, credit;
        private bool warm = true;
        private static readonly Pen[] rain = { Stroke(84,146,182,205,.85), Stroke(130,170,204,222,1.05), Stroke(167,187,217,231,1.25) };
        private static readonly Pen glint = Stroke(163,232,246,248,0.9);
        private static readonly Pen waterLine = Stroke(115,105,155,178,1.8);
        private static readonly Brush water = Ink(135,142,190,211);
        private static readonly Brush frost = Ink(229,235,245,250);
        private static readonly Brush[] flakes = { Ink(147,215,228,237),Ink(199,226,239,245),Ink(231,245,251,253) };
        private static readonly Brush[] leaves = { Ink(224,189,110,43),Ink(224,185,75,43),Ink(225,212,153,48),Ink(221,142,108,48) };
        private static readonly Pen vein = Stroke(160,248,203,119,.65);
        private static readonly Pen grass = Stroke(171,111,146,92,1.35);
        private static readonly Brush mist = Glow(Color.FromArgb(62,197,209,215));
        private static readonly Brush sun = Glow(Color.FromArgb(84,255,214,139));
        private static readonly Brush mote = Ink(153,255,226,170);
        private static Brush Ink(byte a, byte r, byte g, byte b) {
            var brush = new SolidColorBrush(Color.FromArgb(a,r,g,b)); brush.Freeze(); return brush;
        }
        private static Pen Stroke(byte a, byte r, byte g, byte b, double width) {
            var pen = new Pen(Ink(a,r,g,b),width) { StartLineCap=PenLineCap.Round, EndLineCap=PenLineCap.Round };
            pen.Freeze(); return pen;
        }
        private static Brush Glow(Color c) {
            var brush = new RadialGradientBrush(c,Color.FromArgb(0,c.R,c.G,c.B)); brush.Freeze(); return brush;
        }
        internal Scene(Preferences settings) { prefs=settings; ClipToBounds=true; IsHitTestVisible=false; }
        internal void SetWindows(List<Surface> next) {
            var size = RenderSize;
            bool same = size == clipSize && next.Count == windows.Count;
            for (int i=0; same && i<next.Count; i++)
                same = next[i].Id==windows[i].Id && next[i].Box==windows[i].Box;
            if (same) return;
            windows=next; clipSize=size; index.Clear(); clips.Clear();
            Revision++;
            for(int i=0;i<windows.Count;i++)index[windows[i].Id]=i;
            settled.RemoveAll(delegate(Deposit d) { return !index.ContainsKey(d.Owner); });
        }
        internal Geometry ClipFor(int n) {
            if(clips.Count==0) {
                var first=new RectangleGeometry(new Rect(RenderSize));first.Freeze();clips.Add(first);
            }
            while(clips.Count<=n) {
                var cut=new RectangleGeometry(windows[clips.Count-1].Box,5,5);
                var visible=Geometry.Combine(clips[clips.Count-1],cut,GeometryCombineMode.Exclude,null);
                visible.Freeze();clips.Add(visible);
            }
            return clips[n];
        }
        private Particle Spawn(bool initial) {
            var p=new Particle();
            p.Layer=random.NextDouble()<prefs.Depth/100.0 ? random.Next(1,Math.Max(1,Math.Min(3,windows.Count))+1) : 0;
            p.Kind = prefs.Mode==Weather.Snow || (prefs.Mode==Weather.Sleet && random.Next(2)==0) ? Material.Flake :
                     prefs.Mode==Weather.Leaves ? Material.Leaf : Material.Drop;
            p.X = random.NextDouble()*(ActualWidth+160)-80;
            p.Y = initial ? random.NextDouble()*ActualHeight : -15;
            p.Size = (.7+random.NextDouble()*.85)/(1+.13*p.Layer);
            p.Phase=random.NextDouble()*6.28; p.Tint=random.Next(leaves.Length);
            double velocity=p.Kind==Material.Drop ? 520 : p.Kind==Material.Flake ? 37 : 60;
            p.Speed=velocity*(.74+.48*random.NextDouble())*p.Size;
            return p;
        }
        internal void Advance(double dt) {
            if (ActualWidth<4 || ActualHeight<4) return;
            if (prefs.Mode!=last) { falling.Clear(); settled.Clear(); credit=0; warm=true; last=prefs.Mode; }
            time+=dt;
            Birds.Step(this,windows,settled,falling,prefs,dt);
            if (prefs.Mode==Weather.Sunlight || prefs.Mode==Weather.Fog) { if(!Direct) InvalidateVisual(); return; }
            double density = (.16+prefs.Intensity/62.0)*Math.Min(4,ActualWidth*ActualHeight/1152000.0);
            double rate = (prefs.Mode==Weather.Rain ? 95 : prefs.Mode==Weather.Sleet ? 32 : prefs.Mode==Weather.Snow ? 12 : 4.5)*density;
            if (warm) {
                int count=(int)Math.Min(290,rate*ActualHeight/(prefs.Mode==Weather.Rain ? 550 : prefs.Mode==Weather.Sleet ? 180 : 65));
                for(int i=0;i<count;i++) falling.Add(Spawn(true));
                warm=false;
            }
            credit+=dt*rate;
            while (credit>=1) { credit--; if(falling.Count<650) falling.Add(Spawn(false)); }
            for(int i=falling.Count-1;i>=0;i--) {
                var p=falling[i];
                double oldY=p.Y, oldX=p.X;
                p.Drift=prefs.Wind*(p.Kind==Material.Drop ? 1.6 : .9);
                if(p.Kind!=Material.Drop) p.Drift+=Math.Sin(time*1.45+p.Phase)*(p.Kind==Material.Leaf ? 30 : 16);
                p.X+=p.Drift*dt; p.Y+=p.Speed*dt;
                Surface hit=null;
                for(int j=Math.Min(p.Layer,windows.Count);j<windows.Count;j++) {
                    var b=windows[j].Box;
                    if (b.Top<4 || oldY>b.Top || p.Y<b.Top || (hit!=null && hit.Box.Top<=b.Top)) continue;
                    double x=oldX+(p.X-oldX)*(b.Top-oldY)/Math.Max(.001,p.Y-oldY);
                    if(x<b.Left+3 || x>b.Right-3) continue;
                    bool covered=false;
                    for(int k=0;k<j && !covered;k++) covered=windows[k].Box.Contains(new Point(x,b.Top-.5));
                    if(!covered) hit=windows[j];
                }
                if(hit!=null) {
                    if(settled.Count<750) {
                        var b=hit.Box;
                        var d=new Deposit { Owner=hit.Id, X=Math.Max(1,Math.Min(b.Width-1,p.X-b.Left)), Kind=p.Kind, Size=p.Size,
                            Right=p.X>b.Left+b.Width/2, Life=p.Kind==Material.Drop ? 5+random.NextDouble()*3 : 20+random.NextDouble()*22, Tint=p.Tint };
                        settled.Add(d);
                    }
                    falling.RemoveAt(i);
                } else if(p.Y>ActualHeight+24 || p.X < -100 || p.X>ActualWidth+100) falling.RemoveAt(i);
            }
            for(int i=settled.Count-1;i>=0;i--) {
                var d=settled[i]; int n;
                if(!index.TryGetValue(d.Owner,out n) || (d.Age+=dt)>d.Life) { settled.RemoveAt(i); continue; }
                var b=windows[n].Box;
                d.X=Math.Min(b.Width-2,Math.Max(2,d.X));
                if(d.Kind==Material.Drop && d.Age>.38) {
                    d.X+= (d.Right ? 1 : -1)*dt*(23+prefs.Intensity*1.2)*d.Size;
                    if(d.X<=2 || d.X>=b.Width-2 || d.Trail>0) {
                        d.X=d.Right ? b.Width-1 : 1;
                        d.Trail+=dt*(65+prefs.Intensity*1.6)*d.Size;
                        if(d.Trail>b.Height || (d.Trail>90 && prefs.Intensity<60)) settled.RemoveAt(i);
                    }
                }
            }
            if(!Direct) InvalidateVisual();
        }
        private void DrawLeaf(WeatherCanvas dc,double x,double y,double size,double angle,int tint) {
            var m=Matrix.Identity; m.Scale(size,size); m.Rotate(angle); m.Translate(x,y);
            dc.PushTransform(m);
            dc.DrawPath(leaves[tint],PathKind.Leaf);
            dc.DrawLine(vein,new Point(-1,-3),new Point(-.7,4)); dc.Pop();
        }
        private void DrawParticle(WeatherCanvas dc,Particle p) {
            var point=new Point(p.X,p.Y); int shade=p.Layer==0 ? 2 : p.Layer==1 ? 1 : 0;
            if(p.Kind==Material.Drop) {
                double streak=.018*p.Size;
                dc.DrawLine(rain[shade],new Point(p.X-p.Drift*streak,p.Y-p.Speed*streak),point);
                if(p.Size>1.18) dc.DrawEllipse(water,point,.8,1.35);
            } else if(p.Kind==Material.Leaf) {
                DrawLeaf(dc,p.X,p.Y,p.Size*(1+.2*Math.Sin(time*2+p.Phase)),45*Math.Sin(time*1.7+p.Phase)+20,p.Tint);
            } else {
                double r=1.8*p.Size;
                dc.DrawEllipse(flakes[shade],point,r,r*.94);
                if(p.Size>1.2) dc.DrawLine(glint,new Point(p.X-r*1.45,p.Y),new Point(p.X+r*1.45,p.Y));
            }
        }
        private void DrawDeposit(WeatherCanvas dc,Deposit d,Rect b) {
            double x=b.Left+d.X,y=b.Top-1,r=d.Size;
            if(d.Kind==Material.Leaf) { DrawLeaf(dc,x,y-1,r,69+d.Tint*15,d.Tint); return; }
            if(d.Kind==Material.Flake) {
                double fade=Math.Min(1,(d.Life-d.Age)/4);
                dc.DrawEllipse(frost,new Point(x,y),4.7*r*fade,2.5*r*fade); return;
            }
            if(d.Age<.26) {
                double t=d.Age/.26, dx=(2+4*t)*r,dy=4*Math.Sin(t*Math.PI);
                dc.DrawEllipse(water,new Point(x-dx,y-dy),.9,.85);
                dc.DrawEllipse(water,new Point(x+dx,y-dy),.9,.85);
            }
            if(d.Trail>0) {
                y=b.Top+d.Trail;
                dc.DrawLine(waterLine,new Point(x,Math.Max(b.Top,y-12-r*10)),new Point(x,y));
                dc.DrawEllipse(water,new Point(x,y),1.35*r,2.5*r);
            } else {
                dc.DrawEllipse(water,new Point(x,y),3.4*r,1.65*r);
                dc.DrawLine(glint,new Point(x-1.5*r,y-.55),new Point(x+.65*r,y-.7));
            }
        }
        private void DrawAtmosphere(WeatherCanvas dc) {
            double w=ActualWidth,h=ActualHeight, k=.20+prefs.Intensity*.008;
            dc.PushOpacity(k);
            if(prefs.Mode==Weather.Fog) {
                for(int i=0;i<6;i++) {
                    double x=w*(.5+.64*Math.Sin(time*(.035+Math.Abs(prefs.Wind)*.0005)+i*1.62));
                    double y=h*(.20+.13*i+.08*Math.Cos(time*.057+i));
                    dc.DrawEllipse(mist,new Point(x,y),w*.46,h*.13);
                }
            } else {
                dc.DrawEllipse(sun,new Point(w*.84,-h*.15),w*.58,h*.95);
                for(int i=0;i<32;i++) {
                    double x=(w*(i*.618%1)+Math.Sin(time*.2+i)*17);
                    double y=h*((i*.382+time*.012)%1);
                    dc.DrawEllipse(mote,new Point(x,y),1+.5*Math.Sin(i),1);
                }
            }
            dc.Pop();
        }
        protected override void OnRender(DrawingContext dc) {
            base.OnRender(dc);
            if(!Direct) Draw(new WpfCanvas(dc,this));
        }
        internal void Draw(WeatherCanvas dc) {
            if(prefs.Mode==Weather.Sunlight || prefs.Mode==Weather.Fog) DrawAtmosphere(dc);
            for(int z=Math.Min(3,windows.Count);z>=0;z--) {
                if(z>0) dc.PushLayer(z);
                foreach(var p in falling) if(Math.Min(p.Layer,Math.Min(3,windows.Count))==z) DrawParticle(dc,p);
                if(z>0) dc.Pop();
            }
            foreach(var d in settled) {
                int owner;
                if(!index.TryGetValue(d.Owner,out owner)) continue;
                if(owner>0) dc.PushLayer(owner);
                DrawDeposit(dc,d,windows[owner].Box);
                if(owner>0) dc.Pop();
            }
            if(!prefs.Grass && !prefs.Visitors)return;
            for(int n=windows.Count-1;n>=0;n--) {
                var s=windows[n];
                if(n>0) dc.PushLayer(n);
                if(prefs.Grass && s.Box.Top>5) {
                    double left=Math.Max(0,s.Box.Left+5), end=Math.Min(ActualWidth,s.Box.Right-4);
                    for(double x=left;x<end;x+=6) {
                        double seed=(x-s.Box.Left)*.731;
                        double sway=Math.Sin(time*1.7+seed*.12)*(1+Math.Abs(prefs.Wind)*.025);
                        dc.DrawLine(grass,new Point(x,s.Box.Top),new Point(x+sway+Math.Sin(seed)*2,s.Box.Top-4-3*Math.Cos(seed)));
                    }
                }
                if(prefs.Visitors) Birds.Draw(dc,s);
                if(n>0) dc.Pop();
            }
        }
    }
}