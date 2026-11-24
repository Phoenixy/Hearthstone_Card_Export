using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
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
            TokenPanel.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
            UnlinkButton.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            UploadNowButton.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            AutoUploadCheck.IsEnabled = linked;
            UploadNowButton.IsEnabled = !busy;
            SaveTokenButton.IsEnabled = !busy;
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

        private void AutoUpdateCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            _app.Settings.AutoUpdate = AutoUpdateCheck.IsChecked == true;
            _app.Settings.Save();
        }

        private void TokenHelp_Click(object sender, RoutedEventArgs e) => OpenUrl(HshuConfig.TokenPageUrl);

        private void TokenBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SaveToken_Click(sender, e);
        }

        private async void SaveToken_Click(object sender, RoutedEventArgs e)
        {
            SaveTokenButton.IsEnabled = false;
            try
            {
                if (await _app.Sync.LinkAsync(TokenBox.Password))
                {
                    TokenBox.Clear();
                    if (_app.GameState == GameState.Ready)
                        await _app.Sync.SyncAsync(SyncTrigger.Manual);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Összekapcsolás: " + ex);
                ShowError("Hiba az összekapcsolás közben: " + ex.Message);
            }
            finally
            {
                RefreshSyncUi();
            }
        }

        private void Unlink_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(this,
                "Biztosan leválasztod az alkalmazást a Hearthstone Hungary profilodról?\n\nA gyűjteményed nem frissül tovább magától. A tokent a profilodban is érdemes visszavonni.",
                "Leválasztás", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
            _app.Sync.Unlink();
            RefreshSyncUi();
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

        private static void OpenUrl(string url)
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
    }
}
