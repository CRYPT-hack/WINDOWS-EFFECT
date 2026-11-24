using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace WindowWeather {
    // Schedule absolute frame deadlines on a one-shot timer. Only one tick can be outstanding.
    internal sealed class FrameClock : IDisposable {
        [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)]
        private static extern SafeWaitHandle CreateWaitableTimerEx(IntPtr attributes,string name,uint flags,uint access);
        [DllImport("kernel32")] private static extern bool SetWaitableTimer(SafeWaitHandle timer,ref long due,int period,IntPtr routine,IntPtr arg,bool resume);
        private readonly Dispatcher ui=Dispatcher.CurrentDispatcher;
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private readonly Action tick;
        private readonly EventWaitHandle handle;
        private readonly RegisteredWaitHandle registration;
        private readonly DispatcherTimer fallback;
        private double period=.033,deadline;
        private bool disposed;
        internal FrameClock(Action action){
            tick=action;
            fallback=new DispatcherTimer(DispatcherPriority.Render);
            fallback.Tick+=delegate {fallback.Stop();Fire();};
            SafeWaitHandle native=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x00100002);
            if(native.IsInvalid){native.Dispose();return;}
            handle=new EventWaitHandle(false,EventResetMode.AutoReset);
            handle.SafeWaitHandle=native;
            registration=ThreadPool.RegisterWaitForSingleObject(handle,delegate {
                if(!ui.HasShutdownStarted)ui.BeginInvoke(DispatcherPriority.Render,(Action)Fire);
            },null,Timeout.Infinite,false);
        }
        internal void SetPeriod(double seconds){period=seconds;}
        internal void Start(){deadline=clock.Elapsed.TotalSeconds;Arm();}
        private void Fire(){
            if(disposed)return;
            tick();
            if(!disposed)Arm();
        }
        private void Arm(){
            double now=clock.Elapsed.TotalSeconds;
            deadline+=period;
            if(deadline<=now) deadline+=period*(1+Math.Floor((now-deadline)/period));
            double delay=deadline-now;
            long due=-(long)Math.Max(1,delay*10000000);
            if(handle!=null && SetWaitableTimer(handle.SafeWaitHandle,ref due,0,IntPtr.Zero,IntPtr.Zero,false))return;
            fallback.Interval=TimeSpan.FromSeconds(delay);fallback.Start();
        }
        public void Dispose(){
            disposed=true;fallback.Stop();
            if(registration!=null)registration.Unregister(null);
            if(handle!=null)handle.Dispose();
        }
    }
}
