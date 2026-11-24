using System;
using System.Collections.Generic;

namespace HearthstoneHungary.Core
{
    /// <summary>Egy lejátszott meccs a játék naplójából (lásd PowerLogParser, GameTracker).</summary>
    public sealed class GameRecord
    {
        /// <summary>A meccs azonosítója: a naplómappa neve + a kezdés időbélyege (SHA-1), így újraolvasáskor is ugyanaz.</summary>
        public string Uid { get; set; }

        public DateTime StartedUtc { get; set; }

        /// <summary>Másodperc.</summary>
        public int Duration { get; set; }

        /// <summary>A játék játékmódja, pl. GT_RANKED, GT_CASUAL, GT_ARENA.</summary>
        public string GameType { get; set; }

        /// <summary>FT_STANDARD, FT_WILD stb.</summary>
        public string FormatType { get; set; }

        /// <summary>"win", "loss" vagy "tie".</summary>
        public string Result { get; set; }

        /// <summary>true = a játékos kezdett, false = érmével; null, ha nem derült ki.</summary>
        public bool? First { get; set; }

        /// <summary>A hősök kártyaazonosítója a meccs elején, pl. HERO_05.</summary>
        public string PlayerHero { get; set; }

        public string OpponentHero { get; set; }

        /// <summary>A pakli a Decks.log alapján (0 / üres, ha nem találtuk meg).</summary>
        public long DeckId { get; set; }

        public string DeckName { get; set; }

        public string Deckstring { get; set; }

        public int Build { get; set; }

        /// <summary>Feltöltve a HSHU-ra (vagy végleg kihagyva, pl. túl régi).</summary>
        public bool Uploaded { get; set; }
    }

    internal static class GameInfo
    {
        private static readonly Dictionary<string, string> Classes = new Dictionary<string, string>
        {
            ["01"] = "Warrior", ["02"] = "Shaman", ["03"] = "Rogue", ["04"] = "Paladin", ["05"] = "Hunter", ["06"] = "Druid",
            ["07"] = "Warlock", ["08"] = "Mage", ["09"] = "Priest", ["10"] = "Demon Hunter", ["11"] = "Death Knight",
        };

        /// <summary>A hős kártyaazonosítójából (HERO_05, HERO_01u…) a kaszt neve; ismeretlen hősnél "?".</summary>
        public static string HeroClass(string heroCardId)
        {
            if (heroCardId != null && heroCardId.Length >= 7 && heroCardId.StartsWith("HERO_", StringComparison.Ordinal)
                && Classes.TryGetValue(heroCardId.Substring(5, 2), out var name))
                return name;
            return "?";
        }

        public static bool IsArena(string gameType) => gameType != null && gameType.IndexOf("ARENA", StringComparison.Ordinal) >= 0;

        /// <summary>Csak a Ranked, a Casual és az aréna meccseket számoljuk (Brawl, baráti meccs, kaland nem).</summary>
        public static bool IsCounted(string gameType) => gameType == "GT_RANKED" || gameType == "GT_CASUAL" || IsArena(gameType);

        /// <summary>A mód neve kiírásra, pl. "Rangsorolt · Standard", "Aréna".</summary>
        public static string ModeName(string gameType, string formatType)
        {
            if (IsArena(gameType))
                return gameType.IndexOf("UNDERGROUND", StringComparison.Ordinal) >= 0 ? "Underground aréna" : "Aréna";
            var mode = gameType == "GT_RANKED" ? "Rangsorolt" : "Casual";
            switch (formatType)
            {
                case "FT_STANDARD": return mode + " · Standard";
                case "FT_WILD": return mode + " · Wild";
                case "FT_CLASSIC": return mode + " · Classic";
                case "FT_TWIST": return mode + " · Twist";
                default: return mode;
            }
        }

        public static string ResultName(string result) => result == "win" ? "Győzelem" : result == "loss" ? "Vereség" : "Döntetlen";
    }
}
