using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using HearthstoneHungary.Core;

namespace HearthstoneHungary
{
    public partial class MainWindow
    {
        private readonly App _app;
        private bool _initializing = true;

        internal MainWindow(App app)
        {
            _app = app;
            InitializeComponent();

            AutoUploadCheck.IsChecked = _app.Settings.AutoUpload;
            StartWithWindowsCheck.IsChecked = _app.Settings.StartWithWindows;
            AutoUpdateCheck.IsChecked = _app.Settings.AutoUpdate;
            NewsCheck.IsChecked = _app.Settings.NewsNotifications;
            VersionText.Text = $"Verzió: v{App.Version}";

            _app.Sync.StatusChanged += OnSyncStatusChanged;
            _app.Updater.StatusChanged += OnUpdaterStatusChanged;
            _app.GameStateChanged += RefreshGameUi;
            ThemeManager.Changed += OnThemeChanged;
            SourceInitialized += (sender, args) => ThemeManager.ApplyTitleBar(this);
            Closed += (sender, args) =>
            {
                _app.Sync.StatusChanged -= OnSyncStatusChanged;
                _app.Updater.StatusChanged -= OnUpdaterStatusChanged;
                _app.GameStateChanged -= RefreshGameUi;
                ThemeManager.Changed -= OnThemeChanged;
            };

            RefreshGameUi();
            RefreshSyncUi();
            RefreshUpdateUi();
            _initializing = false;
        }

        /// <summary>Bezáráskor csak elrejtjük az ablakot: a program a tálcán fut tovább.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_app.IsExiting)
            {
                e.Cancel = true;
                Hide();
                _app.OnWindowHidden();
            }
            base.OnClosing(e);
        }

        // A státusz események háttérszálról is jöhetnek, ezért a UI szálra tesszük át őket.
        private void OnSyncStatusChanged() => Dispatcher.BeginInvoke(new Action(RefreshSyncUi));

        private void OnUpdaterStatusChanged() => Dispatcher.BeginInvoke(new Action(RefreshUpdateUi));

        // A színeket a DynamicResource-ok maguktól követik, csak a címsort kell átállítani.
        private void OnThemeChanged() => ThemeManager.ApplyTitleBar(this);

        private void RefreshGameUi()
        {
            switch (_app.GameState)
            {
                case GameState.Ready:
                    GameStatusText.Text = "A Hearthstone fut, a gyűjtemény kiolvasható.";
                    GameDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "HshuGreen");
                    break;
                case GameState.Loading:
                    GameStatusText.Text = "A Hearthstone fut, várakozás a főmenüre…";
                    GameDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "HshuOrange");
                    break;
                default:
                    GameStatusText.Text = "A Hearthstone nem fut. Indítsd el, és a gyűjteményed magától feltöltődik.";
                    GameDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "HshuMuted");
                    break;
            }
        }

        private void RefreshSyncUi()
        {
            var linked = _app.Sync.IsLinked;
            var busy = _app.Sync.IsBusy;
            LinkStatusText.Text = linked
                ? "Összekapcsolva" + (string.IsNullOrEmpty(_app.Sync.LinkedUserName) ? "" : ": " + _app.Sync.LinkedUserName)
                : "Nincs összekapcsolva";
            var linking = _app.Sync.IsLinking;
            LinkPanel.Visibility = !linked && !linking ? Visibility.Visible : Visibility.Collapsed;
            LinkWaitPanel.Visibility = linking ? Visibility.Visible : Visibility.Collapsed;
            LinkCodeText.Text = _app.Sync.LinkCode ?? "";
            UnlinkButton.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            UploadNowButton.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            AutoUploadCheck.IsEnabled = linked;
            UploadNowButton.IsEnabled = !busy;
            UploadStatusText.Text = _app.Sync.StatusText;
        }

        private void RefreshUpdateUi()
        {
            UpdateStatusText.Text = _app.Updater.StatusText;
            var installed = _app.Updater.State == UpdateState.Installed;
            RestartButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            CheckUpdateButton.IsEnabled = _app.Updater.State != UpdateState.Checking && !installed;
        }

        private void AutoUploadCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            _app.Settings.AutoUpload = AutoUploadCheck.IsChecked == true;
            _app.Settings.Save();
        }

        private void StartWithWindowsCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            _app.Settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
            _app.Settings.Save();
            WindowsStartup.Apply(_app.Settings.StartWithWindows);
        }

        private void NewsCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            _app.Settings.NewsNotifications = NewsCheck.IsChecked == true;
            _app.Settings.Save();
            // bekapcsoláskor csak az ezután megjelenő hírekről szólunk
            if (_app.Settings.NewsNotifications)
                _app.News.ResetBaseline();
        }

        private void AutoUpdateCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            _app.Settings.AutoUpdate = AutoUpdateCheck.IsChecked == true;
            _app.Settings.Save();
        }

        private void AppPage_Click(object sender, RoutedEventArgs e) => OpenUrl(HshuConfig.AppPageUrl);

        private async void Link_Click(object sender, RoutedEventArgs e)
        {
            LinkButton.IsEnabled = false;
            try
            {
                if (await _app.Sync.LinkViaBrowserAsync(OpenUrl))
                {
                    // a jóváhagyás után a böngészőből visszahozzuk az ablakot
                    _app.ShowMainWindow();
                    if (_app.GameState == GameState.Ready)
                        await _app.Sync.SyncAsync(SyncTrigger.Manual);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Összekötés: " + ex);
                ShowError("Hiba az összekötés közben: " + ex.Message);
            }
            finally
            {
                LinkButton.IsEnabled = true;
                RefreshSyncUi();
            }
        }

        private void CancelLink_Click(object sender, RoutedEventArgs e) => _app.Sync.CancelLink();

        private void OpenLinkPage_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_app.Sync.LinkUrl))
                OpenUrl(_app.Sync.LinkUrl);
        }

        private async void Unlink_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(this,
                "Biztosan leválasztod ezt a gépet a Hearthstone Hungary profilodról?\n\nA gyűjteményed innen nem frissül tovább magától. Később újra összekötheted.",
                "Leválasztás", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
            UnlinkButton.IsEnabled = false;
            try
            {
                await _app.Sync.UnlinkAsync();
            }
            finally
            {
                UnlinkButton.IsEnabled = true;
                RefreshSyncUi();
            }
        }

        private async void UploadNow_Click(object sender, RoutedEventArgs e)
        {
            UploadNowButton.IsEnabled = false;
            try
            {
                await _app.Sync.SyncAsync(SyncTrigger.Manual);
            }
            catch (Exception ex)
            {
                Log.Error("Feltöltés: " + ex);
                ShowError("Hiba a feltöltés közben: " + ex.Message);
            }
            finally
            {
                RefreshSyncUi();
            }
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            try
            {
                await _app.Updater.CheckAndInstallAsync(manual: true);
            }
            catch (Exception ex)
            {
                Log.Error("Frissítés: " + ex);
                ShowError("Hiba a frissítés közben: " + ex.Message);
            }
            finally
            {
                RefreshUpdateUi();
            }
        }

        private void Restart_Click(object sender, RoutedEventArgs e) => _app.Restart(minimized: false);

        private void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(Log.FilePath))
                OpenUrl(Log.FilePath);
            else
                ShowError("Még nincs napló.");
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => _app.ExitApp();

        private void ShowError(string message) =>
            MessageBox.Show(this, message, "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);

        private static void OpenUrl(string url) => App.OpenInBrowser(url);
    }
}
