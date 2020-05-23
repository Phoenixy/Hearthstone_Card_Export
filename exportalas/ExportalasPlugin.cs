using exportalas.Controls;
using exportalas.Internal;
using System;
using System.Diagnostics;
using System.Windows.Controls;

namespace exportalas
{
    public class ExportalasPlugin : Hearthstone_Deck_Tracker.Plugins.IPlugin
    {
        public void OnLoad()
        {

            Settings = PluginSettings.LoadSettings(PluginDataDir);

            MainMenuItem = new PluginMenuItem { Header = Name };
            MainMenuItem.Click += (sender, args) =>
            {
                if (MainWindow == null)
                {
                    InitializeMainWindow();
                    Debug.Assert(MainWindow != null, "_mainWindow != null");
                    MainWindow.Show();
                }
                else
                {
                    MainWindow.Activate();
                }
            };
        }

        public void OnUpdate()
        {
            //CheckForUpdates();
        }

        public string Name => "Gyűjtemény exportálás";

        public string Description => @"Hearthstone gyűjtemény exportálása CSV fájlba egy kattintással.";

        public string Author => "Bence Borovi (Thanks to Vasilev Konstantin)";

        public static readonly Version PluginVersion = new Version(1, 1, 0);

        public Version Version => PluginVersion;

        protected MenuItem MainMenuItem { get; set; }

        protected static MainWindow MainWindow;

        protected void InitializeMainWindow()
        {
            if (MainWindow == null)
            {
                MainWindow = new MainWindow
                {
                    Width = Settings.CollectionWindowWidth,
                    Height = Settings.CollectionWindowHeight,
                };
                MainWindow.Closed += (sender, args) =>
                {
                    Settings.CollectionWindowWidth = MainWindow.Width;
                    Settings.CollectionWindowHeight = MainWindow.Height;
                    MainWindow = null;
                };
            }
        }
        public void OnUnload()
        {
            if (MainWindow != null)
            {
                if (MainWindow.IsVisible)
                {
                    MainWindow.Close();
                }
                MainWindow = null;
            }
            Settings.SaveSettings(PluginDataDir);
        }

        public void OnButtonPress()
        {
        }

        public MenuItem MenuItem => MainMenuItem;

        internal static string PluginDataDir => System.IO.Path.Combine(Hearthstone_Deck_Tracker.Config.Instance.DataDir, "CollectionTracker");

        public static PluginSettings Settings { get; set; }

        public string ButtonText => throw new NotImplementedException();
    }
}
