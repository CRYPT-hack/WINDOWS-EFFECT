using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace WindowWeather {
    internal sealed class GpuOverlay : IDisposable {
        private const string Library="WindowWeather.Gpu.dll";
        [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)]
        private static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [DllImport(Library)] private static extern int WeatherCreate(int x,int y,int width,int height,out IntPtr state,out IntPtr handle);
        [DllImport(Library)] private static extern int WeatherWindows(IntPtr state,[In] float[] bounds,int count);
        [DllImport(Library)] private static extern int WeatherFrame(IntPtr state,[In] Mark[] marks,int count,float width,float height,float scale);
        [DllImport(Library)] private static extern void WeatherDestroy(IntPtr state);
        [DllImport("user32")] internal static extern bool ShowWindow(IntPtr handle,int command);
        [DllImport("user32")] private static extern uint GetDpiForWindow(IntPtr window);
        private static IntPtr library;
        internal static bool NativeLoaded {get{return library!=IntPtr.Zero;}}
        private IntPtr state;
        internal readonly IntPtr Handle;
        internal readonly double Scale;
        private int uploadedRevision=-1;
        private float[] bounds=new float[32];
        private readonly BatchCanvas canvas=new BatchCanvas();
        private GpuOverlay(IntPtr context,IntPtr hwnd) {
            state=context;Handle=hwnd;Scale=Math.Max(1,GetDpiForWindow(hwnd)/96.0);
        }
        private static string Digest(byte[] bytes) {
            using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        }
        private static bool Load() {
            if(library!=IntPtr.Zero)return true;
            using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream(Library)) {
                if(resource==null || IntPtr.Size!=8)return false;
                using(var memory=new MemoryStream()) {
                    resource.CopyTo(memory);
                    byte[] bytes=memory.ToArray();
                    string digest=Digest(bytes);
                    string folder=Path.Combine(Preferences.Folder,"Renderer",digest);
                    Directory.CreateDirectory(folder);
                    string file=Path.Combine(folder,Library);
                    if(!File.Exists(file) || Digest(File.ReadAllBytes(file))!=digest) {
                        string temp=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");
                        try {
                            File.WriteAllBytes(temp,bytes);
                            if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
                        } finally {if(File.Exists(temp))File.Delete(temp);}
                    }
                    library=LoadLibraryEx(file,IntPtr.Zero,0x800); // Dependencies come from System32.
                    return library!=IntPtr.Zero;
                }
            }
        }
        internal static GpuOverlay TryCreate(Rectangle r) {
            IntPtr ctx=IntPtr.Zero;
            try {
                if(!Load())return null;
                IntPtr hwnd;
                if(WeatherCreate(r.X,r.Y,r.Width,r.Height,out ctx,out hwnd)<0)return null;
                return new GpuOverlay(ctx,hwnd);
            } catch(IOException) {} catch(UnauthorizedAccessException) {} catch(EntryPointNotFoundException) {}
              catch(DllNotFoundException) {} catch(BadImageFormatException) {}
            if(ctx!=IntPtr.Zero)WeatherDestroy(ctx);
            return null;
        }
        internal int Present(Scene view) {
            if(view.Revision!=uploadedRevision) {
                List<Surface> windows=view.Surfaces;
                if(bounds.Length<windows.Count*4)Array.Resize(ref bounds,windows.Count*4);
                for(int i=0;i<windows.Count;i++){
                    var b=windows[i].Box;int at=i*4;
                    bounds[at]=(float)b.Left;bounds[at+1]=(float)b.Top;
                    bounds[at+2]=(float)b.Right;bounds[at+3]=(float)b.Bottom;
                }
                int result=WeatherWindows(state,bounds,windows.Count);
                if(result<0)return result;
                uploadedRevision=view.Revision;
            }
            canvas.Reset();view.Draw(canvas);
            return WeatherFrame(state,canvas.Marks,canvas.Count,(float)view.ActualWidth,(float)view.ActualHeight,(float)Scale);
        }
        public void Dispose(){if(state!=IntPtr.Zero){WeatherDestroy(state);state=IntPtr.Zero;}}
    }
}
