using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HearthstoneHungary.Core
{
    /// <summary>Egy lap darabszámai a gyűjteményben (a próba / trial lapok nélkül).</summary>
    internal sealed class OwnedCard
    {
        public OwnedCard(string cardId, int normal, int golden, int diamond, int signature)
        {
            CardId = cardId;
            Normal = normal;
            Golden = golden;
            Diamond = diamond;
            Signature = signature;
        }

        /// <summary>A játék kártyaazonosítója, pl. CS2_029. A HSHU ebből keresi ki a lapot (jsweb_cards.image).</summary>
        public string CardId { get; }
        public int Normal { get; }
        public int Golden { get; }
        public int Diamond { get; }
        public int Signature { get; }

        public int Total => Normal + Golden + Diamond + Signature;
    }

    /// <summary>A gyűjtemény egy pillanatképe, ahogy feltöltjük.</summary>
    internal sealed class CollectionSnapshot
    {
        public CollectionSnapshot(string battleTag, IEnumerable<OwnedCard> cards)
        {
            BattleTag = string.IsNullOrWhiteSpace(battleTag) ? "" : battleTag.Trim();
            var list = cards.Where(c => c.Total > 0).ToList();
            list.Sort((a, b) => string.CompareOrdinal(a.CardId, b.CardId));
            Cards = list;
            TotalCards = list.Sum(c => c.Total);
            Hash = ComputeHash(BattleTag, list);
        }

        public string BattleTag { get; }

        /// <summary>Csak a lapok, amikből legalább 1 van; kártyaazonosító szerint rendezve.</summary>
        public IReadOnlyList<OwnedCard> Cards { get; }

        public int TotalCards { get; }

        /// <summary>SHA-256 a tartalomról: ebből látjuk, hogy változott-e a gyűjtemény az előző feltöltés óta.</summary>
        public string Hash { get; }

        private static string ComputeHash(string battleTag, List<OwnedCard> cards)
        {
            var sb = new StringBuilder();
            sb.Append(battleTag).Append('\n');
            foreach (var c in cards)
                sb.Append(c.CardId).Append(':').Append(c.Normal).Append(':').Append(c.Golden)
                  .Append(':').Append(c.Diamond).Append(':').Append(c.Signature).Append('\n');
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
