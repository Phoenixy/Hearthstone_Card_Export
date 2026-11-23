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

        public string StatusText { get; private set; } = "";

        public bool IsBusy => Volatile.Read(ref _busy) == 1;

        public bool IsLinked => TokenStore.Unprotect(_settings.ProtectedToken) != null;

        public string LinkedUserName => _settings.LinkedUserName;

        /// <summary>Token ellenőrzése a szerveren, és mentése, ha jó.</summary>
        /// <returns>true, ha sikerült összekapcsolni.</returns>
        public async Task<bool> LinkAsync(string token)
        {
            token = token?.Trim();
            if (!TokenStore.LooksValid(token))
            {
                SetStatus("Ez nem tűnik érvényes tokennek. Másold be pontosan, ahogy a Hearthstone Hungary profilodban látod.");
                return false;
            }

            SetStatus("Token ellenőrzése…");
            var result = await HshuApiClient.WhoAmIAsync(token);
            switch (result.Status)
            {
                case ApiStatus.Ok:
                    _settings.ProtectedToken = TokenStore.Protect(token);
                    _settings.LinkedUserName = result.UserName;
                    _settings.Save();
                    Log.Info($"Összekapcsolva: {result.UserName}");
                    SetStatus("Sikeres összekapcsolás." + (_reader.State == GameState.NotRunning
                        ? " A gyűjtemény a Hearthstone indítása után töltődik fel."
                        : " Feltöltés indul…"));
                    return true;
                case ApiStatus.InvalidToken:
                    SetStatus(result.Message ?? "A token érvénytelen vagy vissza lett vonva. Hozz létre újat a Hearthstone Hungary profilodban.");
                    return false;
                default:
                    SetStatus(result.Message);
                    return false;
            }
        }

        public void Unlink()
        {
            _settings.ProtectedToken = null;
            _settings.LinkedUserName = null;
            _settings.Save();
            Log.Info("Leválasztva.");
            SetStatus("Az alkalmazás le van választva a Hearthstone Hungary profilodról.");
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
                    SetStatus("Előbb kapcsold össze az alkalmazást a Hearthstone Hungary profiloddal (token).");
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
                    if (!string.IsNullOrEmpty(result.UserName))
                        _settings.LinkedUserName = result.UserName;
                    _settings.Save();
                    Log.Info($"Gyűjtemény feltöltve: {snapshot.BattleTag}, {snapshot.TotalCards} kiolvasott, {shown} mentett lap. {result.Message}");
                    SetStatus(DescribeUpload(DateTime.UtcNow, shown, snapshot.BattleTag));
                    if (changed)
                        Uploaded?.Invoke(result.Message);
                    break;
                case ApiStatus.InvalidToken:
                    // Visszavont token: töröljük, hogy ne próbálkozzunk vele újra és újra.
                    _settings.ProtectedToken = null;
                    _settings.LinkedUserName = null;
                    _settings.Save();
                    Log.Warn("A szerver elutasította a tokent, leválasztva.");
                    SetStatus("Az alkalmazás leválasztódott: " + (result.Message ?? "a token érvénytelen vagy vissza lett vonva.") + " Új tokent a Hearthstone Hungary profilodban hozhatsz létre.");
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
