using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace HearthstoneHungary.Core
{
    /// <summary>
    /// A naplósorok órája (a naplóban csak óra:perc:másodperc van) dátummal együtt: a kezdőnaptól indul, és éjfél után a következő napra lép.
    /// </summary>
    internal sealed class LogClock
    {
        private static readonly TimeSpan Rollover = TimeSpan.FromHours(12);
        private readonly DateTime _startDate;
        private TimeSpan _last;
        private int _days;

        /// <param name="sessionStart">A játék indításának helyi ideje (a naplómappa nevéből).</param>
        public LogClock(DateTime sessionStart)
        {
            _startDate = sessionStart.Date;
            _last = sessionStart.TimeOfDay;
        }

        /// <summary>A sor helyi ideje. A sorokat időrendben kell adni.</summary>
        public DateTime At(TimeSpan timeOfDay)
        {
            if (timeOfDay < _last - Rollover)
                _days++;
            _last = timeOfDay;
            return _startDate.AddDays(_days) + timeOfDay;
        }
    }

    /// <summary>A Power.log egy befejezett meccse, még pakli nélkül.</summary>
    internal sealed class ParsedGame
    {
        /// <summary>A CREATE_GAME sor időbélyege, ahogy a naplóban áll (a meccs azonosítójához).</summary>
        public string StartStamp;
        public DateTime StartLocal;
        public DateTime EndLocal;
        public string GameType;
        public string FormatType;
        public int Build;
        public string Result;
        public bool? First;
        public string PlayerHero;
        public string OpponentHero;
        public string PlayerName;
    }

    /// <summary>
    /// A Hearthstone Power.log olvasása: a meccs módja, a két hős és az eredmény. Csak a GameState sorait nézi
    /// (a PowerTaskList ugyanezt késleltetve megismétli):
    ///
    ///   D 16:13:34.8693380 GameState.DebugPrintPower() - CREATE_GAME
    ///   D 16:13:34.8693380 GameState.DebugPrintPower() -     Player EntityID=2 PlayerID=1 GameAccountId=[hi=… lo=…]
    ///   D 16:13:34.8693380 GameState.DebugPrintPower() -         tag=HERO_ENTITY value=68
    ///   D 16:13:34.8693380 GameState.DebugPrintPower() - FULL_ENTITY - Creating ID=68 CardID=HERO_01u
    ///   D 16:13:34.8693380 GameState.DebugPrintGame() - GameType=GT_RANKED
    ///   D 16:13:34.8693380 GameState.DebugPrintGame() - PlayerID=1, PlayerName=UNKNOWN HUMAN PLAYER
    ///   D 16:13:34.8693380 GameState.DebugPrintGame() - PlayerID=2, PlayerName=Név#1234
    ///   D 16:21:14.6276173 GameState.DebugPrintPower() - TAG_CHANGE Entity=Név#1234 tag=PLAYSTATE value=LOST
    ///   D 16:21:14.6276173 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE
    ///
    /// A saját játékos az, akinek a neve a meccs elején ismert (az ellenfélé ekkor még "UNKNOWN HUMAN PLAYER").
    /// </summary>
    internal sealed class PowerLogParser
    {
        private const string UnknownPlayer = "UNKNOWN HUMAN PLAYER";

        private static readonly Regex Line = new Regex(
            @"^[A-Z] (\d{1,2}:\d{2}:\d{2}(?:\.\d+)?) GameState\.DebugPrint(Power|Game)\(\) - (.*)$", RegexOptions.Compiled);
        private static readonly Regex PlayerEntity = new Regex(@"^Player EntityID=(\d+) PlayerID=(\d+)", RegexOptions.Compiled);
        private static readonly Regex FullEntity = new Regex(@"^FULL_ENTITY - Creating ID=(\d+) CardID=(\S*)", RegexOptions.Compiled);
        private static readonly Regex HeroEntity = new Regex(@"^tag=HERO_ENTITY value=(\d+)", RegexOptions.Compiled);
        private static readonly Regex TagChange = new Regex(@"^TAG_CHANGE Entity=(.+) tag=(\w+) value=(\w+)", RegexOptions.Compiled);
        private static readonly Regex PlayerName = new Regex(@"^PlayerID=(\d+), PlayerName=(.*)$", RegexOptions.Compiled);

        private readonly LogClock _clock;
        private readonly HashSet<string> _knownNames;
        private Game _game;
        private int _blockPlayer;
        private bool _spectating;

        /// <param name="knownPlayerNames">A saját BattleTag(ek), ha ismert: újracsatlakozás után a napló mindkét nevet kiírja, ilyenkor ebből derül ki, melyik a saját.</param>
        public PowerLogParser(DateTime sessionStart, IEnumerable<string> knownPlayerNames = null)
        {
            _clock = new LogClock(sessionStart);
            _knownNames = new HashSet<string>(knownPlayerNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        private sealed class Game
        {
            public string StartStamp;
            public DateTime StartLocal;
            public string GameType;
            public string FormatType;
            public int Build;
            public readonly Dictionary<string, int> EntityPlayer = new Dictionary<string, int>();
            public readonly Dictionary<int, string> HeroEntity = new Dictionary<int, string>();
            public readonly Dictionary<string, string> EntityCard = new Dictionary<string, string>();
            public readonly Dictionary<int, string> Names = new Dictionary<int, string>();
            public readonly List<KeyValuePair<string, string>> PlayStates = new List<KeyValuePair<string, string>>();
            public string FirstEntity;
        }

        /// <summary>Egy naplósor feldolgozása. Befejezett (és értelmezhető) meccsnél a meccset adja vissza, különben null-t.</summary>
        public ParsedGame Feed(string line)
        {
            if (string.IsNullOrEmpty(line))
                return null;
            if (line.IndexOf("GameState.DebugPrint", StringComparison.Ordinal) < 0)
            {
                // Mások meccsének nézése közben a napló ugyanúgy íródik: ezeket nem számoljuk.
                if (line.IndexOf("Begin Spectating", StringComparison.Ordinal) >= 0)
                {
                    _spectating = true;
                    _game = null;
                }
                else if (line.IndexOf("End Spectator", StringComparison.Ordinal) >= 0)
                {
                    _spectating = false;
                    _game = null;
                }
                return null;
            }
            var m = Line.Match(line);
            if (!m.Success || !TimeSpan.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out var time))
                return null;
            var when = _clock.At(time);
            var text = m.Groups[3].Value.Trim();

            if (m.Groups[2].Value == "Game")
            {
                ReadGameInfo(text);
                return null;
            }
            if (text == "CREATE_GAME")
            {
                _game = _spectating ? null : new Game { StartStamp = m.Groups[1].Value, StartLocal = when };
                _blockPlayer = 0;
                return null;
            }
            if (_game == null)
                return null;

            if (text.StartsWith("tag=", StringComparison.Ordinal))
            {
                Match hero;
                if (_blockPlayer > 0 && (hero = HeroEntity.Match(text)).Success)
                    _game.HeroEntity[_blockPlayer] = hero.Groups[1].Value;
                return null;
            }
            _blockPlayer = 0;

            Match match;
            if ((match = PlayerEntity.Match(text)).Success)
            {
                _blockPlayer = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                _game.EntityPlayer[match.Groups[1].Value] = _blockPlayer;
            }
            else if (text.StartsWith("FULL_ENTITY", StringComparison.Ordinal))
            {
                // csak a két kezdő hős lapja kell
                if ((match = FullEntity.Match(text)).Success && _game.HeroEntity.ContainsValue(match.Groups[1].Value)
                    && !_game.EntityCard.ContainsKey(match.Groups[1].Value))
                    _game.EntityCard[match.Groups[1].Value] = match.Groups[2].Value;
            }
            else if (text.StartsWith("TAG_CHANGE", StringComparison.Ordinal) && (match = TagChange.Match(text)).Success)
            {
                var entity = match.Groups[1].Value;
                var tag = match.Groups[2].Value;
                var value = match.Groups[3].Value;
                if (tag == "PLAYSTATE" && (value == "WON" || value == "LOST" || value == "TIED"))
                    _game.PlayStates.Add(new KeyValuePair<string, string>(entity, value));
                else if (tag == "FIRST_PLAYER" && value == "1")
                    _game.FirstEntity = entity;
                else if (tag == "STATE" && value == "COMPLETE" && (entity == "GameEntity" || entity == "1"))
                {
                    var finished = Finish(_game, when);
                    _game = null;
                    return finished;
                }
            }
            return null;
        }

        private void ReadGameInfo(string text)
        {
            if (_game == null)
                return;
            Match match;
            if (text.StartsWith("GameType=", StringComparison.Ordinal))
                _game.GameType = text.Substring(9).Trim();
            else if (text.StartsWith("FormatType=", StringComparison.Ordinal))
                _game.FormatType = text.Substring(11).Trim();
            else if (text.StartsWith("BuildNumber=", StringComparison.Ordinal))
                int.TryParse(text.Substring(12).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _game.Build);
            else if ((match = PlayerName.Match(text)).Success)
                _game.Names[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)] = match.Groups[2].Value.Trim();
        }

        private ParsedGame Finish(Game game, DateTime end)
        {
            var local = FindLocalPlayer(game);
            if (local == 0 || string.IsNullOrEmpty(game.GameType))
                return null;
            var localName = game.Names[local];

            // A saját eredmény; ha csak az ellenfélé van meg, annak a fordítottja.
            string own = null, other = null;
            foreach (var state in game.PlayStates)
            {
                if (IsPlayer(game, state.Key, local, localName))
                    own = state.Value;
                else
                    other = state.Value;
            }
            if (own == null && other != null)
                own = other == "WON" ? "LOST" : other == "LOST" ? "WON" : "TIED";
            if (own == null)
                return null;

            var opponent = game.Names.Keys.FirstOrDefault(id => id != local);
            return new ParsedGame
            {
                StartStamp = game.StartStamp,
                StartLocal = game.StartLocal,
                EndLocal = end < game.StartLocal ? game.StartLocal : end,
                GameType = game.GameType,
                FormatType = game.FormatType ?? "",
                Build = game.Build,
                Result = own == "WON" ? "win" : own == "LOST" ? "loss" : "tie",
                First = game.FirstEntity == null ? (bool?)null : IsPlayer(game, game.FirstEntity, local, localName),
                PlayerHero = HeroCard(game, local),
                OpponentHero = HeroCard(game, opponent),
                PlayerName = localName
            };
        }

        /// <summary>A saját játékos PlayerID-je, vagy 0, ha nem dönthető el (pl. gép elleni meccs: az úgysem számít).</summary>
        private int FindLocalPlayer(Game game)
        {
            if (game.Names.Count != 2)
                return 0;
            var known = game.Names.Where(n => n.Value.Length > 0 && n.Value != UnknownPlayer).ToList();
            if (known.Count == 1)
                return known[0].Key;
            var mine = known.Where(n => _knownNames.Contains(n.Value)).ToList();
            return mine.Count == 1 ? mine[0].Key : 0;
        }

        /// <summary>Az Entity= érték a megadott játékosé-e: lehet a neve vagy a játékos-entitás száma.</summary>
        private static bool IsPlayer(Game game, string entity, int player, string playerName)
        {
            if (game.EntityPlayer.TryGetValue(entity, out var id))
                return id == player;
            return string.Equals(entity, playerName, StringComparison.Ordinal);
        }

        private static string HeroCard(Game game, int player)
        {
            return game.HeroEntity.TryGetValue(player, out var entity) && game.EntityCard.TryGetValue(entity, out var card) ? card : "";
        }
    }
}
