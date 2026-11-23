using System.IO;
using System.Linq;
using System.Text;

namespace exportalas.Internal
{
    /// <summary>
    /// Ugyanazt a CSV-t írja, mint az 1.x verziók (a HSHU feltöltő ezt várja):
    /// Id (dbfId), Név, Normal lapok, Golden lapok (arany + gyémánt + aláírt). Minden gyűjthető lap szerepel, 0 darabbal is.
    /// </summary>
    internal static class CsvExporter
    {
        public static void Write(string path, CollectionSnapshot snapshot)
        {
            var owned = snapshot.Cards.ToDictionary(c => c.DbfId);
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.NewLine = "\r\n";
                writer.WriteLine("Id,Név,Normal lapok,Golden lapok");
                foreach (var card in HearthDb.Cards.Collectible.Values)
                {
                    owned.TryGetValue(card.DbfId, out var counts);
                    writer.Write(card.DbfId);
                    writer.Write(',');
                    writer.Write(Escape(card.Name));
                    writer.Write(',');
                    writer.Write(counts?.Normal ?? 0);
                    writer.Write(',');
                    writer.Write(counts?.AllPremium ?? 0);
                    writer.WriteLine();
                }
            }
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            var needsQuotes = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || value.Trim() != value;
            return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
