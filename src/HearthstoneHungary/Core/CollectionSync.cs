using System;
using System.Threading;
using System.Threading.Tasks;

namespace HearthstoneHungary.Core
{
    internal enum SyncTrigger
    {
        /// <summary>Időzített ellenőrzés. Csak akkor töltünk fel, ha változott a gyűjtemény (vagy rég volt feltöltés).</summary>
        Automatic,

        /// <summary>A felhasználó kattintott. Mindig feltöltünk, és minden hibát kiírunk.</summary>
        Manual
    }

    /// <summary>A gyűjtemény feltöltése a HSHU profilba, és a token kezelése.</summary>
    internal sealed class CollectionSync
    {
        /// <summary>Ha a lapok száma ennyi alá esik az előző feltöltéshez képest, automatikusan nem töltünk fel (valószínűleg hiányos kiolvasás).</summary>
        private const double SuspiciousDropRatio = 0.5;

        private static readonly TimeSpan RetryDelayAfterFailure = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Változatlan gyűjteményt is feltöltünk ennyi idő után: az új kiegészítő lapjait az oldal csak a kártya import után
        /// ismeri, így ezek a következő feltöltéssel kerülnek fel.
        /// </summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

        private readonly AppSettings _settings;
        private readonly GameReader _reader;
        private int _busy;
        private int _checking;
        private DateTime _lastLinkCheckUtc = DateTime.MinValue;
        private DateTime _lastAutomaticFailureUtc = DateTime.MinValue;

        public CollectionSync(AppSettings settings, GameReader reader)
        {
            _settings = settings;
            _reader = reader;
            var last = settings.LastUpload;
            if (last != null)
                StatusText = DescribeUpload(last.UploadedAtUtc, last.ShownCards > 0 ? last.ShownCards : last.TotalCards, last.BattleTag);
        }

        /// <summary>Akkor hívódik, ha változott a státusz szöveg vagy az összekapcsolás. Bármelyik szálról jöhet.</summary>
        public event Action StatusChanged;

        /// <summary>Sikeres automatikus feltöltés után (a tálcaértesítéshez). A szöveg a szerver üzenete.</summary>
        public event Action<string> Uploaded;

        /// <summary>A szerver elutasította a tokent (pl. a profilban leválasztották az eszközt); a szöveg a kiírt üzenet.</summary>
        public event Action<string> LinkLost;

        public string StatusText { get; private set; } = "";

        public bool IsBusy => Volatile.Read(ref _busy) == 1;

        public bool IsLinked => TokenStore.Unprotect(_settings.ProtectedToken) != null;

        public string LinkedUserName => _settings.LinkedUserName;

        /// <summary>Folyamatban van-e a böngészős összekötés (várjuk a jóváhagyást).</summary>
        public bool IsLinking => _linkCts != null;

        /// <summary>Összekötés közben a kód (pl. KX7P-29QD), ami a böngészőben megnyílt oldalon is látszik.</summary>
        public string LinkCode { get; private set; }

        /// <summary>Összekötés közben a jóváhagyó oldal címe (ha a böngésző nem nyílt meg magától).</summary>
        public string LinkUrl { get; private set; }

        private CancellationTokenSource _linkCts;

        /// <summary>
        /// Összekötés a HSHU profillal: kódot kér a szervertől, megnyitja a jóváhagyó oldalt a böngészőben, és
        /// pár másodpercenként rákérdez, amíg a felhasználó jóvá nem hagyja (vagy le nem jár a kód). A UI szálról kell hívni.
        /// </summary>
        /// <param name="openUrl">A jóváhagyó oldal megnyitása a böngészőben.</param>
        /// <returns>true, ha sikerült összekötni.</returns>
        public async Task<bool> LinkViaBrowserAsync(Action<string> openUrl)
        {
            if (_linkCts != null)
                return false;
            var cts = new CancellationTokenSource();
            _linkCts = cts;
            try
            {
                SetStatus("Összekötés indítása…");
                var (start, error) = await HshuApiClient.StartLinkAsync(_settings.DeviceId, Environment.MachineName);
                if (start == null)
                {
                    SetStatus(error);
                    return false;
                }

                LinkCode = start.UserCode;
                LinkUrl = start.VerifyUrl;
                Log.Info("Összekötés indult, kód: " + start.UserCode);
                SetStatus("Hagyd jóvá az összekötést a böngészőben megnyílt oldalon.");
                openUrl(start.VerifyUrl);

                var deadline = DateTime.UtcNow + start.ExpiresIn;
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(start.Interval, cts.Token);
                    var poll = await HshuApiClient.PollLinkAsync(start.DeviceCode);
                    cts.Token.ThrowIfCancellationRequested();
                    switch (poll.Status)
                    {
                        case LinkPollStatus.Approved:
                            _settings.ProtectedToken = TokenStore.Protect(poll.Token);
                            _settings.LinkedUserName = poll.UserName;
                            _settings.Save();
                            Log.Info($"Összekapcsolva: {poll.UserName}");
                            SetStatus("Sikeres összekapcsolás." + (_reader.State == GameState.NotRunning
                                ? " A gyűjtemény a Hearthstone indítása után töltődik fel."
                                : " Feltöltés indul…"));
                            return true;
                        case LinkPollStatus.Failed:
                            Log.Warn("Összekötés sikertelen: " + poll.Message);
                            SetStatus(poll.Message);
                            return false;
                    }
                }
                SetStatus("Az összekötési kód lejárt. Kattints újra az összekötés gombra.");
                return false;
            }
            catch (OperationCanceledException)
            {
                SetStatus("Az összekötés megszakítva.");
                return false;
            }
            finally
            {
                _linkCts = null;
                LinkCode = null;
                LinkUrl = null;
                cts.Dispose();
                StatusChanged?.Invoke();
            }
        }

        /// <summary>A folyamatban lévő összekötés megszakítása (a kód a szerveren magától lejár).</summary>
        public void CancelLink() => _linkCts?.Cancel();

        /// <summary>
        /// Él-e még az összekötés a szerveren, legfeljebb <paramref name="minInterval"/>-onként egyszer.
        /// Indításkor, az ablak megnyitásakor és óránként hívódik, hogy a profilban történt leválasztás akkor is kiderüljön,
        /// ha épp nincs feltöltés. Hálózati vagy szerverhibánál nem csinál semmit (nem válunk le egy átmeneti hiba miatt).
        /// </summary>
        public async Task CheckLinkAsync(TimeSpan minInterval)
        {
            var token = TokenStore.Unprotect(_settings.ProtectedToken);
            if (token == null || IsLinking || IsBusy || DateTime.UtcNow - _lastLinkCheckUtc < minInterval)
                return;
            if (Interlocked.Exchange(ref _checking, 1) == 1)
                return;
            try
            {
                _lastLinkCheckUtc = DateTime.UtcNow;
                var result = await HshuApiClient.CheckTokenAsync(token);
                // közben a felhasználó leválasztotta vagy újra összekötötte: ez az eredmény már nem érvényes
                if (TokenStore.Unprotect(_settings.ProtectedToken) != token)
                    return;
                if (result.Status == ApiStatus.InvalidToken)
                {
                    OnTokenRejected(result.Message);
                }
                else if (result.Status == ApiStatus.Ok && !string.IsNullOrEmpty(result.UserName) && result.UserName != _settings.LinkedUserName)
                {
                    _settings.LinkedUserName = result.UserName;
                    _settings.Save();
                    StatusChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Az összekötés ellenőrzése nem sikerült: " + ex.Message);
            }
            finally
            {
                Volatile.Write(ref _checking, 0);
            }
        }

        /// <summary>
        /// Pakli küldése az oldalra: jegyet kér a szervertől, és megnyitja a böngészőben. "instant" módban a pakli azonnal
        /// létrejön a profilban, "builder" módban a pakli készítő nyílik meg a lapokkal.
        /// </summary>
        /// <returns>null, ha sikerült megnyitni; különben a kiírandó hibaüzenet.</returns>
        public async Task<string> OpenDeckOnSiteAsync(SavedDeck deck, string mode, Action<string> openUrl)
        {
            var token = TokenStore.Unprotect(_settings.ProtectedToken);
            if (token == null)
                return "Előbb kösd össze az alkalmazást a Hearthstone Hungary profiloddal.";
            if (deck == null || string.IsNullOrEmpty(deck.Deckstring))
                return "Nincs kiválasztott pakli.";

            var result = await HshuApiClient.CreateDeckTicketAsync(token, mode, deck.Deckstring, deck.Name);
            switch (result.Status)
            {
                case ApiStatus.Ok:
                    Log.Info($"Pakli küldése az oldalra ({mode}): {deck.Name}");
                    openUrl(result.Message);
                    return null;
                case ApiStatus.InvalidToken:
                    OnTokenRejected(result.Message);
                    return StatusText;
                default:
                    Log.Warn($"A pakli küldése nem sikerült ({result.Status}): {result.Message}");
                    return result.Message ?? "A pakli küldése nem sikerült, próbáld újra később.";
            }
        }

        /// <summary>A szerver elutasította a tokent: töröljük, hogy ne próbálkozzunk vele újra és újra.</summary>
        private void OnTokenRejected(string serverMessage)
        {
            _settings.ProtectedToken = null;
            _settings.LinkedUserName = null;
            _settings.Save();
            Log.Warn("A szerver elutasította a tokent, leválasztva: " + serverMessage);
            var text = serverMessage ?? "Ezt a gépet leválasztották a Hearthstone Hungary profilodról. Kösd össze újra az összekötés gombbal.";
            SetStatus(text);
            LinkLost?.Invoke(text);
        }

        /// <summary>Leválasztás: a szerveren is visszavonjuk ennek az eszköznek a tokenjét, aztán töröljük.</summary>
        public async Task UnlinkAsync()
        {
            var token = TokenStore.Unprotect(_settings.ProtectedToken);
            _settings.ProtectedToken = null;
            _settings.LinkedUserName = null;
            _settings.Save();
            var revoked = token == null || await HshuApiClient.UnlinkAsync(token);
            Log.Info(revoked ? "Leválasztva." : "Leválasztva, de a szerveren nem sikerült visszavonni a tokent.");
            SetStatus(revoked
                ? "Az alkalmazás le van választva a Hearthstone Hungary profilodról."
                : "Az alkalmazás le van választva. A szervert nem értem el, ezért az eszközt a profilod Gyűjtemény fülén is válaszd le.");
        }

        public async Task SyncAsync(SyncTrigger trigger)
        {
            var manual = trigger == SyncTrigger.Manual;
            if (Interlocked.Exchange(ref _busy, 1) == 1)
            {
                if (manual)
                    SetStatus("Már folyamatban van egy feltöltés…");
                return;
            }
            try
            {
                await SyncCoreAsync(manual);
            }
            catch (Exception ex)
            {
                Log.Error("Feltöltési hiba: " + ex);
                if (!manual)
                    _lastAutomaticFailureUtc = DateTime.UtcNow;
                SetStatus("Váratlan hiba a feltöltés közben: " + ex.Message);
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
                StatusChanged?.Invoke();
            }
        }

        private async Task SyncCoreAsync(bool manual)
        {
            var token = TokenStore.Unprotect(_settings.ProtectedToken);
            if (token == null)
            {
                if (manual)
                    SetStatus("Előbb kösd össze az alkalmazást a Hearthstone Hungary profiloddal.");
                return;
            }
            if (!manual)
            {
                if (!_settings.AutoUpload)
                    return;
                if (DateTime.UtcNow - _lastAutomaticFailureUtc < RetryDelayAfterFailure)
                    return;
            }

            if (manual)
                SetStatus("Gyűjtemény kiolvasása…");
            var snapshot = await Task.Run(() => _reader.ReadCollection());
            if (snapshot == null)
            {
                if (manual)
                    SetStatus(_reader.State == GameState.NotRunning
                        ? "Nem fut a Hearthstone. Indítsd el, és várd meg a főmenüt."
                        : "Nem sikerült kiolvasni a gyűjteményt. Várd meg, amíg betölt a főmenü, és próbáld újra.");
                return;
            }

            var previous = _settings.FindUpload(snapshot.BattleTag);
            if (!manual && previous != null)
            {
                if (previous.Hash == snapshot.Hash && DateTime.UtcNow - previous.UploadedAtUtc < RefreshInterval)
                    return;
                if (snapshot.TotalCards < previous.TotalCards * SuspiciousDropRatio)
                {
                    Log.Warn($"Automatikus feltöltés kihagyva: {snapshot.TotalCards} lap az előző {previous.TotalCards} helyett.");
                    SetStatus($"A kiolvasott gyűjtemény gyanúsan kicsi ({snapshot.TotalCards} lap, legutóbb {previous.TotalCards}), ezért nem töltöttem fel. Ha ez így jó, használd a „Feltöltés most” gombot.");
                    return;
                }
            }

            SetStatus("Feltöltés…");
            var result = await HshuApiClient.UploadAsync(token, snapshot);
            switch (result.Status)
            {
                case ApiStatus.Ok:
                    var changed = previous == null || previous.Hash != snapshot.Hash;
                    var shown = result.Cards ?? snapshot.TotalCards;
                    _settings.RememberUpload(snapshot.BattleTag, snapshot.Hash, snapshot.TotalCards, shown);
                    _lastLinkCheckUtc = DateTime.UtcNow; // a sikeres feltöltés is igazolja, hogy él az összekötés
                    if (!string.IsNullOrEmpty(result.UserName))
                        _settings.LinkedUserName = result.UserName;
                    _settings.Save();
                    Log.Info($"Gyűjtemény feltöltve: {snapshot.BattleTag}, {snapshot.TotalCards} kiolvasott, {shown} mentett lap. {result.Message}");
                    SetStatus(DescribeUpload(DateTime.UtcNow, shown, snapshot.BattleTag));
                    if (changed)
                        Uploaded?.Invoke(result.Message);
                    break;
                case ApiStatus.InvalidToken:
                    OnTokenRejected(result.Message);
                    break;
                default:
                    if (!manual)
                        _lastAutomaticFailureUtc = DateTime.UtcNow;
                    Log.Warn($"Sikertelen feltöltés ({result.Status}): {result.Message}");
                    SetStatus("Sikertelen feltöltés: " + result.Message);
                    break;
            }
        }

        private static string DescribeUpload(DateTime uploadedAtUtc, int totalCards, string battleTag) =>
            $"Utolsó feltöltés: {uploadedAtUtc.ToLocalTime():yyyy.MM.dd. HH:mm} – {totalCards} lap" +
            (string.IsNullOrEmpty(battleTag) ? "" : $" ({battleTag})");

        private void SetStatus(string text)
        {
            StatusText = text ?? "";
            StatusChanged?.Invoke();
        }
    }
}
