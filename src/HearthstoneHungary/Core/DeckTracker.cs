using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HearthstoneHungary.Core
{
    /// <summary>
    /// A játékban használt paklik a Decks.log alapján. A játék ide írja a pakli nevét, azonosítóját és a kész pakli kódot
    /// (oldalpaklikkal együtt), amikor meccset indítasz vele vagy megnyitod:
    ///
    ///   D 15:02:33.1234567 Finding Game With Deck:
    ///   D 15:02:33.1234567 ### Pakli neve
    ///   D 15:02:33.1234567 # Deck ID: 1234567890
    ///   D 15:02:33.1234567 AAECAf0EBu0F…
    ///
    /// Az olvasás szándékosan megengedő: bármelyik "### név" + pakli kód páros pakli, a fejléc csak azt dönti el,
    /// hogy játszottak-e vele ("Finding Game With Deck").
    /// </summary>
    internal sealed class DeckTracker
    {
        private const string LogFileName = "Decks.log";
        private const int MaxDecks = 40;

        private static readonly Regex LinePrefix = new Regex(@"^[A-Z] \d{1,2}:\d{2}:\d{2}(?:\.\d+)? ", RegexOptions.Compiled);
        private static readonly Regex Deckstring = new Regex(@"^AA[A-Za-z0-9+/]{10,680}={0,2}$", RegexOptions.Compiled);

        private readonly AppSettings _settings;
        private readonly object _lock = new object();
        private string _lastFile;
        private long _lastLength = -1;

        public DeckTracker(AppSettings settings)
        {
            _settings = settings;
        }

        /// <summary>Változott a paklik listája. Háttérszálról jön.</summary>
        public event Action Changed;

        /// <summary>Az ismert paklik: az utoljára játszott elöl, utána a legutóbb látottak.</summary>
        public IReadOnlyList<SavedDeck> Decks => Order(_settings.Decks);

        /// <summary>A Decks.log újraolvasása, ha változott. Háttérszálon hívandó (fájlt olvas).</summary>
        /// <param name="installDir">A futó játék mappája; ha null, a legutóbb ismert mappát használjuk.</param>
        public void Refresh(string installDir)
        {
            lock (_lock)
            {
                try
                {
                    if (!string.IsNullOrEmpty(installDir) && installDir != _settings.HearthstoneDir)
                    {
                        _settings.HearthstoneDir = installDir;
                        _settings.Save();
                    }
                    var logDir = HearthstoneLogs.FindLogDirectory(installDir ?? _settings.HearthstoneDir);
                    if (logDir == null)
                        return;
                    var file = Path.Combine(logDir, LogFileName);
                    var info = new FileInfo(file);
                    if (!info.Exists || (file == _lastFile && info.Length == _lastLength))
                        return;
                    _lastFile = file;
                    _lastLength = info.Length;

                    var parsed = Parse(HearthstoneLogs.ReadAllLines(file), out var lastPlayedKey);
                    if (parsed.Count == 0)
                        return;
                    if (Merge(parsed, lastPlayedKey))
                    {
                        Log.Info($"Paklik a naplóból: {parsed.Count} ({_settings.Decks.Count} ismert).");
                        Changed?.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Hiba a Decks.log olvasásakor: " + ex.Message);
                }
            }
        }

        /// <returns>A naplóban talált paklik, a fájl sorrendjében (ugyanaz a pakli többször is szerepelhet).</returns>
        internal static List<SavedDeck> Parse(IEnumerable<string> lines, out string lastPlayedKey)
        {
            var decks = new List<SavedDeck>();
            lastPlayedKey = null;
            var played = false;
            string name = null;
            long deckId = 0;

            foreach (var raw in lines)
            {
                var line = LinePrefix.Replace(raw ?? "", "").Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("### ", StringComparison.Ordinal))
                {
                    name = line.Substring(4).Trim();
                    deckId = 0;
                }
                else if (line.StartsWith("# Deck ID:", StringComparison.OrdinalIgnoreCase))
                {
                    long.TryParse(line.Substring(10).Trim(), out deckId);
                }
                else if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    // a pakli kód többi megjegyzés sora (# Class: …, # Format: …, lapok listája)
                }
                else if (Deckstring.IsMatch(line))
                {
                    var deck = new SavedDeck
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? "Névtelen pakli" : name,
                        DeckId = deckId,
                        Deckstring = line
                    };
                    decks.Add(deck);
                    if (played)
                        lastPlayedKey = deck.Key;
                    name = null;
                    deckId = 0;
                }
                else if (line.EndsWith(":", StringComparison.Ordinal))
                {
                    // új blokk fejléce
                    played = line.StartsWith("Finding Game With Deck", StringComparison.OrdinalIgnoreCase);
                    name = null;
                    deckId = 0;
                }
            }
            return decks;
        }

        /// <summary>A most látott paklik beolvasztása az ismertek közé. A beállításbeli listát mindig újra cseréljük, soha nem módosítjuk helyben.</summary>
        private bool Merge(List<SavedDeck> parsed, string lastPlayedKey)
        {
            var now = DateTime.UtcNow;
            var known = _settings.Decks.ToDictionary(d => d.Key, d => d.Clone());
            var before = Signature(Order(_settings.Decks));

            foreach (var deck in parsed)
            {
                known.TryGetValue(deck.Key, out var old);
                deck.SeenUtc = now;
                deck.PlayedUtc = old?.PlayedUtc ?? default(DateTime);
                known[deck.Key] = deck;
            }
            if (lastPlayedKey != null && known.TryGetValue(lastPlayedKey, out var last))
            {
                // csak akkor kap új időt, ha nem már ez a legutóbb játszott pakli (különben minden olvasás átírná)
                var newest = known.Values.OrderByDescending(d => d.PlayedUtc).First();
                if (newest.Key != last.Key || last.PlayedUtc == default(DateTime))
                    last.PlayedUtc = now;
            }

            var merged = Order(known.Values).Take(MaxDecks).ToList();
            if (Signature(merged) == before)
                return false;
            _settings.Decks = merged;
            _settings.Save();
            return true;
        }

        private static List<SavedDeck> Order(IEnumerable<SavedDeck> decks) =>
            decks.OrderByDescending(d => d.PlayedUtc).ThenByDescending(d => d.SeenUtc).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        private static string Signature(IEnumerable<SavedDeck> decks) =>
            string.Join("|", decks.Select(d => d.Key + "/" + d.Name + "/" + d.Deckstring));
    }
}
