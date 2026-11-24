using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using HearthstoneHungary.Core;
using Forms = System.Windows.Forms;

namespace HearthstoneHungary
{
    public partial class App
    {
        private const string MutexName = @"Local\HearthstoneHungary.App";
        private const string ShowEventName = @"Local\HearthstoneHungary.Show";

        /// <summary>Ilyen gyakran nézzük, fut-e a Hearthstone.</summary>
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(20);

        /// <summary>Futó játéknál ilyen gyakran olvassuk ki a gyűjteményt (és töltjük fel, ha változott).</summary>
        private static readonly TimeSpan AutoSyncInterval = TimeSpan.FromMinutes(2);

        /// <summary>Ilyen gyakran nézzük meg a háttérben, hogy nem választották-e le az eszközt a profilban.</summary>
        private static readonly TimeSpan LinkCheckInterval = TimeSpan.FromHours(1);

        /// <summary>Az ablak megnyitásakor legfeljebb ilyen gyakran ellenőrizzük az összekötést.</summary>
        private static readonly TimeSpan LinkCheckOnShowInterval = TimeSpan.FromMinutes(1);

        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        private RegisteredWaitHandle _showWait;
        private Forms.NotifyIcon _trayIcon;
        private DispatcherTimer _timer;
        private MainWindow _window;
        private bool _tickRunning;
        private bool _exiting;
        private bool _updateNotified;
        private string _balloonUrl;
        private DateTime _lastAutoSyncUtc = DateTime.MinValue;
        private GameState _lastGameState = GameState.NotRunning;

        /// <summary>Az alkalmazás verziója (3 tagú), az AssemblyInfo.cs-ből.</summary>
        public static Version Version { get; } = GetVersion();

        internal AppSettings Settings { get; private set; }
        internal GameReader Reader { get; private set; }
        internal CollectionSync Sync { get; private set; }
        internal AppUpdater Updater { get; private set; }
        internal NewsWatcher News { get; private set; }
        internal DeckTracker Decks { get; private set; }

        /// <summary>A Hearthstone állapota (a UI szálon frissül).</summary>
        internal GameState GameState => _lastGameState;

        internal event Action GameStateChanged;

        internal static new App Current => (App)Application.Current;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Egyszerre csak egy példány fut; a második csak előhozza az elsőt.
            _mutex = new Mutex(true, MutexName, out var createdNew);
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            if (!createdNew)
            {
                _showEvent.Set();
                _mutex.Dispose();
                _mutex = null;
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (sender, args) =>
            {
                Log.Error("Kezeletlen hiba: " + args.Exception);
                args.Handled = true;
            };

            Log.Info($"Indulás: v{Version}, {(Environment.Is64BitProcess ? "64" : "32")} bites folyamat.");
            AppUpdater.CleanupOldFiles();

            Settings = AppSettings.Load();
            ThemeManager.Initialize();
            WindowsStartup.Apply(Settings.StartWithWindows);
            Reader = new GameReader();
            Sync = new CollectionSync(Settings, Reader);
            Updater = new AppUpdater(Settings);
            News = new NewsWatcher(Settings);
            Decks = new DeckTracker(Settings);
            // A paklikhoz (és később a meccsek eredményéhez) a játék naplója kell; a játék az új beállítást a következő indításakor veszi át.
            HearthstoneLogs.EnsureLoggingEnabled();
            News.NewsFound += (item, more) => Dispatcher.BeginInvoke(new Action(() => ShowBalloon(
                more > 0 ? $"Új hírek a Hearthstone Hungary-n (+{more} további)" : "Új hír a Hearthstone Hungary-n", item.Title, item.Url)));
            Sync.Uploaded += message => Dispatcher.BeginInvoke(new Action(() => ShowBalloon("Gyűjtemény feltöltve", message)));
            Sync.LinkLost += message => Dispatcher.BeginInvoke(new Action(() => ShowBalloon("Leválasztva", message)));
            Updater.StatusChanged += () => Dispatcher.BeginInvoke(new Action(OnUpdaterStatusChanged));

            CreateTrayIcon();
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
                (state, timedOut) => Dispatcher.BeginInvoke(new Action(ShowMainWindow)), null, Timeout.Infinite, false);

            _timer = new DispatcherTimer { Interval = TickInterval };
            _timer.Tick += async (sender, args) => await TickAsync();
            _timer.Start();

            if (!e.Args.Contains(WindowsStartup.MinimizedArg, StringComparer.OrdinalIgnoreCase))
                ShowMainWindow();

            Dispatcher.BeginInvoke(new Action(async () => await TickAsync()), DispatcherPriority.Background);
        }

        private async Task TickAsync()
        {
            if (_tickRunning || _exiting)
                return;
            _tickRunning = true;
            try
            {
                var state = await Task.Run(() => Reader.Refresh());
                var becameReady = state == GameState.Ready && _lastGameState != GameState.Ready;
                if (state != _lastGameState)
                {
                    Log.Info("Hearthstone állapot: " + state);
                    _lastGameState = state;
                    GameStateChanged?.Invoke();
                }

                if (state == GameState.Ready && (becameReady || DateTime.UtcNow - _lastAutoSyncUtc >= AutoSyncInterval))
                {
                    _lastAutoSyncUtc = DateTime.UtcNow;
                    await Sync.SyncAsync(SyncTrigger.Automatic);
                }

                // Indításkor (az első körben) és óránként: nem választották-e le az eszközt a profilban.
                await Sync.CheckLinkAsync(LinkCheckInterval);

                // A játék naplójából a paklik (futó játéknál; a fájl csak akkor olvasódik újra, ha változott).
                if (state != GameState.NotRunning)
                    await Task.Run(() => Decks.Refresh(HearthstoneLogs.FindInstallDirectory()));

                if (Settings.NewsNotifications && News.IsCheckDue)
                    await News.CheckAsync();

                if (Settings.AutoUpdate && Updater.IsCheckDue)
                    await Updater.CheckAndInstallAsync(manual: false);

                // Telepített frissítés: ha épp nem használja senki (rejtett ablak, nem fut a játék), magától újraindulunk.
                if (Updater.State == UpdateState.Installed && state == GameState.NotRunning && (_window == null || !_window.IsVisible))
                {
                    Log.Info("Újraindítás a frissítés miatt.");
                    Restart(minimized: true);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Hiba az időzített ellenőrzésben: " + ex);
            }
            finally
            {
                _tickRunning = false;
            }
        }

        private void OnUpdaterStatusChanged()
        {
            if (Updater.State == UpdateState.Installed && !_updateNotified)
            {
                _updateNotified = true;
                ShowBalloon("Frissítés telepítve", Updater.StatusText);
            }
        }

        internal void ShowMainWindow()
        {
            if (_exiting)
                return;
            if (_window == null)
            {
                _window = new MainWindow(this);
                _window.Closed += (sender, args) => _window = null;
            }
            _window.Show();
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
            _window.Activate();
            // A megnyitott ablak friss állapotot mutasson (a profilban közben leválaszthatták).
            _ = Sync.CheckLinkAsync(LinkCheckOnShowInterval);
        }

        /// <summary>Az ablak bezárásakor: a program a tálcán fut tovább.</summary>
        internal void OnWindowHidden()
        {
            if (Settings.TrayHintShown)
                return;
            Settings.TrayHintShown = true;
            Settings.Save();
            ShowBalloon("Hearthstone Hungary", "A program a tálcán fut tovább, és feltölti a gyűjteményedet. Kilépni a tálcaikon menüjéből lehet.");
        }

        internal bool IsExiting => _exiting;

        private void CreateTrayIcon()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Megnyitás", null, (s, a) => ShowMainWindow());
            menu.Items.Add("Feltöltés most", null, async (s, a) => await Sync.SyncAsync(SyncTrigger.Manual));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Kilépés", null, (s, a) => ExitApp());

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly().Location),
                Text = "Hearthstone Hungary",
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.MouseClick += (s, a) =>
            {
                if (a.Button == Forms.MouseButtons.Left)
                    ShowMainWindow();
            };
            // Az értesítésre kattintva: ha cikkhez tartozik, a cikk nyílik meg, különben az ablak.
            _trayIcon.BalloonTipClicked += (s, a) =>
            {
                if (_balloonUrl != null)
                    OpenInBrowser(_balloonUrl);
                else
                    ShowMainWindow();
            };
        }

        /// <param name="url">Ha meg van adva, az értesítésre kattintva ez nyílik meg a böngészőben.</param>
        private void ShowBalloon(string title, string text, string url = null)
        {
            if (_trayIcon == null || string.IsNullOrEmpty(text))
                return;
            _balloonUrl = url;
            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText = text.Length > 250 ? text.Substring(0, 250) + "…" : text;
            _trayIcon.ShowBalloonTip(5000);
        }

        internal void ExitApp()
        {
            if (_exiting)
                return;
            _exiting = true;
            Log.Info("Kilépés.");
            _timer?.Stop();
            _showWait?.Unregister(null);
            ThemeManager.Shutdown();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            _window?.Close();
            Reader?.Dispose();
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
            }
            Shutdown();
        }

        /// <summary>Újraindítás (frissítés után): az új példány a már kicserélt exe-ből indul.</summary>
        internal void Restart(bool minimized)
        {
            var exe = Assembly.GetEntryAssembly().Location;
            ExitApp();
            try
            {
                Process.Start(new ProcessStartInfo(exe, minimized ? WindowsStartup.MinimizedArg : "") { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                Log.Error("Nem sikerült újraindítani: " + ex);
            }
        }

        /// <summary>Weboldal (vagy fájl) megnyitása az alapértelmezett programmal.</summary>
        internal static void OpenInBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Nem sikerült megnyitni: " + url + " – " + ex.Message);
            }
        }

        private static Version GetVersion()
        {
            var v = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }
}
