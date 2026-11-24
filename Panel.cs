using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace WindowWeather {
    internal sealed class Panel {
        private readonly Controller app;
        internal readonly Window Window;
        private readonly Dictionary<Weather,Button> choices=new Dictionary<Weather,Button>();
        private bool updating;
        private readonly Brush active=new SolidColorBrush(Color.FromRgb(42,86,75));
        private readonly Brush inactive=new SolidColorBrush(Color.FromRgb(229,236,229));
        private T Get<T>(string id) where T:FrameworkElement { return (T)Window.FindName(id); }
        internal Panel(Controller controller) {
            app=controller;
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Controls.xaml"))
                Window=(Window)XamlReader.Load(stream);
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Weather.ico")) {
                var decoder=new IconBitmapDecoder(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
                Window.Icon=decoder.Frames[decoder.Frames.Count-1];
            }
            Window.MaxHeight=SystemParameters.WorkArea.Height;
            Window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) {
                if(!app.Quitting) { e.Cancel=true; Window.Hide(); }
            };
            var glyphs=new string[]{"☂","❄","☔","❧","☀","≋"};
            foreach(Weather item in Enum.GetValues(typeof(Weather))) {
                Weather scene=item;
                var label=new TextBlock {Text=glyphs[(int)item]+"  "+item,FontSize=14,TextAlignment=TextAlignment.Center};
                var button=new Button { Content=label,Margin=new Thickness(3),MinHeight=42,Padding=new Thickness(4,5,4,5) };
                System.Windows.Automation.AutomationProperties.SetName(button,item.ToString());
                button.Click+=delegate { app.Manual(); app.Prefs.Mode=scene; app.Changed(); };
                Get<System.Windows.Controls.Primitives.UniformGrid>("Choices").Children.Add(button);
                choices[item]=button;
            }
            Slider("Level",delegate(int n){app.Manual();app.Prefs.Intensity=n;});
            Slider("Breeze",delegate(int n){app.Manual();app.Prefs.Wind=n;});
            Slider("Distance",delegate(int n){app.Prefs.Depth=n;});
            Check("Birds",delegate(bool b){app.Prefs.Visitors=b;});
            var names=Get<ComboBox>("SceneName");
            foreach(string key in app.Prefs.Scenes.Keys) names.Items.Add(key);
            Get<Button>("SaveScene").Click+=delegate {
                string name=names.Text.Trim();
                if(name.Length==0 || name.Length>60) {Get<TextBlock>("SceneNotice").Text="Enter a name between 1 and 60 characters.";return;}
                if(!app.Prefs.Scenes.ContainsKey(name)) names.Items.Add(name);
                app.Prefs.Scenes[name]=SavedScene.Capture(app.Prefs);
                Get<TextBlock>("SceneNotice").Text="Saved “"+name+"”."; app.Changed();
            };
            Get<Button>("LoadScene").Click+=delegate {
                SavedScene scene;
                if(!app.Prefs.Scenes.TryGetValue(names.Text.Trim(),out scene)){Get<TextBlock>("SceneNotice").Text="Select a saved scene first.";return;}
                app.Manual(); scene.Apply(app.Prefs); app.Changed(); Page(false);
            };
            Check("Grass",delegate(bool b){app.Prefs.Grass=b;});
            Check("Sound",delegate(bool b){app.Prefs.Sound=b;});
            Check("Fullscreen",delegate(bool b){app.Prefs.OverFullscreen=b;});
            Check("Saver",delegate(bool b){app.Prefs.Saver=b;});
            Get<Button>("Pause").Click+=delegate { app.Toggle(); };
            Get<Button>("Exit").Click+=delegate { app.Quit(); };
            Get<Button>("ShowScene").Click+=delegate { Page(false); };
            Get<Button>("ShowOptions").Click+=delegate { DisplayList(); Page(true); };
            Get<Button>("Sync").Click+=delegate { string city=Get<TextBox>("City").Text.Trim(); if(city.Length>1) app.Synchronize(city); else { app.Report="Enter a city name first."; Refresh(); } };
            Get<Button>("Manual").Click+=delegate {app.Manual(); app.Changed();};
            Page(false);
            Refresh();
        }
        private void Style(Button b,bool on) { b.Background=on ? active : inactive; b.Foreground=on ? Brushes.White : Brushes.Black; }
        private void Page(bool options) {
            Get<StackPanel>("ScenePage").Visibility=options ? Visibility.Collapsed : Visibility.Visible;
            Get<StackPanel>("OptionsPage").Visibility=options ? Visibility.Visible : Visibility.Collapsed;
            Style(Get<Button>("ShowScene"),!options); Style(Get<Button>("ShowOptions"),options);
        }
        private void Slider(string name,Action<int> change) {
            var s=Get<Slider>(name); System.Windows.Automation.AutomationProperties.SetName(s,name=="Distance" ? "Weather behind windows" : name=="Breeze" ? "Wind" : "Intensity");
            s.ValueChanged+=delegate {if(!updating) { change((int)Math.Round(s.Value)); app.Changed(); }};
        }
        private void Check(string name,Action<bool> change) {
            var c=Get<CheckBox>(name); c.Click+=delegate {change(c.IsChecked==true); app.Changed();};
        }
        internal void DisplayList() {
            var list=Get<StackPanel>("Displays"); list.Children.Clear();
            int i=0;
            foreach(var screen in Forms.Screen.AllScreens) {
                var id=screen.DeviceName; var b=screen.Bounds;
                var c=new CheckBox {Content="Display "+(++i)+"  ·  "+b.Width+" × "+b.Height+(screen.Primary ? "  ·  Primary" : ""),
                    IsChecked=!app.Prefs.Disabled.Contains(id)};
                c.Click+=delegate { if(c.IsChecked==true) app.Prefs.Disabled.Remove(id); else if(!app.Prefs.Disabled.Contains(id)) app.Prefs.Disabled.Add(id); app.Changed(); };
                list.Children.Add(c);
            }
        }
        internal void Show() { Refresh(); DisplayList(); Window.Show(); if(Window.WindowState==WindowState.Minimized) Window.WindowState=WindowState.Normal; Window.Activate(); }
        internal void Refresh() {
            updating=true;
            try {
                var p=app.Prefs;
                foreach(var pair in choices) Style(pair.Value,pair.Key==p.Mode);
                Get<Button>("Pause").Content=p.Paused ? "Resume" : "Pause";
                Get<TextBlock>("Status").Text=(p.Paused ? "Paused" : "Weather on")+"   ·   "+p.Mode+(p.Sync ? "   ·   "+p.Place : "");
                Get<TextBlock>("Hint").Text=app.Hotkey ? "Ctrl + Alt + W   ·   Pause or resume" : "Right-click the tray cloud for controls";
                Get<Slider>("Level").Value=p.Intensity; Get<TextBlock>("LevelValue").Text=p.Intensity+"%";
                Get<Slider>("Breeze").Value=p.Wind; Get<TextBlock>("WindValue").Text=p.Wind==0 ? "Still" : (p.Wind<0 ? "← " : "→ ")+Math.Abs(p.Wind)+"%";
                Get<Slider>("Distance").Value=p.Depth; Get<TextBlock>("DepthValue").Text=p.Depth+"%";
                Get<CheckBox>("Birds").IsChecked=p.Visitors;
                Get<TextBlock>("BirdCount").Text="Hover over a bird to greet it. Birds greeted: "+p.BirdsFound;
                Get<CheckBox>("Grass").IsChecked=p.Grass; Get<CheckBox>("Sound").IsChecked=p.Sound;
                Get<CheckBox>("Saver").IsChecked=p.Saver; Get<CheckBox>("Fullscreen").IsChecked=p.OverFullscreen;
                Get<TextBlock>("Report").Text=app.Report ?? (p.Sync ? "Following "+p.Place : "Manual weather");
                Get<Button>("Manual").Visibility=p.Sync || app.Busy ? Visibility.Visible : Visibility.Collapsed;
                Get<Button>("Sync").IsEnabled=!app.Busy;
            } finally { updating=false; }
        }
    }
}