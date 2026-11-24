using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Runtime.InteropServices;

namespace WindowWeather {
    internal enum PathKind { Leaf, Tail, Beak }
    internal abstract class WeatherCanvas {
        internal abstract void DrawLine(Pen pen,Point a,Point b);
        internal abstract void DrawEllipse(Brush color,Point center,double rx,double ry);
        internal abstract void DrawPath(Brush color,PathKind kind);
        internal abstract void PushLayer(int count);
        internal abstract void PushTransform(Matrix m);
        internal abstract void PushOpacity(double opacity);
        internal abstract void Pop();
    }
    internal sealed class WpfCanvas : WeatherCanvas {
        private readonly DrawingContext dc;
        private readonly Scene scene;
        private static readonly Geometry[] paths={Geometry.Parse("M -1,-5 Q 5,-2 2,2 Q 0,4 -1,5 Q -5,0 -1,-5 Z"),
            Geometry.Parse("M -4,-7 L -14,-13 L -10,-4 L -4,-3 Z"),Geometry.Parse("M 7,-11 L 12,-9 L 7,-8 Z")};
        static WpfCanvas(){foreach(var path in paths) path.Freeze();}
        internal WpfCanvas(DrawingContext context,Scene view){dc=context;scene=view;}
        internal override void DrawLine(Pen p,Point a,Point b){dc.DrawLine(p,a,b);}
        internal override void DrawEllipse(Brush b,Point p,double rx,double ry){dc.DrawEllipse(b,null,p,rx,ry);}
        internal override void DrawPath(Brush b,PathKind k){dc.DrawGeometry(b,null,paths[(int)k]);}
        internal override void PushLayer(int n){dc.PushClip(scene.ClipFor(n));}
        internal override void PushTransform(Matrix m){dc.PushTransform(new MatrixTransform(m));}
        internal override void PushOpacity(double a){dc.PushOpacity(a);}
        internal override void Pop(){dc.Pop();}
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Mark {
        internal float X,Y,Rx,Ry,AxisX,AxisY;
        internal uint Color,Shape,Depth;
    }
    internal sealed class BatchCanvas : WeatherCanvas {
        internal Mark[] Marks=new Mark[512];
        internal int Count;
        private struct State {
            internal Matrix Transform;
            internal double Opacity;
            internal int Depth;
        }
        private readonly Stack<State> stack=new Stack<State>(8);
        private State state;
        private readonly Dictionary<Brush,uint> colors=new Dictionary<Brush,uint>();
        internal void Reset(){
            Count=0; stack.Clear(); state=new State {Transform=Matrix.Identity,Opacity=1};
        }
        private uint ColorOf(Brush b) {
            uint argb;
            if(!colors.TryGetValue(b,out argb)) {
                var solid=b as SolidColorBrush;
                Color c=solid!=null ? solid.Color : ((GradientBrush)b).GradientStops[0].Color;
                argb=(uint)(c.A<<24 | c.R<<16 | c.G<<8 | c.B);
                colors.Add(b,argb);
            }
            if(state.Opacity<1) argb=(argb&0xffffffu) | ((uint)((argb>>24)*state.Opacity)<<24);
            return argb;
        }
        private void Add(Point center,double rx,double ry,double ax,double ay,Brush color,uint shape) {
            if(Count==Marks.Length) Array.Resize(ref Marks,Marks.Length*2);
            Marks[Count++]=new Mark {X=(float)center.X,Y=(float)center.Y,Rx=(float)rx,Ry=(float)ry,
                AxisX=(float)ax,AxisY=(float)ay,Color=ColorOf(color),Shape=shape,Depth=(uint)state.Depth};
        }
        internal override void DrawLine(Pen pen,Point a,Point b) {
            a=state.Transform.Transform(a);b=state.Transform.Transform(b);
            double x=b.X-a.X,y=b.Y-a.Y,len=Math.Sqrt(x*x+y*y);
            double r=pen.Thickness*.5+.42;
            if(len<.01) return;
            Add(new Point((a.X+b.X)/2,(a.Y+b.Y)/2),len/2+r,r,x/len,y/len,pen.Brush,1);
        }
        internal override void DrawEllipse(Brush b,Point p,double rx,double ry){
            Add(state.Transform.Transform(p),rx,ry,1,0,b,b is GradientBrush ? 2u : 0u);
        }
        internal override void DrawPath(Brush b,PathKind kind){
            Matrix m=state.Transform;
            if(kind==PathKind.Leaf) {
                double scale=Math.Sqrt(m.M11*m.M11+m.M12*m.M12);
                Add(m.Transform(new Point(-.6,0)),3.1*scale,5*scale,m.M11/scale,m.M12/scale,b,3);
            } else if(kind==PathKind.Tail) Add(m.Transform(new Point(-8.5,-7.5)),6.5,2.75,.79,.61,b,3);
            else Add(m.Transform(new Point(9,-9.4)),3,1.65,1,0,b,4);
        }
        internal override void PushLayer(int n){stack.Push(state);state.Depth=n;}
        internal override void PushOpacity(double a){stack.Push(state);state.Opacity*=a;}
        internal override void PushTransform(Matrix m){stack.Push(state);m.Append(state.Transform);state.Transform=m;}
        internal override void Pop(){state=stack.Pop();}
    }
}
