using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using HearthstoneHungary.Core;

namespace HearthstoneHungary
{
    public partial class MainWindow
    {
        private const string NoDecksText = "Még nem láttam paklit. Indíts el egy meccset a Hearthstone-ban, és a paklid megjelenik itt. (Ha most telepítetted az alkalmazást, előbb indítsd újra a játékot.)";

        private const string NoGamesText = "Még nincs rögzített meccs. Játssz egy Ranked, Casual vagy aréna meccset, és az eredménye megjelenik itt. (Ha most telepítetted az alkalmazást, előbb indítsd újra a játékot.)";
        private const int MaxStatsModes = 4;
        private const int MaxRecentGames = 4;

        private readonly App _app;
        private bool _initializing = true;
        private bool _fillingDecks;
        private string _selectedDeckKey;
        private string _deckMessage;
        private string _deckListSignature;

        internal MainWindow(App app)
        {
            _app = app;
            InitializeComponent();

            AutoUploadCheck.IsChecked = _app.Settings.AutoUpload;
            StartWithWindowsCheck.IsChecked = _app.Settings.StartWithWindows;
            AutoUpdateCheck.IsChecked = _app.Settings.AutoUpdate;
            NewsCheck.IsChecked = _app.Settings.NewsNotifications;
            TrackGamesCheck.IsChecked = _app.Settings.TrackGames;
            VersionText.Text = $"Verzió: v{App.Version}";

            _app.Sync.StatusChanged += OnSyncStatusChanged;
            _app.Updater.StatusChanged += OnUpdaterStatusChanged;
            _app.GameStateChanged += RefreshGameUi;
            _app.Decks.Changed += OnDecksChanged;
            _app.Games.Changed += OnGamesChanged;
            ThemeManager.Changed += OnThemeChanged;
            SourceInitialized += (sender, args) => ThemeManager.ApplyTitleBar(this);
            Closed += (sender, args) =>
            {
                _app.Sync.StatusChanged -= OnSyncStatusChanged;
                _app.Updater.StatusChanged -= OnUpdaterStatusChanged;
                _app.GameStateChanged -= RefreshGameUi;
                _app.Decks.Changed -= OnDecksChanged;
                _app.Games.Changed -= OnGamesChanged;
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

        private void OnDecksChanged() => Dispatcher.BeginInvoke(new Action(RefreshDeckUi));

        private void OnGamesChanged() => Dispatcher.BeginInvoke(new Action(RefreshStatsUi));

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
            RefreshDeckUi();
            RefreshStatsUi();
        }

        /// <summary>Fülváltás: a kiválasztott rész látszik, a többi Hidden (a helyük megmarad, így az ablak mérete nem változik).</summary>
        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            // Az InitializeComponent közben is lefut, amikor a panelek még nem léteznek.
            if (CollectionPanel == null || DecksPanel == null || StatsPanel == null || SettingsPanel == null)
                return;
            CollectionPanel.Visibility = CollectionTab.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
            DecksPanel.Visibility = DecksTab.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
            StatsPanel.Visibility = StatsTab.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
            SettingsPanel.Visibility = SettingsTab.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
        }

        /// <summary>A paklik listája: az utoljára játszott elöl; a felhasználó választása megmarad, amíg a pakli létezik.</summary>
        private void RefreshDeckUi()
        {
            var linked = _app.Sync.IsLinked;
            var decks = _app.Decks.Decks;
            // A listát csak akkor cseréljük, ha tényleg változott (különben a nyitott legördülő lista minden állapotfrissítésnél becsukódna).
            var signature = string.Join("|", decks.Select(d => d.Key + "/" + d.Name + "/" + d.Deckstring));
            if (signature != _deckListSignature)
            {
                _deckListSignature = signature;
                FillDeckCombo(decks);
            }
            var any = linked && decks.Count > 0;
            DeckCombo.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            DeckButtons.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            DeckStatusText.Text = !linked ? "A paklik feltöltéséhez előbb kösd össze az alkalmazást a Hearthstone Hungary profiloddal a Gyűjtemény fülön."
                : any ? (_deckMessage ?? "A játékban használt paklijaid; az utoljára játszott van elöl.") : NoDecksText;
        }

        private void FillDeckCombo(System.Collections.Generic.IReadOnlyList<SavedDeck> decks)
        {
            _fillingDecks = true;
            try
            {
                DeckCombo.ItemsSource = decks;
                var selected = decks.FirstOrDefault(d => d.Key == _selectedDeckKey) ?? decks.FirstOrDefault();
                DeckCombo.SelectedItem = selected;
                _selectedDeckKey = selected?.Key;
            }
            finally
            {
                _fillingDecks = false;
            }
        }

        private void DeckCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_fillingDecks)
                return;
            _selectedDeckKey = (DeckCombo.SelectedItem as SavedDeck)?.Key;
            _deckMessage = null;
            DeckStatusText.Text = "A játékban használt paklijaid; az utoljára játszott van elöl.";
        }

        private void DeckCreate_Click(object sender, RoutedEventArgs e) => SendDeck("instant",
            "Megnyitottam a böngészőben: a pakli létrejön a profilodban, és a pakli oldalára kerülsz.");

        private void DeckBuilder_Click(object sender, RoutedEventArgs e) => SendDeck("builder",
            "Megnyitottam a böngészőben a pakli készítőt a pakli lapjaival.");

        private async void SendDeck(string mode, string successText)
        {
            var deck = DeckCombo.SelectedItem as SavedDeck;
            if (deck == null)
                return;
            DeckCreateButton.IsEnabled = DeckBuilderButton.IsEnabled = false;
            try
            {
                _deckMessage = await _app.Sync.OpenDeckOnSiteAsync(deck, mode, OpenUrl) ?? successText;
            }
            catch (Exception ex)
            {
                Log.Error("Pakli küldése: " + ex);
                _deckMessage = "Hiba a pakli küldése közben: " + ex.Message;
            }
            finally
            {
                DeckCreateButton.IsEnabled = DeckBuilderButton.IsEnabled = true;
                DeckStatusText.Text = _deckMessage;
                RefreshSyncUi();
            }
        }

        /// <summary>A Statisztika fül: módonként a győzelmek és vereségek, alatta a legutóbbi meccsek.</summary>
        private void RefreshStatsUi()
        {
            var games = _app.Games.Games;
            var tracking = _app.Settings.TrackGames;
            if (games.Count == 0)
            {
                StatsSummaryText.Text = tracking ? NoGamesText : "A statisztikák mentése ki van kapcsolva (Beállítások fül).";
                StatsRecentTitle.Visibility = StatsRecentText.Visibility = Visibility.Collapsed;
                StatsStatusText.Text = "";
                return;
            }

            // módonként egy sor; a legtöbbet játszott mód elöl
            var summary = games
                .GroupBy(g => GameInfo.ModeName(g.GameType, g.FormatType))
                .OrderByDescending(g => g.Count())
                .Take(MaxStatsModes)
                .Select(g =>
                {
                    var wins = g.Count(x => x.Result == "win");
                    var losses = g.Count(x => x.Result == "loss");
                    var rate = wins + losses > 0 ? $" ({Math.Round(100.0 * wins / (wins + losses))}%)" : "";
                    return $"{g.Key}: {wins} győzelem, {losses} vereség{rate}";
                });
            StatsSummaryText.Text = string.Join(Environment.NewLine, summary);

            StatsRecentTitle.Visibility = StatsRecentText.Visibility = Visibility.Visible;
            StatsRecentText.Text = string.Join(Environment.NewLine, games.Take(MaxRecentGames).Select(g =>
                $"{g.StartedUtc.ToLocalTime():MM.dd. HH:mm} · {GameInfo.ResultName(g.Result)} · {GameInfo.HeroClass(g.PlayerHero)} – {GameInfo.HeroClass(g.OpponentHero)}"
                + (string.IsNullOrEmpty(g.DeckName) ? "" : " · " + g.DeckName)));

            var pending = _app.Games.PendingCount;
            StatsStatusText.Text = !tracking ? "A statisztikák mentése ki van kapcsolva (Beállítások fül)."
                : !_app.Sync.IsLinked ? "A meccsek csak ezen a gépen vannak meg. Kösd össze az alkalmazást a Hearthstone Hungary profiloddal, és feltöltődnek."
                : pending > 0 ? $"{pending} meccs feltöltésre vár."
                : "Minden meccs fel van töltve a Hearthstone Hungary profilodba.";
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

        private void TrackGamesCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            _app.Settings.TrackGames = TrackGamesCheck.IsChecked == true;
            _app.Settings.Save();
            RefreshStatsUi();
        }

        private void AutoUpdateCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            _app.Settings.AutoUpdate = AutoUpdateCheck.IsChecked == true;
            _app.Settings.Save();
        }

        private void AppPage_Click(object sender, RoutedEventArgs e) => OpenUrl(HshuConfig.AppPageUrl);

        private void SiteSettings_Click(object sender, RoutedEventArgs e) => OpenUrl(HshuConfig.SettingsPageUrl);

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
