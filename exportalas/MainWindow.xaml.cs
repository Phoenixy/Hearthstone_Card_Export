using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using exportalas.Internal;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Utility.Logging;
using Microsoft.Win32;

namespace exportalas
{
    public partial class MainWindow
    {
        private readonly PluginSettings _settings;
        private readonly CollectionSync _sync;
        private readonly PluginUpdater _updater;
        private bool _initializing = true;

        internal MainWindow(PluginSettings settings, CollectionSync sync, PluginUpdater updater)
        {
            _settings = settings;
            _sync = sync;
            _updater = updater;

            InitializeComponent();

            AutoUploadCheck.IsChecked = _settings.AutoUpload;
            AutoUpdateCheck.IsChecked = _settings.AutoUpdate;
            VersionText.Text = $"Verzió: v{ExportalasPlugin.PluginVersion}";

            _sync.StatusChanged += OnSyncStatusChanged;
            _updater.StatusChanged += OnUpdaterStatusChanged;
            Closed += (sender, args) =>
            {
                _sync.StatusChanged -= OnSyncStatusChanged;
                _updater.StatusChanged -= OnUpdaterStatusChanged;
            };

            RefreshSyncUi();
            RefreshUpdateUi();
            _initializing = false;
        }

        // A státusz események háttérszálról is jöhetnek, ezért a UI szálra tesszük át őket.
        private void OnSyncStatusChanged() => Dispatcher.BeginInvoke(new Action(RefreshSyncUi));

        private void OnUpdaterStatusChanged() => Dispatcher.BeginInvoke(new Action(RefreshUpdateUi));

        private void RefreshSyncUi()
        {
            var linked = _sync.IsLinked;
            var busy = _sync.IsBusy;
            LinkStatusText.Text = linked
                ? "Összekapcsolva" + (string.IsNullOrEmpty(_sync.LinkedUserName) ? "" : ": " + _sync.LinkedUserName)
                : "Nincs összekapcsolva";
            TokenPanel.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
            UnlinkButton.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            AutoUploadCheck.IsEnabled = linked;
            UploadNowButton.IsEnabled = linked && !busy;
            SaveTokenButton.IsEnabled = !busy;
            UploadStatusText.Text = _sync.StatusText;
        }

        private void RefreshUpdateUi()
        {
            UpdateStatusText.Text = _updater.StatusText;
            CheckUpdateButton.IsEnabled = _updater.State != UpdateState.Checking && _updater.State != UpdateState.Installed;
        }

        private void AutoUploadCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            _settings.AutoUpload = AutoUploadCheck.IsChecked == true;
            _settings.Save();
        }

        private void AutoUpdateCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            _settings.AutoUpdate = AutoUpdateCheck.IsChecked == true;
            _settings.Save();
        }

        private void TokenHelp_Click(object sender, RoutedEventArgs e) => OpenUrl(HshuConfig.TokenPageUrl);

        private async void SaveToken_Click(object sender, RoutedEventArgs e)
        {
            SaveTokenButton.IsEnabled = false;
            try
            {
                if (await _sync.LinkAsync(TokenBox.Password))
                {
                    TokenBox.Clear();
                    if (Core.Game.IsRunning)
                        await _sync.SyncAsync(SyncTrigger.Manual);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[HSHU] " + ex);
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
                "Biztosan leválasztod a plugint a Hearthstone Hungary profilodról?\n\nA gyűjteményed nem frissül tovább magától. A tokent a profilodban is érdemes visszavonni.",
                "Leválasztás", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
            _sync.Unlink();
            RefreshSyncUi();
        }

        private async void UploadNow_Click(object sender, RoutedEventArgs e)
        {
            UploadNowButton.IsEnabled = false;
            try
            {
                await _sync.SyncAsync(SyncTrigger.Manual);
            }
            catch (Exception ex)
            {
                Log.Error("[HSHU] " + ex);
                ShowError("Hiba a feltöltés közben: " + ex.Message);
            }
            finally
            {
                RefreshSyncUi();
            }
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            ExportButton.IsEnabled = false;
            try
            {
                var snapshot = await CollectionSnapshot.ReadAsync(refresh: true);
                if (snapshot == null)
                {
                    ShowError(Core.Game.IsRunning
                        ? "Nem sikerült kiolvasni a gyűjteményt. Várd meg, amíg betölt a főmenü, és próbáld újra."
                        : "Nem fut a Hearthstone! Indítsd el, és várd meg a főmenüt.");
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = "Gyűjtemény exportálása",
                    Filter = "CSV fájl (*.csv)|*.csv",
                    DefaultExt = ".csv",
                    FileName = "gyujtemeny.csv",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                };
                if (dialog.ShowDialog(this) != true)
                    return;

                var path = dialog.FileName;
                await Task.Run(() => CsvExporter.Write(path, snapshot));
                MessageBox.Show(this, $"Sikeres exportálás! A {Path.GetFileName(path)} fájl létrejött.\n\nMost töltsd fel a Hearthstone Hungary-n a profilod gyűjtemény oldalán.",
                    "Sikeres exportálás", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (IOException ex)
            {
                ShowError("Hiba történt a fájl írása közben: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error("[HSHU] " + ex);
                ShowError("Ismeretlen hiba történt az exportálás során: " + ex.Message);
            }
            finally
            {
                ExportButton.IsEnabled = true;
            }
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            try
            {
                await _updater.CheckAndInstallAsync(manual: true);
            }
            catch (Exception ex)
            {
                Log.Error("[HSHU] " + ex);
                ShowError("Hiba a frissítés közben: " + ex.Message);
            }
            finally
            {
                RefreshUpdateUi();
            }
        }

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
                Log.Error("[HSHU] Nem sikerült megnyitni: " + url + " – " + ex.Message);
            }
        }
    }
}
