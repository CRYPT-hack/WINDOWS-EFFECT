using System;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class CompositorTest {
    [DllImport("WindowWeather.Gpu.dll")] private static extern int WeatherCreate(int x,int y,int width,int height,out IntPtr state,out IntPtr hwnd);
    [DllImport("WindowWeather.Gpu.dll")] private static extern int WeatherFrame(IntPtr state,float[] marks,int count,float width,float height,float scale);
    [DllImport("WindowWeather.Gpu.dll")] private static extern int WeatherWindows(IntPtr state,float[] bounds,int count);
    [DllImport("WindowWeather.Gpu.dll")] private static extern void WeatherDestroy(IntPtr state);
    [DllImport("user32")] private static extern bool SetProcessDPIAware();
    [DllImport("user32")] private static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32")] private static extern IntPtr WindowFromPoint(Point position);
    [DllImport("user32")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32")] private static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32")] private static extern uint GetPixel(IntPtr dc,int x,int y);

    private static float Bits(uint n) {return BitConverter.ToSingle(BitConverter.GetBytes(n),0);}
    private static void Check(bool passed,string message) {
        if(!passed)throw new InvalidOperationException(message);
        Console.WriteLine("PASS "+message);
    }
    private static uint Pixel(Point p) {
        var dc=GetDC(IntPtr.Zero);
        try{return GetPixel(dc,p.X,p.Y);}
        finally{ReleaseDC(IntPtr.Zero,dc);}
    }
    private static void PresentUntil(IntPtr state,float[] mark,Point sample,uint expected) {
        var elapsed=Stopwatch.StartNew();
        do {
            Marshal.ThrowExceptionForHR(WeatherFrame(state,mark,1,180,100,1));
            Application.DoEvents();
            Thread.Sleep(20);
        } while (Pixel(sample)!=expected && elapsed.ElapsedMilliseconds<2500);
    }
    [STAThread] private static void Main() {
        SetProcessDPIAware();
        using(var fixture=new Form {TopMost=true,StartPosition=FormStartPosition.Manual,
            Location=new Point(70,120),Size=new Size(320,180),BackColor=Color.Blue,Text="Compositor test"}) {
            fixture.Show();Application.DoEvents();fixture.Refresh();
            var origin=fixture.PointToScreen(Point.Empty);
            var center=Point.Add(origin,new Size(90,50));
            IntPtr state,hwnd;
            Marshal.ThrowExceptionForHR(WeatherCreate(origin.X,origin.Y,180,100,out state,out hwnd));
            try {
                ShowWindow(hwnd,4);
                Marshal.ThrowExceptionForHR(WeatherWindows(state,null,0));
                var mark=new float[]{90,50,20,20,1,0,Bits(0xffff0000),0,0};
                PresentUntil(state,mark,center,0x0000ff);
                Check(Pixel(center)==0x0000ff,"opaque red mark is visible");
                Check(Pixel(Point.Add(origin,new Size(10,10)))==0xff0000,"transparent pixels reveal the blue fixture");
                Check(WindowFromPoint(center)==fixture.Handle,"a visible mark passes pointer input through");
                mark[8]=Bits(1);
                Marshal.ThrowExceptionForHR(WeatherWindows(state,new float[]{50,0,150,100},1));
                PresentUntil(state,mark,center,0xff0000);
                Check(Pixel(center)==0xff0000,"foreground window hides a particle behind it");
                Marshal.ThrowExceptionForHR(WeatherWindows(state,new float[]{0,0,25,25},1));
                PresentUntil(state,mark,center,0x0000ff);
                Check(Pixel(center)==0x0000ff,"moving the foreground window exposes the particle");
                var slope=new float[]{90,50,20,8,1,14,Bits(0xffffffff),Bits(5),0};
                var lower=Point.Add(origin,new Size(105,52));
                var upper=Point.Add(origin,new Size(75,46));
                var timer=Stopwatch.StartNew();
                do {
                    Marshal.ThrowExceptionForHR(WeatherFrame(state,slope,1,180,100,1));
                    Application.DoEvents();Thread.Sleep(20);
                } while(Pixel(lower)==0xff0000 && timer.ElapsedMilliseconds<2500);
                Check(Pixel(lower)!=0xff0000 && Pixel(upper)==0xff0000,"snow follows the simulated slope with a transparent crest");
            } finally {WeatherDestroy(state);}
        }
    }
}
