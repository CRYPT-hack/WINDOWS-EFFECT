using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace WindowWeather {
    // Visitors use sampled cursor positions. The overlay stays click-through.
    internal sealed class Visitors {
        private readonly Random random=new Random(Guid.NewGuid().GetHashCode());
        private IntPtr perch;
        private double offset,age,delay=6,hover;
        private int color;
        private bool departing;
        private Point previous=new Point(double.NaN,double.NaN);
        private static readonly Brush[] plumage={Brushes.DarkSlateGray,Brushes.Sienna,Brushes.SlateBlue};
        private static readonly Brush[] wing={Brushes.CadetBlue,Brushes.Peru,Brushes.SlateGray};
        internal event Action Found;
        internal void Reset(){perch=IntPtr.Zero;delay=6;previous=new Point(double.NaN,double.NaN);}
        private static bool Exposed(List<Surface> windows,int index,Point p) {
            for(int i=0;i<index;i++) if(windows[i].Box.Contains(p)) return false;
            return true;
        }
        internal void Step(Scene view,List<Surface> windows,List<Deposit> deposits,List<Particle> falling,Preferences prefs,double dt) {
            bool brushSnow=prefs.Mode==Weather.Snow || prefs.Mode==Weather.Sleet;
            if(!prefs.Visitors && !brushSnow) {previous.X=double.NaN;perch=IntPtr.Zero;return;}
            Point cursor=new Point(double.NaN,double.NaN);
            if(view.Direct || PresentationSource.FromVisual(view)!=null) {
                var screen=Forms.Cursor.Position;
                cursor=new Point((screen.X-view.ScreenOrigin.X)/view.DisplayScale,(screen.Y-view.ScreenOrigin.Y)/view.DisplayScale);
            }
            double motion=cursor.X-previous.X;
            if(brushSnow && Math.Abs(motion)>.8 && Math.Abs(motion)<110) {
                for(int i=deposits.Count-1;i>=0;i--) {
                    var d=deposits[i];
                    if(d.Kind!=Material.Flake) continue;
                    int n;
                    if(!view.TryGetSurface(d.Owner,out n)) continue;
                    var b=windows[n].Box;
                    var point=new Point(b.Left+d.X,b.Top-1);
                    if(Math.Abs(cursor.X-point.X)>17 || Math.Abs(cursor.Y-point.Y)>14 || !Exposed(windows,n,point)) continue;
                    d.X+=motion*.95;
                    if(d.X<2 || d.X>b.Width-2) {
                        if(falling.Count<650) falling.Add(new Particle {X=point.X+motion,Y=point.Y+4,Speed=53,Size=d.Size,Kind=Material.Flake});
                        deposits.RemoveAt(i);
                    }
                }
            }
            previous=cursor;
            if(!prefs.Visitors){perch=IntPtr.Zero;return;}
            if(perch==IntPtr.Zero) {
                if((delay-=dt)>0) return;
                delay=2;
                if(windows.Count==0) return;
                int n=random.Next(windows.Count);
                var s=windows[n]; var b=s.Box;
                double lo=Math.Max(12,12-b.Left),hi=Math.Min(b.Width-12,view.ActualWidth-b.Left-12);
                if(b.Top<18 || b.Top>view.ActualHeight || hi<=lo) return;
                offset=lo+random.NextDouble()*(hi-lo);
                if(!Exposed(windows,n,new Point(b.Left+offset,b.Top-4)))return;
                perch=s.Id;color=random.Next(3);age=0;hover=0;departing=false;return;
            }
            age+=dt;
            int at=windows.FindIndex(delegate(Surface s){return s.Id==perch;});
            if(at<0 || age>(departing ? .8 : 19)) {perch=IntPtr.Zero; delay=8+random.NextDouble()*12;return;}
            if(departing)return;
            var box=windows[at].Box;
            bool near=Math.Abs(cursor.X-(box.Left+offset))<18 && Math.Abs(cursor.Y-(box.Top-7))<17 && Exposed(windows,at,cursor);
            hover=near ? hover+dt : 0;
            if(hover>.40){departing=true;age=0;if(Found!=null) Found();}
        }
        internal void Draw(WeatherCanvas dc,Surface surface) {
            if(perch==IntPtr.Zero || surface.Id!=perch)return;
            double flight=departing ? age*65 : 0;
            var m=Matrix.Identity;m.Translate(surface.Box.Left+offset+flight,surface.Box.Top-flight*.75);dc.PushTransform(m);
            dc.DrawPath(plumage[color],PathKind.Tail);
            dc.DrawEllipse(plumage[color],new Point(-1,-5.8),7,5.3);
            double bob=Math.Sin(age*3.1)*.65;
            dc.DrawEllipse(plumage[color],new Point(4,-9+bob),4.5,4.6);
            dc.DrawEllipse(wing[color],new Point(-2,-6-(departing ? 2*Math.Sin(age*20) : 0)),4.6,2.5);
            dc.DrawPath(Brushes.Goldenrod,PathKind.Beak);
            dc.DrawEllipse(Brushes.Ivory,new Point(5.8,-10.1+bob),1.1,1.1);
            dc.DrawEllipse(Brushes.Black,new Point(6.1,-10.1+bob),.65,.65);
            dc.Pop();
        }
    }
}