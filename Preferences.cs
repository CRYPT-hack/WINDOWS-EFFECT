using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Media;
using System.Net;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace WindowWeather {
    [DataContract]
    internal sealed class SavedScene {
        [DataMember] public Weather Mode;
        [DataMember] public int Intensity,Wind,Depth;
        [DataMember] public bool Grass,Sound,Visitors;
        internal static SavedScene Capture(Preferences p) {
            return new SavedScene {Mode=p.Mode,Intensity=p.Intensity,Wind=p.Wind,Depth=p.Depth,Grass=p.Grass,Sound=p.Sound,Visitors=p.Visitors};
        }
        internal void Apply(Preferences p) {
            p.Mode=Mode; p.Intensity=Intensity; p.Wind=Wind; p.Depth=Depth; p.Grass=Grass; p.Sound=Sound; p.Visitors=Visitors;
        }
    }
    [DataContract]
    internal sealed class Preferences {
        [DataMember] public Weather Mode = Weather.Rain;
        [DataMember] public int Intensity = 48;
        [DataMember] public int Wind = 16;
        [DataMember] public int Depth = 45;
        [DataMember] public bool Sound, Grass, OverFullscreen, Saver, Paused, Sync;
        [DataMember] public bool Visitors;
        [DataMember] public int BirdsFound;
        [DataMember] public Dictionary<string,SavedScene> Scenes=new Dictionary<string,SavedScene>();
        [DataMember] public string Place = "";
        [DataMember] public double Latitude, Longitude;
        [DataMember] public List<string> Disabled = new List<string>();
        internal static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Window Weather");
        private static string FileName { get { return Path.Combine(Folder,"settings.json"); } }
        internal static Preferences Load() {
            try {
                using(var f=File.OpenRead(FileName)) {
                    var p=(Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(f);
                    if(!Enum.IsDefined(typeof(Weather),p.Mode)) p.Mode=Weather.Rain;
                    p.Intensity=Math.Min(100,Math.Max(1,p.Intensity));
                    p.Depth=Math.Min(100,Math.Max(0,p.Depth)); p.Wind=Math.Min(100,Math.Max(-100,p.Wind));
                    if(p.Scenes==null) p.Scenes=new Dictionary<string,SavedScene>();
                    if(p.Disabled==null) p.Disabled=new List<string>();
                    if(String.IsNullOrWhiteSpace(p.Place)) p.Sync=false;
                    return p;
                }
            } catch(IOException) {} catch(UnauthorizedAccessException) {} catch(SerializationException) {}
            return new Preferences();
        }
        internal void Save() {
            Directory.CreateDirectory(Folder);
            string tmp=FileName+".new";
            using(var f=File.Create(tmp)) new DataContractJsonSerializer(typeof(Preferences)).WriteObject(f,this);
            if(File.Exists(FileName)) File.Replace(tmp,FileName,null); else File.Move(tmp,FileName);
        }
    }

    internal sealed class RainAudio : IDisposable {
        private SoundPlayer player;
        private MemoryStream wave;
        private bool playing;
        internal void Set(bool on) {
            if(on==playing) return;
            if(on) {
                if(player==null) { wave=MakeLoop(); player=new SoundPlayer(wave); player.Load(); }
                player.PlayLooping();
            } else if(player!=null) player.Stop();
            playing=on;
        }
        private static MemoryStream MakeLoop() {
            const int rate=22050, samples=rate*8;
            var stream=new MemoryStream();
            var writer=new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36+samples*2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
            writer.Write((short)1); writer.Write(rate); writer.Write(rate*2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples*2);
            var rng=new Random(391); double low=0,mid=0;
            for(int i=0;i<samples;i++) {
                double noise=rng.NextDouble()*2-1;
                low=.979*low+.021*noise; mid=.67*mid+.33*noise;
                double swell=.80+.12*Math.Sin(2*Math.PI*i/samples)+.08*Math.Cos(6*Math.PI*i/samples);
                double ramp=Math.Min(1,Math.Min(i,samples-1-i)/180.0);
                writer.Write((short)((low*13000+mid*3200+noise*850)*swell*ramp));
            }
            stream.Position=0; return stream;
        }
        public void Dispose() { if(player!=null){player.Stop();player.Dispose();} if(wave!=null) wave.Dispose(); }
    }

    internal sealed class Conditions {
        internal string Place;
        internal double Lat,Lon;
        internal Weather Kind;
        internal int Intensity,Wind;
    }
    internal static class WeatherService {
        private static Dictionary<string,object> Read(string url) {
            var request=(HttpWebRequest)WebRequest.Create(url);
            request.Timeout=12000; request.ReadWriteTimeout=12000; request.UserAgent="WindowWeather/1.0";
            using(var response=request.GetResponse()) using(var reader=new StreamReader(response.GetResponseStream()))
                return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
        }
        internal static Conditions Lookup(string city, string place, double lat, double lon) {
            if(city!=null) {
                var geo=Read("https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&name="+Uri.EscapeDataString(city));
                object rows;
                if(!geo.TryGetValue("results",out rows) || ((System.Collections.IList)rows).Count==0) throw new InvalidOperationException("City not found. Try adding the country.");
                var first=(Dictionary<string,object>)((System.Collections.IList)rows)[0];
                lat=Convert.ToDouble(first["latitude"]); lon=Convert.ToDouble(first["longitude"]);
                place=Convert.ToString(first["name"])+(first.ContainsKey("country") ? ", "+first["country"] : "");
            }
            string url=String.Format(CultureInfo.InvariantCulture,"https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=weather_code,wind_speed_10m,wind_direction_10m,is_day,precipitation",lat,lon);
            var row=(Dictionary<string,object>)Read(url)["current"];
            int code=Convert.ToInt32(row["weather_code"]);
            Weather kind=Weather.Rain;
            if(code<=2) kind=Convert.ToInt32(row["is_day"])==1 ? Weather.Sunlight : Weather.Fog;
            else if(code==3 || code==45 || code==48) kind=Weather.Fog;
            else if((code>=71 && code<=77) || code==85 || code==86) kind=Weather.Snow;
            else if(code==56 || code==57 || code==66 || code==67) kind=Weather.Sleet;
            double speed=Convert.ToDouble(row["wind_speed_10m"]), direction=Convert.ToDouble(row["wind_direction_10m"]);
            return new Conditions {Place=place,Lat=lat,Lon=lon,Kind=kind,
                Intensity=Math.Min(100,25+(int)(Convert.ToDouble(row["precipitation"])*11)+(code>=95 ? 32 : 0)),
                Wind=(int)Math.Max(-100,Math.Min(100,-2*speed*Math.Sin(direction*Math.PI/180)))};
        }
    }
}