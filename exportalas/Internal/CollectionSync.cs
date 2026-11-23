using System;
using System.Threading;
using System.Threading.Tasks;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Utility.Logging;

namespace exportalas.Internal
{
    internal enum SyncTrigger
    {
        /// <summary>A HDT jelezte, hogy változhatott a gyűjtemény. Csak akkor töltünk fel, ha tényleg változott.</summary>
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

        private readonly PluginSettings _settings;
        private int _busy;
        private DateTime _lastAutomaticFailureUtc = DateTime.MinValue;

        public CollectionSync(PluginSettings settings)
        {
            _settings = settings;
            var last = settings.LastUpload;
            if (last != null)
                StatusText = DescribeUpload(last.UploadedAtUtc, last.TotalCards, last.BattleTag);
        }

        /// <summary>Akkor hívódik, ha változott a státusz szöveg vagy az összekapcsolás. Bármelyik szálról jöhet.</summary>
        public event Action StatusChanged;

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
                    Log.Info($"[HSHU] Összekapcsolva: {result.UserName}");
                    SetStatus("Sikeres összekapcsolás." + (Core.Game.IsRunning ? " Feltöltés indul…" : " A gyűjtemény a Hearthstone indítása után töltődik fel."));
                    return true;
                case ApiStatus.InvalidToken:
                    SetStatus("A token érvénytelen vagy vissza lett vonva. Hozz létre újat a Hearthstone Hungary profilodban.");
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
            Log.Info("[HSHU] Leválasztva.");
            SetStatus("A plugin le van választva a Hearthstone Hungary profilodról.");
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
                Log.Error("[HSHU] Feltöltési hiba: " + ex);
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
                    SetStatus("Előbb kapcsold össze a plugint a Hearthstone Hungary profiloddal (token).");
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
            var snapshot = await CollectionSnapshot.ReadAsync(refresh: manual);
            if (snapshot == null)
            {
                if (manual)
                    SetStatus(Core.Game.IsRunning
                        ? "Nem sikerült kiolvasni a gyűjteményt. Várd meg, amíg betölt a főmenü, és próbáld újra."
                        : "Nem fut a Hearthstone. Indítsd el, és várd meg a főmenüt.");
                return;
            }

            var previous = _settings.FindUpload(snapshot.BattleTag);
            if (!manual && previous != null)
            {
                if (previous.Hash == snapshot.Hash)
                    return;
                if (snapshot.TotalCards < previous.TotalCards * SuspiciousDropRatio)
                {
                    Log.Warn($"[HSHU] Automatikus feltöltés kihagyva: {snapshot.TotalCards} lap az előző {previous.TotalCards} helyett.");
                    SetStatus($"A kiolvasott gyűjtemény gyanúsan kicsi ({snapshot.TotalCards} lap, legutóbb {previous.TotalCards}), ezért nem töltöttem fel. Ha ez így jó, használd a „Feltöltés most” gombot.");
                    return;
                }
            }

            SetStatus("Feltöltés…");
            var result = await HshuApiClient.UploadAsync(token, snapshot);
            switch (result.Status)
            {
                case ApiStatus.Ok:
                    _settings.RememberUpload(snapshot.BattleTag, snapshot.Hash, snapshot.TotalCards);
                    if (!string.IsNullOrEmpty(result.UserName))
                        _settings.LinkedUserName = result.UserName;
                    _settings.Save();
                    Log.Info($"[HSHU] Gyűjtemény feltöltve: {snapshot.BattleTag}, {snapshot.TotalCards} lap.");
                    SetStatus(DescribeUpload(DateTime.UtcNow, snapshot.TotalCards, snapshot.BattleTag));
                    break;
                case ApiStatus.InvalidToken:
                    // Visszavont token: töröljük, hogy ne próbálkozzunk vele újra és újra.
                    _settings.ProtectedToken = null;
                    _settings.LinkedUserName = null;
                    _settings.Save();
                    Log.Warn("[HSHU] A szerver elutasította a tokent, leválasztva.");
                    SetStatus("A token érvénytelen vagy vissza lett vonva, ezért a plugin leválasztódott. Hozz létre új tokent a Hearthstone Hungary profilodban.");
                    break;
                default:
                    if (!manual)
                        _lastAutomaticFailureUtc = DateTime.UtcNow;
                    Log.Warn($"[HSHU] Sikertelen feltöltés ({result.Status}): {result.Message}");
                    SetStatus("Sikertelen feltöltés: " + result.Message);
                    break;
            }
        }

        private static string DescribeUpload(DateTime uploadedAtUtc, int totalCards, string battleTag) =>
            $"Utolsó feltöltés: {uploadedAtUtc.ToLocalTime():yyyy.MM.dd. HH:mm} – {totalCards} lap ({battleTag})";

        private void SetStatus(string text)
        {
            StatusText = text ?? "";
            StatusChanged?.Invoke();
        }
    }
}
