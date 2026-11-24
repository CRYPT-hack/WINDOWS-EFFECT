using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace WindowWeather {
    internal sealed class Surface {
        public IntPtr Id;
        public Rect Box;
    }

    internal static class Desktop {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Box { public int Left, Top, Right, Bottom; }
        private delegate bool Visitor(IntPtr handle, IntPtr arg);
        [DllImport("user32")] private static extern bool EnumWindows(Visitor visit, IntPtr arg);
        [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32")] private static extern bool IsIconic(IntPtr handle);
        [DllImport("user32")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder name, int length);
        [DllImport("user32", EntryPoint="GetWindowLongW")] internal static extern int GetStyle(IntPtr handle, int index);
        [DllImport("user32", EntryPoint="SetWindowLongW")] internal static extern int SetStyle(IntPtr handle, int index, int style);
        [DllImport("user32")] private static extern bool GetWindowRect(IntPtr handle, out Box rect);
        [DllImport("dwmapi", EntryPoint="DwmGetWindowAttribute")] private static extern int DwmBounds(IntPtr handle, int attr, out Box rect, int size);
        [DllImport("dwmapi", EntryPoint="DwmGetWindowAttribute")] private static extern int DwmInt(IntPtr handle, int attr, out int value, int size);
        [DllImport("user32")] internal static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32")] internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
        [DllImport("user32")] internal static extern bool UnregisterHotKey(IntPtr handle, int id);

        internal static List<Surface> ReadWindows() {
            var windows = new List<Surface>();
            var name = new StringBuilder(256);
            EnumWindows(delegate(IntPtr h, IntPtr unused) {
                if (!IsWindowVisible(h) || IsIconic(h)) return true;
                int ex = GetStyle(h, -20);
                if ((ex & (0x80 | 0x20 | 0x08000000)) != 0) return true;
                int cloaked;
                if (DwmInt(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                GetClassName(h, name, name.Capacity);
                switch (name.ToString()) {
                    case "Progman": case "WorkerW": case "Shell_TrayWnd": case "Shell_SecondaryTrayWnd":
                    case "Windows.UI.Core.CoreWindow": case "#32768": return true;
                }
                Box b;
                if (DwmBounds(h, 9, out b, 16) != 0 && !GetWindowRect(h, out b)) return true;
                if (b.Right-b.Left < 48 || b.Bottom-b.Top < 28) return true;
                windows.Add(new Surface { Id=h, Box=new Rect(b.Left,b.Top,b.Right-b.Left,b.Bottom-b.Top) });
                return true;
            }, IntPtr.Zero);
            return windows;
        }
    }

    internal sealed class FallbackWindow : Window {
        internal readonly Forms.Screen Monitor;

        internal IntPtr Handle { get { return new WindowInteropHelper(this).Handle; } }
        internal FallbackWindow(Forms.Screen screen, Scene view) {
            Monitor = screen;
            Title = "Window Weather overlay";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Topmost = true;
            Width = Height = 1;
            Content = view;
            SourceInitialized += delegate {
                Desktop.SetStyle(Handle,-20,Desktop.GetStyle(Handle,-20) | 0x20 | 0x80 | 0x08000000);
                HwndSource.FromHwnd(Handle).AddHook(Hook);
            };
            Show();
            Place();
        }
        internal void Place() {
            var r = Monitor.Bounds;
            Desktop.SetWindowPos(Handle,new IntPtr(-1),r.X,r.Y,r.Width,r.Height,0x0010);
        }
        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) {
            if (msg == 0x84) { handled = true; return new IntPtr(-1); }
            if (msg == 0x21) { handled = true; return new IntPtr(3); }
            return IntPtr.Zero;
        }
    }
    internal sealed class Overlay {
        internal readonly Forms.Screen Monitor;
        internal readonly Scene View;
        internal bool Sleeping,Fullscreen;
        private GpuOverlay gpu;
        private FallbackWindow fallback;
        internal double ActualWidth {get{return View.ActualWidth;}}
        internal double ActualHeight {get{return View.ActualHeight;}}
        internal bool IsVisible {get{return !Sleeping;}}
        internal bool Accelerated {get{return gpu!=null;}}
        internal IntPtr Handle {get{return gpu==null ? fallback.Handle : gpu.Handle;}}
        internal Overlay(Forms.Screen screen,Preferences prefs) {
            Monitor=screen;View=new Scene(prefs);
            var r=screen.Bounds;
            View.ScreenOrigin=new Point(r.X,r.Y);
            gpu=GpuOverlay.TryCreate(r);
            if(gpu==null) UseFallback();
            else {
                View.Direct=true;View.DisplayScale=gpu.Scale;
                var size=new Size(r.Width/gpu.Scale,r.Height/gpu.Scale);
                View.Measure(size);View.Arrange(new Rect(size));Show();
            }
        }
        private void UseFallback() {
            if(gpu!=null){gpu.Dispose();gpu=null;}
            View.Direct=false;
            fallback=new FallbackWindow(Monitor,View);
            View.DisplayScale=HwndSource.FromHwnd(fallback.Handle).CompositionTarget.TransformToDevice.M11;
        }
        internal void Place(){
            if(gpu==null) fallback.Place();
            else {var r=Monitor.Bounds;Desktop.SetWindowPos(Handle,new IntPtr(-1),r.X,r.Y,r.Width,r.Height,0x0010);}
        }
        internal void Show(){Sleeping=false;if(gpu==null)fallback.Show();else GpuOverlay.ShowWindow(Handle,4);}
        internal void Hide(){Sleeping=true;if(gpu==null)fallback.Hide();else GpuOverlay.ShowWindow(Handle,0);}
        internal void Close(){if(gpu!=null)gpu.Dispose();else fallback.Close();}
        internal void Advance(double dt){
            View.Advance(dt);
            if(gpu!=null && !gpu.Draw(View)) UseFallback();
        }
        internal void Refresh(List<Surface> windows, IntPtr foreground) {
            var screen = Monitor.Bounds;
            var area = new Rect(screen.X,screen.Y,screen.Width,screen.Height);
            double scale=View.DisplayScale;
            var local = new List<Surface>();
            Fullscreen = false;
            foreach (var surface in windows) {
                if (!area.IntersectsWith(surface.Box)) continue;
                var b = surface.Box;
                if (surface.Id == foreground && b.Contains(area)) Fullscreen = true;
                local.Add(new Surface { Id=surface.Id, Box=new Rect((b.X-area.X)/scale,(b.Y-area.Y)/scale,b.Width/scale,b.Height/scale) });
            }
            View.SetWindows(local);
        }
    }
}