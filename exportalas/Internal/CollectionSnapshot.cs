using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Hearthstone;

namespace exportalas.Internal
{
    /// <summary>Egy gyűjthető lap darabszámai a gyűjteményben.</summary>
    internal sealed class OwnedCard
    {
        public OwnedCard(int dbfId, string name, int normal, int golden, int diamond, int signature)
        {
            DbfId = dbfId;
            Name = name;
            Normal = normal;
            Golden = golden;
            Diamond = diamond;
            Signature = signature;
        }

        public int DbfId { get; }
        public string Name { get; }
        public int Normal { get; }
        public int Golden { get; }
        public int Diamond { get; }
        public int Signature { get; }

        /// <summary>A CSV "Golden lapok" oszlopa: arany + gyémánt + aláírt (ahogy eddig is).</summary>
        public int AllPremium => Golden + Diamond + Signature;

        public int Total => Normal + AllPremium;
    }

    /// <summary>A gyűjtemény egy pillanatképe, ahogy feltöltjük / exportáljuk.</summary>
    internal sealed class CollectionSnapshot
    {
        private CollectionSnapshot(string battleTag, List<OwnedCard> cards)
        {
            BattleTag = battleTag;
            Cards = cards;
            TotalCards = cards.Sum(c => c.Total);
            Hash = ComputeHash(battleTag, cards);
        }

        public string BattleTag { get; }

        /// <summary>Csak a gyűjthető lapok, amikből legalább 1 van; dbfId szerint rendezve.</summary>
        public IReadOnlyList<OwnedCard> Cards { get; }

        public int TotalCards { get; }

        /// <summary>SHA-256 a tartalomról: ebből látjuk, hogy változott-e a gyűjtemény az előző feltöltés óta.</summary>
        public string Hash { get; }

        /// <summary>
        /// A gyűjteményt a HDT-n keresztül olvassuk (CollectionHelpers), nem közvetlenül a HearthMirrorral.
        /// Így ha a Hearthstone vagy a HearthMirror változik, azt a HDT fejlesztői javítják, és a pluginhoz nem kell nyúlni.
        /// </summary>
        /// <param name="refresh">Igaz esetén előbb újraolvastatjuk a gyűjteményt a játékból (kézi feltöltésnél, exportnál).</param>
        /// <returns>null, ha nincs elérhető gyűjtemény (pl. nem fut a Hearthstone).</returns>
        public static async Task<CollectionSnapshot> ReadAsync(bool refresh)
        {
            if (refresh && Core.Game.IsRunning)
                await CollectionHelpers.Hearthstone.UpdateCollection();
            var collection = await CollectionHelpers.Hearthstone.GetCollection();
            return collection == null ? null : FromHdt(collection);
        }

        private static CollectionSnapshot FromHdt(Collection collection)
        {
            var cards = new List<OwnedCard>();
            foreach (var entry in collection.Cards)
            {
                if (entry.Key <= 0)
                    continue;
                if (!HearthDb.Cards.CollectibleByDbfId.TryGetValue(entry.Key, out var card))
                    continue;
                // A HDT tömbje: [normál, arany, gyémánt, aláírt, ...próba (trial) darabszámok]. A próba lapokat nem számoljuk.
                var counts = entry.Value;
                int Count(int index) => counts != null && counts.Length > index ? Math.Max(0, counts[index]) : 0;
                var owned = new OwnedCard(entry.Key, card.Name, Count(0), Count(1), Count(2), Count(3));
                if (owned.Total > 0)
                    cards.Add(owned);
            }
            cards.Sort((a, b) => a.DbfId.CompareTo(b.DbfId));
            return new CollectionSnapshot(collection.BattleTag, cards);
        }

        private static string ComputeHash(string battleTag, List<OwnedCard> cards)
        {
            var sb = new StringBuilder();
            sb.Append(battleTag).Append('\n');
            foreach (var c in cards)
                sb.Append(c.DbfId).Append(':').Append(c.Normal).Append(':').Append(c.Golden)
                  .Append(':').Append(c.Diamond).Append(':').Append(c.Signature).Append('\n');
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
