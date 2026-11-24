using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WindowWeather {
    [StructLayout(LayoutKind.Sequential)]
    internal struct IconLedge {
        internal long Key;
        internal Desktop.Box Box;
    }

    internal sealed class DesktopIcons : IDisposable {
        [DllImport("WindowWeather.Gpu.dll")]
        private static extern int WeatherDesktopIcons([Out] IconLedge[] icons,int capacity,out int count);
        internal IconLedge[] Items=new IconLedge[0];
        private Thread worker;
        private readonly AutoResetEvent wake=new AutoResetEvent(false);
        private IconLedge[] completed;
        private volatile bool stopping;
        private bool pending;
        private double next;
        private static IconLedge[] Read() {
            var items=new IconLedge[128];
            int count;
            int result=WeatherDesktopIcons(items,items.Length,out count);
            if(result==unchecked((int)0x8007007a) && count>0){
                items=new IconLedge[count];result=WeatherDesktopIcons(items,items.Length,out count);
            }
            if(result!=0)return new IconLedge[0];
            Array.Resize(ref items,count);return items;
        }
        private void Work(){
            try {
                while(true){
                    wake.WaitOne();
                    if(stopping)return;
                    IconLedge[] result;
                    try {result=Read();}
                    catch(ExternalException){result=new IconLedge[0];}
                    Interlocked.Exchange(ref completed,result);
                }
            } finally {wake.Dispose();}
        }
        internal bool Update(double now,bool active) {
            bool changed=false;
            var items=Interlocked.Exchange(ref completed,null);
            if(items!=null){
                pending=false;
                changed=items.Length!=Items.Length;
                for(int i=0;!changed && i<items.Length;i++){
                    var a=items[i];var b=Items[i];
                    changed=a.Key!=b.Key || a.Box.Left!=b.Box.Left || a.Box.Top!=b.Box.Top ||
                        a.Box.Right!=b.Box.Right || a.Box.Bottom!=b.Box.Bottom;
                }
                if(changed)Items=items;
            }
            if(active && now>=next && !pending && GpuOverlay.NativeLoaded && !stopping){
                if(worker==null){
                    worker=new Thread(Work){IsBackground=true,Name="Desktop icon positions"};
                    worker.SetApartmentState(ApartmentState.STA);worker.Start();
                }
                next=now+1.5;pending=true;wake.Set();
            }
            return changed;
        }
        public void Dispose(){
            if(stopping)return;
            stopping=true;
            if(worker!=null)wake.Set();else wake.Dispose();
        }
    }
}
