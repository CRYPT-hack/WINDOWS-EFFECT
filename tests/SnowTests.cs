using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using Forms=System.Windows.Forms;

namespace WindowWeather {
    internal static class SnowTests {
        private static void Check(bool ok,string message){
            if(!ok)throw new InvalidOperationException(message);
            Console.WriteLine("PASS "+message);
        }
        private static Particle Flake(double x,double y,double mass){
            return new Particle {X=x,Y=y,Size=1,Mass=mass,Kind=Material.Flake};
        }
        private static void Physics(){
            double shed=0;
            var field=new Snowpack(delegate(double x,double y,double mass,double velocity,int depth){shed+=mass;});
            var windows=new List<Surface>{new Surface {Id=new IntPtr(1),Box=new Rect(0,8,44,60)}};
            var icon=new Ledge(-2,15,65,45,1,LedgeKind.Icon);
            var bar=new Ledge(2,0,240,130,0,LedgeKind.Taskbar);
            field.SetLedges(new List<Ledge>{icon,bar},240);
            Check(!field.Land(Flake(30,47,2),30,30,windows),"covered desktop icon rejects snowfall");
            Check(field.Land(Flake(52,47,4),52,30,windows),"exposed part of desktop icon catches snow");
            double h=field.Find(-2).HeightAt(52);
            Check(h>1 && field.Land(Flake(52,45-h+.1,3),52,36,windows),"next flake hits the raised snow surface");
            var deep=Flake(102,135,4);deep.Layer=3;
            Check(field.Land(deep,102,90,windows),"visible taskbar catches snow from all particle layers");
            for(int i=0;i<18;i++)field.Land(Flake(120,132,4),120,60,windows);
            Drift bank=field.Find(2);
            double center=bank.HeightAt(120);
            for(int i=0;i<40;i++)field.Step(.1,Weather.Snow,0);
            Check(bank.HeightAt(120)<center && bank.HeightAt(112)>0,"steep snow spreads into adjacent cells");
            double packed=0;foreach(double p in bank.Packed)packed+=p;
            Check(packed>0,"loose snow compacts with time");
            var d=field.Find(-2);double oldHeight=d.HeightAt(52);
            field.Brush(new Point(50,42),new Point(88,42),windows);
            Check(d.HeightAt(52)<oldHeight && shed>0,"brushing an exposed icon spills snow from the edge");
            double before=shed;
            field.SetLedges(new List<Ledge>{icon},240);
            Check(shed>before && field.Find(2)==null,"removed supports release their snow");
            d.Deposit(49,12);double massBeforeMove=d.HeightAt(49);
            icon.Left+=30;icon.Right+=30;
            field.SetLedges(new List<Ledge>{icon},240);
            Check(d.HeightAt(79)>0 && d.HeightAt(79)<massBeforeMove,"a moved support carries some snow and sheds loose clumps");
            for(int i=0;i<350;i++)field.Step(.1,Weather.Sunlight,0);
            Check(!d.Active,"sunlight melts the remaining cap");
        }
        private static void Live(){
            var prefs=new Preferences {Mode=Weather.Snow};
            var overlay=new Overlay(Forms.Screen.PrimaryScreen,prefs);
            try {
                Check(overlay.Accelerated,"Direct3D overlay available");
                var reader=new DesktopIcons();
                var sw=Stopwatch.StartNew();
                do{reader.Update(sw.Elapsed.TotalSeconds,true);Thread.Sleep(20);}
                while(reader.Items.Length==0 && sw.Elapsed.TotalSeconds<5);
                Check(reader.Items.Length>0,"Explorer returns visible icon geometry ("+reader.Items.Length+" icons)");
                var windows=Desktop.ReadWindows();
                overlay.Refresh(windows,Desktop.GetForegroundWindow(),reader.Items);
                int registered=0;int bars=0;
                foreach(var icon in reader.Items)if(overlay.View.Snow.Find(icon.Key|long.MinValue)!=null)registered++;
                foreach(var w in windows)if(w.Taskbar && overlay.View.Snow.Find(w.Id.ToInt64())!=null)bars++;
                Check(registered>0 && bars>0,"active display registers "+registered+" icon ledges and "+bars+" taskbar ledge(s)");
                // Keep actual Explorer positions, then simulate an unobstructed desktop.
                windows.RemoveAll(delegate(Surface w){return !w.Taskbar;});
                overlay.Refresh(windows,IntPtr.Zero,reader.Items);
                foreach(var icon in reader.Items){
                    var d=overlay.View.Snow.Find(icon.Key|long.MinValue);
                    if(d==null || d.Edge.Y<12)continue;
                    double x=(d.Edge.Left+d.Edge.Right)*.5;
                    Check(overlay.View.Snow.Land(Flake(x,d.Edge.Y+1,5),x,d.Edge.Y-3,overlay.View.Surfaces) && d.Active,
                        "flake lands on a measured Explorer icon");
                    break;
                }
                foreach(var w in windows){
                    var d=overlay.View.Snow.Find(w.Id.ToInt64());
                    if(d==null)continue;
                    double x=(d.Edge.Left+d.Edge.Right)*.5;
                    Check(overlay.View.Snow.Land(Flake(x,d.Edge.Y+1,5),x,d.Edge.Y-3,overlay.View.Surfaces) && d.Active,
                        "flake lands on the measured taskbar edge");break;
                }
                overlay.Advance(.021);Check(overlay.Accelerated,"snowpack frame is presented on the GPU");
            } finally {overlay.Close();}
        }
        [STAThread] private static int Main(string[] args){
            try{Physics();if(Array.IndexOf(args,"--live")>=0)Live();return 0;}
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
