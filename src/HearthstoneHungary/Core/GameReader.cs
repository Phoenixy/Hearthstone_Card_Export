using System;
using System.Diagnostics;
using System.Linq;
using HackF5.UnitySpy.HearthstoneLib;

namespace HearthstoneHungary.Core
{
    internal enum GameState
    {
        /// <summary>Nem fut a Hearthstone.</summary>
        NotRunning,

        /// <summary>Fut, de a gyűjtemény még nem töltött be (indul a játék / bejelentkezés).</summary>
        Loading,

        /// <summary>A gyűjtemény kiolvasható.</summary>
        Ready
    }

    /// <summary>
    /// A Hearthstone memóriájának olvasása a Firestone UnitySpy könyvtárával (external\unityspy, MIT).
    /// Csak olvas, a játékot nem módosítja. Nem szálbiztos önmagában: minden hívás a zár alatt fut.
    /// </summary>
    internal sealed class GameReader : IDisposable
    {
        private const string ProcessName = "Hearthstone";

        /// <summary>
        /// Ha a csatlakozás után ennyi ideig sem töltött be a gyűjtemény, újracsatlakozunk. A nagyon korán (a játék ablaka előtt)
        /// létrehozott olvasó a játék még be nem töltött belső részeit jegyezheti meg, és utána sosem látná betöltöttnek a gyűjteményt.
        /// </summary>
        private static readonly TimeSpan ReattachAfter = TimeSpan.FromSeconds(90);

        private readonly object _lock = new object();
        private MindVision _mindVision;
        private int _processId;
        private DateTime _attachedUtc;

        public GameState State { get; private set; } = GameState.NotRunning;

        /// <summary>Megnézi, fut-e a játék, és szükség esetén (újra) csatlakozik hozzá.</summary>
        public GameState Refresh()
        {
            lock (_lock)
            {
                var processId = FindProcessId(out var hasWindow);
                if (processId == 0)
                {
                    Reset();
                    return State = GameState.NotRunning;
                }
                // A játék még indul (nincs ablaka): még nem csatlakozunk, hogy a félig betöltött állapotot ne jegyezzük meg.
                if (!hasWindow && _mindVision == null)
                    return State = GameState.Loading;

                if (_mindVision == null || _processId != processId)
                {
                    Reset();
                    try
                    {
                        _mindVision = new MindVision(OnLibraryMessage, inputProcessId: processId);
                        _processId = processId;
                        _attachedUtc = DateTime.UtcNow;
                        Log.Info($"Csatlakozva a Hearthstone-hoz (PID {processId}).");
                    }
                    catch (Exception ex)
                    {
                        // Induláskor a játék memóriája még nem olvasható, később újrapróbáljuk.
                        Log.Info("A Hearthstone még nem olvasható: " + ex.Message);
                        Reset();
                        return State = GameState.Loading;
                    }
                }

                bool ready;
                try
                {
                    ready = _mindVision.IsCollectionInit();
                }
                catch (Exception ex)
                {
                    Log.Warn("Hiba a gyűjtemény állapotának olvasásakor: " + ex.Message);
                    Reset();
                    return State = GameState.Loading;
                }
                if (!ready && State != GameState.Ready && DateTime.UtcNow - _attachedUtc >= ReattachAfter)
                {
                    // Még mindig nem látjuk a gyűjteményt: a következő körben új olvasóval próbáljuk.
                    Log.Info("A gyűjtemény még nem olvasható, újracsatlakozás a Hearthstone-hoz.");
                    Reset();
                    return State = GameState.Loading;
                }
                return State = ready ? GameState.Ready : GameState.Loading;
            }
        }

        /// <returns>A gyűjtemény, vagy null, ha most nem olvasható.</returns>
        public CollectionSnapshot ReadCollection()
        {
            lock (_lock)
            {
                if (Refresh() != GameState.Ready)
                    return null;
                try
                {
                    var cards = _mindVision.GetCollectionCards();
                    if (cards == null || cards.Count == 0)
                        return null;

                    string battleTag = null;
                    try
                    {
                        battleTag = _mindVision.GetAccountInfo()?.BattleTag;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("A BattleTag nem olvasható: " + ex.Message);
                    }

                    // Count / PremiumCount / DiamondCount / SignatureCount: a saját lapok, a próba (trial) lapok külön vannak.
                    var owned = cards
                        .Where(c => !string.IsNullOrEmpty(c.CardId))
                        .Select(c => new OwnedCard(c.CardId, Math.Max(0, c.Count), Math.Max(0, c.PremiumCount), Math.Max(0, c.DiamondCount), Math.Max(0, c.SignatureCount)));
                    var snapshot = new CollectionSnapshot(battleTag, owned);
                    if (snapshot.TotalCards == 0)
                        return null;
                    return snapshot;
                }
                catch (Exception ex)
                {
                    Log.Warn("Hiba a gyűjtemény kiolvasásakor: " + ex);
                    Reset();
                    return null;
                }
            }
        }

        /// <param name="hasWindow">Van-e már ablaka a játéknak (induláskor egy ideig nincs).</param>
        private static int FindProcessId(out bool hasWindow)
        {
            hasWindow = false;
            var processes = Process.GetProcessesByName(ProcessName);
            try
            {
                if (processes.Length == 0)
                    return 0;
                try
                {
                    hasWindow = processes[0].MainWindowHandle != IntPtr.Zero;
                }
                catch (Exception)
                {
                    // ha nem kérdezhető le, ne akadályozza a csatlakozást
                    hasWindow = true;
                }
                return processes[0].Id;
            }
            finally
            {
                foreach (var p in processes)
                    p.Dispose();
            }
        }

        private static void OnLibraryMessage(object sender, EventArgs e)
        {
            if (e is MessageEventArgs message && !string.IsNullOrEmpty(message.Message))
                Log.Info("[UnitySpy] " + message.Message);
        }

        private void Reset()
        {
            try
            {
                _mindVision?.Dispose();
            }
            catch
            {
                // nem baj
            }
            _mindVision = null;
            _processId = 0;
        }

        public void Dispose()
        {
            lock (_lock)
                Reset();
        }
    }
}
