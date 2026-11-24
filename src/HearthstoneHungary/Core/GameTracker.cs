using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HearthstoneHungary.Core
{
    /// <summary>
    /// A lejátszott meccsek rögzítése a játék naplójából és feltöltése a HSHU-ra.
    /// A Power.log-ot folyamatosan követi (csak az új sorokat olvassa), a befejezett meccshez a Decks.log-ból keresi meg a paklit:
    ///
    ///   I 16:13:09.7026166 Finding Game With Deck:            (arénában: Starting Arena Game With Deck:)
    ///   I 16:13:09.7026166 ### Pakli neve
    ///   I 16:13:09.7026166 # Deck ID: 2777523599
    ///   I 16:13:09.7026166 AAECAR8E…
    /// </summary>
    internal sealed class GameTracker
    {
        private const string PowerLog = "Power.log";
        private const string DecksLog = "Decks.log";
        private const int ChunkBytes = 4 * 1024 * 1024;
        private const int UploadBatch = 50;

        /// <summary>Ennél régebbi meccset az oldal már nem fogad el, ezért fel sem küldjük.</summary>
        private static readonly TimeSpan MaxUploadAge = TimeSpan.FromDays(30);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

        private static readonly Regex FolderStamp = new Regex(@"(\d{4})_(\d{2})_(\d{2})_(\d{2})_(\d{2})_(\d{2})", RegexOptions.Compiled);
        private static readonly Regex DeckLine = new Regex(@"^[A-Z] (\d{1,2}:\d{2}:\d{2}(?:\.\d+)?) (.*)$", RegexOptions.Compiled);
        private static readonly Regex Deckstring = new Regex(@"^AA[A-Za-z0-9+/]{10,680}={0,2}$", RegexOptions.Compiled);

        private readonly AppSettings _settings;
        private readonly GameStore _store;
        private readonly object _lock = new object();
        private string _file;
        private long _offset;
        private PowerLogParser _parser;
        private DateTime _sessionStart;
        private DateTime _nextUploadUtc = DateTime.MinValue;
        private int _uploading;

        public GameTracker(AppSettings settings)
        {
            _settings = settings;
            _store = GameStore.Load();
        }

        /// <summary>Új meccs került a listába, vagy változott a feltöltés állapota. Háttérszálról jön.</summary>
        public event Action Changed;

        /// <summary>A rögzített meccsek, a legújabb elöl.</summary>
        public List<GameRecord> Games => _store.Snapshot();

        public int PendingCount => _store.PendingCount;

        /// <summary>A Power.log új sorainak feldolgozása. Háttérszálon hívandó (fájlt olvas).</summary>
        /// <param name="installDir">A futó játék mappája; ha null, a legutóbb ismert mappát használjuk (a játék bezárása előtti utolsó meccshez).</param>
        public void Refresh(string installDir)
        {
            lock (_lock)
            {
                try
                {
                    var logDir = HearthstoneLogs.FindLogDirectory(installDir ?? _settings.HearthstoneDir);
                    if (logDir == null)
                        return;
                    var file = Path.Combine(logDir, PowerLog);
                    var info = new FileInfo(file);
                    if (!info.Exists)
                        return;
                    if (file != _file || info.Length < _offset)
                    {
                        _file = file;
                        _offset = 0;
                        _sessionStart = SessionStart(logDir, info);
                        _parser = new PowerLogParser(_sessionStart, _settings.Uploads.ToArray().Select(u => u.BattleTag).Where(t => !string.IsNullOrEmpty(t)));
                    }
                    if (info.Length == _offset)
                        return;

                    var added = 0;
                    foreach (var line in ReadNewLines(file))
                    {
                        var parsed = _parser.Feed(line);
                        if (parsed != null && Record(parsed, logDir))
                            added++;
                    }
                    if (added > 0)
                        Changed?.Invoke();
                }
                catch (Exception ex)
                {
                    Log.Warn("Hiba a Power.log olvasásakor: " + ex.Message);
                }
            }
        }

        /// <summary>A fájl új, teljes sorai az előző olvasás óta (a félig kiírt utolsó sor a következő körre marad).</summary>
        private IEnumerable<string> ReadNewLines(string file)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var buffer = new byte[ChunkBytes];
                while (true)
                {
                    stream.Seek(_offset, SeekOrigin.Begin);
                    var read = 0;
                    int n;
                    while (read < buffer.Length && (n = stream.Read(buffer, read, buffer.Length - read)) > 0)
                        read += n;
                    var end = read > 0 ? Array.LastIndexOf(buffer, (byte)'\n', read - 1, read) : -1;
                    if (read == 0 || end < 0)
                    {
                        // egyetlen sor sem fér a pufferbe (ilyen hosszú sor nincs a naplóban): átugorjuk, hogy ne akadjunk el
                        if (read == buffer.Length)
                            _offset += read;
                        yield break;
                    }
                    var text = Encoding.UTF8.GetString(buffer, 0, end + 1);
                    _offset += end + 1;
                    var start = 0;
                    while (start < text.Length)
                    {
                        var stop = text.IndexOf('\n', start);
                        if (stop < 0)
                            stop = text.Length;
                        var length = stop - start;
                        if (length > 0 && text[stop - 1] == '\r')
                            length--;
                        yield return text.Substring(start, length);
                        start = stop + 1;
                    }
                    if (read < buffer.Length)
                        yield break;
                }
            }
        }

        /// <returns>true, ha a meccs új, és számolt módban játszották.</returns>
        private bool Record(ParsedGame parsed, string logDir)
        {
            if (!GameInfo.IsCounted(parsed.GameType))
            {
                Log.Info($"Meccs vége ({parsed.GameType}): ezt a módot nem számoljuk.");
                return false;
            }
            var uid = Sha1(Path.GetFileName(logDir) + "|" + parsed.StartStamp);
            if (_store.Contains(uid))
                return false;

            var deck = FindDeck(logDir, parsed.StartLocal);
            var game = new GameRecord
            {
                Uid = uid,
                StartedUtc = parsed.StartLocal.ToUniversalTime(),
                Duration = (int)Math.Min(65535, Math.Max(0, (parsed.EndLocal - parsed.StartLocal).TotalSeconds)),
                GameType = parsed.GameType,
                FormatType = parsed.FormatType,
                Result = parsed.Result,
                First = parsed.First,
                PlayerHero = parsed.PlayerHero,
                OpponentHero = parsed.OpponentHero,
                DeckId = deck?.DeckId ?? 0,
                DeckName = deck?.Name ?? "",
                Deckstring = deck?.Deckstring ?? "",
                Build = parsed.Build,
                // a nagyon régi meccs (pl. egy régi napló első beolvasása) csak a helyi listába kerül
                Uploaded = DateTime.Now - parsed.StartLocal > MaxUploadAge
            };
            if (!_store.Add(game))
                return false;
            Log.Info($"Meccs rögzítve: {GameInfo.ModeName(game.GameType, game.FormatType)}, {GameInfo.ResultName(game.Result)}, "
                + $"{GameInfo.HeroClass(game.PlayerHero)} – {GameInfo.HeroClass(game.OpponentHero)}"
                + (game.Deckstring.Length > 0 ? $", pakli: {(game.DeckName.Length > 0 ? game.DeckName : "névtelen")}" : ", pakli nélkül"));
            return true;
        }

        /// <summary>A meccshez indított pakli: a Decks.log utolsó "… Game With Deck" blokkja a meccs kezdete előtt.</summary>
        private SavedDeck FindDeck(string logDir, DateTime gameStart)
        {
            try
            {
                var file = Path.Combine(logDir, DecksLog);
                if (!File.Exists(file))
                    return null;
                return FindDeck(HearthstoneLogs.ReadAllLines(file), _sessionStart, gameStart);
            }
            catch (Exception ex)
            {
                Log.Warn("Hiba a Decks.log olvasásakor: " + ex.Message);
                return null;
            }
        }

        internal static SavedDeck FindDeck(IEnumerable<string> lines, DateTime sessionStart, DateTime gameStart)
        {
            var clock = new LogClock(sessionStart);
            // a pakli választása mindig a meccs előtt van; pár másodperc ráhagyás a két napló órájának eltérésére
            var limit = gameStart.AddSeconds(5);
            SavedDeck found = null;
            var inGameBlock = false;
            string name = null;
            long deckId = 0;

            foreach (var raw in lines)
            {
                var m = DeckLine.Match(raw ?? "");
                if (!m.Success || !TimeSpan.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out var time))
                    continue;
                if (clock.At(time) > limit)
                    break;
                var line = m.Groups[2].Value.Trim();
                if (line.StartsWith("### ", StringComparison.Ordinal) || line == "###")
                {
                    name = line.Length > 4 ? line.Substring(4).Trim() : "";
                    deckId = 0;
                }
                else if (line.StartsWith("# Deck ID:", StringComparison.OrdinalIgnoreCase))
                {
                    long.TryParse(line.Substring(10).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out deckId);
                }
                else if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    // a pakli kód megjegyzés sorai
                }
                else if (Deckstring.IsMatch(line))
                {
                    if (inGameBlock)
                        found = new SavedDeck { Name = name ?? "", DeckId = deckId, Deckstring = line };
                    name = null;
                    deckId = 0;
                }
                else if (line.EndsWith(":", StringComparison.Ordinal))
                {
                    inGameBlock = line.IndexOf("Game With Deck", StringComparison.OrdinalIgnoreCase) >= 0;
                    name = null;
                    deckId = 0;
                }
            }
            return found;
        }

        /// <summary>A játék indításának ideje a naplómappa nevéből (Hearthstone_2026_11_24_16_11_58); ha nem olvasható ki, a fájl létrehozásából.</summary>
        private static DateTime SessionStart(string logDir, FileInfo powerLog)
        {
            var m = FolderStamp.Match(Path.GetFileName(logDir) ?? "");
            if (m.Success)
            {
                try
                {
                    return new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                        int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value), DateTimeKind.Local);
                }
                catch (ArgumentException)
                {
                    // nem dátum: a fájl ideje marad
                }
            }
            return powerLog.CreationTime;
        }

        private static string Sha1(string text)
        {
            using (var sha = SHA1.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b => b.ToString("x2")));
        }

        /// <summary>A még fel nem töltött meccsek elküldése, ha az alkalmazás össze van kötve. Hiba után pár percig nem próbálkozik újra.</summary>
        public async Task UploadPendingAsync()
        {
            if (DateTime.UtcNow < _nextUploadUtc || _store.PendingCount == 0)
                return;
            var token = TokenStore.Unprotect(_settings.ProtectedToken);
            if (token == null)
                return;
            if (Interlocked.Exchange(ref _uploading, 1) == 1)
                return;
            try
            {
                while (true)
                {
                    var batch = _store.Pending(UploadBatch);
                    if (batch.Count == 0)
                        break;
                    var result = await HshuApiClient.UploadGamesAsync(token, batch);
                    if (result.Status == ApiStatus.Ok)
                    {
                        _store.MarkUploaded(batch);
                        Log.Info($"Meccsek feltöltve: {batch.Count}.");
                        Changed?.Invoke();
                        continue;
                    }
                    if (result.Status == ApiStatus.BadRequest)
                    {
                        // hibásnak ítélt adat: újraküldve sem menne át, ezért nem próbáljuk tovább (a helyi listában megmarad)
                        _store.MarkUploaded(batch);
                        Log.Warn($"A szerver nem fogadta el a meccseket ({batch.Count}): {result.Message}");
                        Changed?.Invoke();
                        continue;
                    }
                    // leválasztott eszköz (ezt az összekötés ellenőrzése kezeli), hálózati vagy szerverhiba: később újra
                    Log.Warn($"A meccsek feltöltése nem sikerült ({result.Status}): {result.Message}");
                    _nextUploadUtc = DateTime.UtcNow + RetryDelay;
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Hiba a meccsek feltöltésekor: " + ex.Message);
                _nextUploadUtc = DateTime.UtcNow + RetryDelay;
            }
            finally
            {
                Interlocked.Exchange(ref _uploading, 0);
            }
        }
    }
}
