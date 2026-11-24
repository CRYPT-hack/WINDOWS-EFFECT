using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WindowWeather {
    internal sealed class WindowChanges : IDisposable {
        private delegate void Callback(IntPtr hook,uint evt,IntPtr hwnd,int obj,int child,uint thread,uint time);
        [DllImport("user32")] private static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,Callback proc,uint process,uint thread,uint flags);
        [DllImport("user32")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("gdi32",CharSet=CharSet.Unicode)] private static extern IntPtr CreateDC(string driver,string device,string output,IntPtr mode);
        [DllImport("gdi32")] private static extern int GetDeviceCaps(IntPtr dc,int index);
        [DllImport("gdi32")] private static extern bool DeleteDC(IntPtr dc);
        private readonly Callback callback;
        private readonly List<IntPtr> hooks=new List<IntPtr>();
        internal bool Dirty=true;
        internal WindowChanges(){
            callback=OnEvent;
            Subscribe(3,3);           // foreground
            Subscribe(0x000a,0x000b); // move/size start and end
            Subscribe(0x0016,0x0017); // minimize start and end
            Subscribe(0x8000,0x8004); // create, destroy, show, hide and reorder
            Subscribe(0x800b,0x800b); // location
        }
        private void Subscribe(uint first,uint last){
            IntPtr hook=SetWinEventHook(first,last,IntPtr.Zero,callback,0,0,0);
            if(hook!=IntPtr.Zero)hooks.Add(hook);
        }
        private void OnEvent(IntPtr hook,uint evt,IntPtr hwnd,int obj,int child,uint thread,uint time){
            if(hwnd==IntPtr.Zero || obj!=0 || child!=0)return;
            // Ignore caret/child control activity and movement inside a top-level window.
            if((Desktop.GetStyle(hwnd,-16)&0x40000000)==0)Dirty=true;
        }
        internal static int RefreshRate(string monitor){
            IntPtr dc=CreateDC("DISPLAY",monitor,null,IntPtr.Zero);
            if(dc==IntPtr.Zero)return 60;
            try {int hz=GetDeviceCaps(dc,116);return hz>=24 && hz<=600 ? hz : 60;}
            finally {DeleteDC(dc);}
        }
        public void Dispose(){foreach(var h in hooks)UnhookWinEvent(h);hooks.Clear();}
    }
}
