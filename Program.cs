using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
[assembly:AssemblyTitle("Window Weather")]
[assembly:AssemblyDescription("Ambient weather for the Windows desktop")]
[assembly:AssemblyProduct("Window Weather")]
[assembly:AssemblyVersion("1.1.0.0")]

namespace WindowWeather {
    internal static class Program {
        [STAThread] static void Main() {
            bool first;
            using(var mutex=new Mutex(true,@"Local\WindowWeather.Desktop.v1",out first)) {
                if(!first) {
                    try { using(var e=EventWaitHandle.OpenExisting(@"Local\WindowWeather.Controls.v1")) e.Set(); }
                    catch(WaitHandleCannotBeOpenedException) { }
                    return;
                }
                var application=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
                using(var controller=new Controller()) {
                    application.SessionEnding+=delegate { controller.Flush(); };
                    application.Run();
                }
                mutex.ReleaseMutex();
            }
        }
    }

    internal sealed class Controller : IDisposable {
        internal readonly Preferences Prefs=Preferences.Load();
        private readonly List<Overlay> overlays=new List<Overlay>();
        private readonly Dispatcher ui=Dispatcher.CurrentDispatcher;
        private readonly FrameClock frame;
        private readonly WindowChanges changes;
        private int refreshRate=60;
        private bool onBattery;
        private double lastScan;
        private readonly DispatcherTimer save=new DispatcherTimer();
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private readonly RainAudio audio=new RainAudio();
        private readonly HwndSource messages;
        private readonly EventWaitHandle showEvent;
        private readonly RegisteredWaitHandle showWait;
        private readonly Forms.NotifyIcon tray;
        private readonly Icon cloud;
        private readonly Forms.ContextMenuStrip menu=new Forms.ContextMenuStrip();
        private readonly Forms.ToolStripMenuItem pauseItem;
        private readonly List<Forms.ToolStripMenuItem> modes=new List<Forms.ToolStripMenuItem>();
        private Panel panel;
        private double lastFrame, nextScan;
        private bool locked, suspended;
        private int requestId;
        private DateTime nextSync=DateTime.UtcNow;
        internal bool Quitting, Hotkey, Busy;
        internal string Report;
        internal Controller() {
            var hp=new HwndSourceParameters("Window Weather shortcuts") {WindowStyle=0,Width=0,Height=0,ParentWindow=new IntPtr(-3)};
            messages=new HwndSource(hp); messages.AddHook(Message);
            Hotkey=Desktop.RegisterHotKey(messages.Handle,1,0x4003,0x57);
            showEvent=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\WindowWeather.Controls.v1");
            showWait=ThreadPool.RegisterWaitForSingleObject(showEvent,delegate { ui.BeginInvoke((Action)Open); },null,-1,false);
            cloud=Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            tray=new Forms.NotifyIcon {Icon=cloud,Text="Window Weather",ContextMenuStrip=menu};
            menu.Items.Add("Open Window Weather",null,delegate { Open(); });
            pauseItem=(Forms.ToolStripMenuItem)menu.Items.Add("Pause",null,delegate { Toggle(); });
            var scenes=new Forms.ToolStripMenuItem("Weather");
            foreach(Weather w in Enum.GetValues(typeof(Weather))) {
                Weather chosen=w;
                modes.Add((Forms.ToolStripMenuItem)scenes.DropDownItems.Add(w.ToString(),null,delegate { Manual(); Prefs.Mode=chosen; Changed(); }));
            }
            menu.Items.Add(scenes); menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Quit",null,delegate { Quit(); });
            tray.MouseClick+=delegate(object sender,Forms.MouseEventArgs e) { if(e.Button==Forms.MouseButtons.Left) Open(); };
            tray.Visible=true;
            save.Interval=TimeSpan.FromMilliseconds(450); save.Tick+=delegate {save.Stop(); Flush();};
            frame=new FrameClock(Tick);
            changes=new WindowChanges();
            ReadPower();
            SystemEvents.DisplaySettingsChanged+=OnDisplays;
            SystemEvents.SessionSwitch+=OnSession;
            SystemEvents.PowerModeChanged+=OnPower;
            Rebuild(); RefreshControls(); frame.Start();
        }
        private void Rebuild() {
            if(Quitting) return;
            foreach(var o in overlays) o.Close(); overlays.Clear();
            foreach(var screen in Forms.Screen.AllScreens) {
                var overlay=new Overlay(screen,Prefs);
                overlay.View.Birds.Found+=delegate { Prefs.BirdsFound++; RefreshControls(); save.Stop(); save.Start(); };
                overlays.Add(overlay);
            }
            refreshRate=WindowChanges.RefreshRate(Forms.Screen.PrimaryScreen.DeviceName);
            if(panel!=null) panel.DisplayList();
            nextScan=0; Scan();
        }
        private void OnDisplays(object sender,EventArgs e) { ui.BeginInvoke((Action)Rebuild); }
        private void OnSession(object sender,SessionSwitchEventArgs e) {
            if(e.Reason!=SessionSwitchReason.SessionLock && e.Reason!=SessionSwitchReason.SessionUnlock) return;
            ui.BeginInvoke((Action)delegate {locked=e.Reason==SessionSwitchReason.SessionLock; nextScan=0;});
        }
        private void OnPower(object sender,PowerModeChangedEventArgs e) {
            if(e.Mode==PowerModes.StatusChange) {ui.BeginInvoke((Action)delegate {ReadPower();nextScan=0;});return;}
            ui.BeginInvoke((Action)delegate {suspended=e.Mode==PowerModes.Suspend; nextScan=0;});
        }
        private void ReadPower(){
            onBattery=Forms.SystemInformation.PowerStatus.PowerLineStatus==Forms.PowerLineStatus.Offline;
        }
        private IntPtr Message(IntPtr h,int m,IntPtr w,IntPtr l,ref bool handled) {
            if(m==0x0312 && w.ToInt32()==1) { Toggle(); handled=true; } return IntPtr.Zero;
        }
        private void Open() { if(Quitting) return; if(panel==null) panel=new Panel(this); panel.Show(); }
        internal void Changed() {
            RefreshControls(); save.Stop(); save.Start(); Scan();
        }
        private void RefreshControls() {
            tray.Text="Window Weather · "+(Prefs.Paused ? "Paused" : Prefs.Mode.ToString());
            pauseItem.Text=Prefs.Paused ? "Resume weather" : "Pause weather";
            for(int i=0;i<modes.Count;i++) modes[i].Checked=i==(int)Prefs.Mode;
            if(panel!=null) panel.Refresh();
        }
        internal void Toggle() { Prefs.Paused=!Prefs.Paused; Changed(); }
        internal void Manual() { requestId++; Busy=false; Prefs.Sync=false; Report=null; }
        internal async void Synchronize(string city) {
            int id=++requestId; Busy=true; Report="Checking weather…"; RefreshControls();
            string place=Prefs.Place; double lat=Prefs.Latitude,lon=Prefs.Longitude;
            nextSync=DateTime.UtcNow.AddMinutes(30);
            try {
                var c=await Task.Run(delegate {return WeatherService.Lookup(city,place,lat,lon);});
                if(Quitting || id!=requestId) return;
                Prefs.Place=c.Place; Prefs.Latitude=c.Lat; Prefs.Longitude=c.Lon; Prefs.Mode=c.Kind;
                Prefs.Intensity=c.Intensity; Prefs.Wind=c.Wind; Prefs.Sync=true;
                Report=c.Place+" · Updated "+DateTime.Now.ToShortTimeString();
            } catch(Exception ex) {
                if(Quitting || id!=requestId) return;
                Report=ex is InvalidOperationException && !(ex is System.Net.WebException) ? ex.Message : "Weather service unavailable. Your current scene is still active.";
                nextSync=DateTime.UtcNow.AddMinutes(5);
            }
            Busy=false; Changed();
        }
        internal void Flush() {
            try {Prefs.Save();} catch(IOException) {Report="Could not save preferences to disk.";} catch(UnauthorizedAccessException) {Report="Windows denied access to save preferences.";}
        }
        private void Scan() {
            bool idle=Prefs.Paused || locked || suspended;
            bool enabled=overlays.Exists(delegate(Overlay o){return !Prefs.Disabled.Contains(o.Monitor.DeviceName);});
            var windows=idle || !enabled ? null : Desktop.ReadWindows();
            IntPtr foreground=Desktop.GetForegroundWindow();
            bool running=false, fallback=false;
            foreach(var o in overlays) {
                if(windows!=null && !Prefs.Disabled.Contains(o.Monitor.DeviceName)) o.Refresh(windows,foreground);
                bool sleep=idle || Prefs.Disabled.Contains(o.Monitor.DeviceName) || (!Prefs.OverFullscreen && o.Fullscreen);
                if(sleep!=o.Sleeping) {
                    o.Sleeping=sleep;
                    if(sleep) o.Hide(); else {o.Show();o.Place();}
                }
                if(!sleep){running=true;fallback|=!o.Accelerated;}
            }
            audio.Set(running && Prefs.Sound && (Prefs.Mode==Weather.Rain || Prefs.Mode==Weather.Sleet));
            int cap=Prefs.Saver ? 24 : (onBattery || fallback ? 30 : 60);
            // An integer display-refresh divisor prevents uneven cadence, e.g. 48 fps at 144 Hz.
            double fps=refreshRate/Math.Ceiling((double)refreshRate/cap);
            frame.SetPeriod(running ? 1/fps : .25);
        }
        private void Tick() {
            double now=clock.Elapsed.TotalSeconds;
            double dt=Math.Min(.09,Math.Max(0,now-lastFrame)); lastFrame=now;
            if(now>=nextScan || (changes.Dirty && now-lastScan>=.014)) {
                changes.Dirty=false; Scan(); lastScan=now;nextScan=now+2;
            }
            foreach(var o in overlays) if(!o.Sleeping) o.Advance(dt);
            if(Prefs.Sync && !Busy && !locked && !suspended && DateTime.UtcNow>=nextSync) Synchronize(null);
        }
        internal void Quit() { Quitting=true; Application.Current.Shutdown(); }
        public void Dispose() {
            Quitting=true; frame.Dispose(); changes.Dispose(); save.Stop(); Flush();
            SystemEvents.DisplaySettingsChanged-=OnDisplays; SystemEvents.SessionSwitch-=OnSession; SystemEvents.PowerModeChanged-=OnPower;
            showWait.Unregister(null); showEvent.Dispose(); Desktop.UnregisterHotKey(messages.Handle,1); messages.Dispose();
            audio.Dispose(); tray.Visible=false; tray.Dispose(); menu.Dispose(); cloud.Dispose();
        }
    }
}