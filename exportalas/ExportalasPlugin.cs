using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using exportalas.Controls;
using exportalas.Internal;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Utility.Logging;
// A HDT 1.60-ban is van PluginSettings osztály, ezért kell a pontos hivatkozás.
using PluginSettings = exportalas.Internal.PluginSettings;

namespace exportalas
{
    public class ExportalasPlugin : IPlugin
    {
        /// <summary>A verzió az AssemblyInfo.cs-ből jön; a GitHub Release tagnek ezzel kell egyeznie (pl. v2.0.0).</summary>
        public static readonly Version PluginVersion = GetPluginVersion();

        /// <summary>Meccsek után legfeljebb ilyen gyakran olvastatjuk újra a gyűjteményt (a HDT magától csak a gyűjteménykezelő és a csomagbontás után teszi).</summary>
        private static readonly TimeSpan MenuRefreshInterval = TimeSpan.FromMinutes(20);

        private static int _loadGeneration;

        private PluginSettings _settings;
        private CollectionSync _sync;
        private PluginUpdater _updater;
        private PluginMenuItem _menuItem;
        private MainWindow _window;
        private bool _loaded;
        private DateTime _nextUpdateCheckUtc;
        private DateTime _lastMenuRefreshUtc = DateTime.MinValue;

        // A HDT a Name + fájlnév alapján jegyzi meg, hogy a plugin be van-e kapcsolva. Ne nevezd át!
        public string Name => "Gyűjtemény exportálás";

        public string Description =>
            "Feltölti a Hearthstone gyűjteményedet a Hearthstone Hungary profilodba, vagy CSV fájlba exportálja. " +
            "Új verzió esetén magától frissül.";

        public string ButtonText => "Megnyitás";

        public string Author => "Bence Borovi (PHOENIX) – Hearthstone Hungary";

        public Version Version => PluginVersion;

        public MenuItem MenuItem => _menuItem;

        public void OnLoad()
        {
            _settings = PluginSettings.Load();
            _sync = new CollectionSync(_settings);
            _updater = new PluginUpdater(_settings);
            _updater.StatusChanged += OnUpdaterStatusChanged;

            _menuItem = new PluginMenuItem { Header = Name };
            _menuItem.Click += (sender, args) => ShowWindow();

            // Ezt közvetlenül itt, az OnLoad-ban kell hívni: a HDT a hívó osztályból állapítja meg, melyik pluginhoz
            // tartozik az esemény, és csak így veszi le, ha a plugint kikapcsolják.
            // A generáció azért kell, hogy ki-be kapcsolás után se fusson le kétszer.
            var generation = Interlocked.Increment(ref _loadGeneration);
            GameEvents.OnInMenu.Add(() =>
            {
                if (generation == Volatile.Read(ref _loadGeneration))
                    OnInMenu();
            });
            CollectionHelpers.Hearthstone.OnCollectionChanged += OnCollectionChanged;

            // Ne lassítsuk a HDT indulását: az első frissítés-ellenőrzés kicsit később fut.
            _nextUpdateCheckUtc = DateTime.UtcNow.AddSeconds(20);
            _loaded = true;
            Log.Info($"[HSHU] Gyűjtemény plugin v{PluginVersion} betöltve.");
        }

        public void OnUnload()
        {
            _loaded = false;
            Interlocked.Increment(ref _loadGeneration);
            CollectionHelpers.Hearthstone.OnCollectionChanged -= OnCollectionChanged;
            if (_updater != null)
                _updater.StatusChanged -= OnUpdaterStatusChanged;
            if (_window != null)
            {
                _window.Close();
                _window = null;
            }
            _settings?.Save();
        }

        public void OnButtonPress() => ShowWindow();

        /// <summary>A HDT kb. 100 ms-onként hívja, ezért itt csak olcsó ellenőrzés lehet.</summary>
        public void OnUpdate()
        {
            if (!_loaded || !_settings.AutoUpdate)
                return;
            var now = DateTime.UtcNow;
            if (now < _nextUpdateCheckUtc)
                return;
            _nextUpdateCheckUtc = now.AddMinutes(30);
            if (_updater.IsCheckDue)
                RunSafe(() => _updater.CheckAndInstallAsync(manual: false));
        }

        private void OnInMenu()
        {
            if (!_loaded || !_settings.AutoUpload || !_sync.IsLinked)
                return;
            var now = DateTime.UtcNow;
            if (now - _lastMenuRefreshUtc > MenuRefreshInterval)
            {
                _lastMenuRefreshUtc = now;
                // Ha változott, a HDT OnCollectionChanged eseménye indítja a feltöltést.
                RunSafe(async () =>
                {
                    await CollectionHelpers.Hearthstone.UpdateCollection();
                    await _sync.SyncAsync(SyncTrigger.Automatic);
                });
            }
            else
            {
                RunSafe(() => _sync.SyncAsync(SyncTrigger.Automatic));
            }
        }

        private void OnCollectionChanged()
        {
            if (!_loaded)
                return;
            RunSafe(() => _sync.SyncAsync(SyncTrigger.Automatic));
        }

        private void OnUpdaterStatusChanged()
        {
            if (_updater.State != UpdateState.Installed)
                return;
            RunOnUi(() =>
            {
                if (_menuItem != null)
                    _menuItem.Header = Name + " – frissítve, indítsd újra a HDT-t";
            });
        }

        private void ShowWindow()
        {
            if (!_loaded)
                return;
            if (_window == null)
            {
                _window = new MainWindow(_settings, _sync, _updater);
                _window.Closed += (sender, args) => _window = null;
                _window.Show();
            }
            else
            {
                if (_window.WindowState == WindowState.Minimized)
                    _window.WindowState = WindowState.Normal;
                _window.Activate();
            }
        }

        /// <summary>A HDT-ben egy elkapatlan kivétel az egész programot leállíthatja, ezért minden háttérmunka ezen megy át.</summary>
        private static async void RunSafe(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Log.Error("[HSHU] " + ex);
            }
        }

        private static void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.BeginInvoke(action);
        }

        private static Version GetPluginVersion()
        {
            var v = typeof(ExportalasPlugin).Assembly.GetName().Version;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }
}
