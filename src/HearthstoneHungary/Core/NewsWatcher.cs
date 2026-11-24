using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HearthstoneHungary.Core
{
    internal sealed class NewsItem
    {
        public int Id;
        public string Title;
        public string Url;
        public DateTime DateUtc;
    }

    /// <summary>
    /// Új hírek figyelése a hearthstonehungary.hu-n (api/app/news.php: a legfrissebb cikkek címe és linkje).
    /// Az első ellenőrzés csak megjegyzi a legújabb cikket; utána az annál újabbakról szól.
    /// </summary>
    internal sealed class NewsWatcher
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

        /// <summary>Ennél régebbi cikkről nem szólunk (pl. ha a gép napokig ki volt kapcsolva), csak megjegyezzük.</summary>
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(3);

        private readonly AppSettings _settings;
        private DateTime _lastCheckUtc = DateTime.MinValue;
        private int _busy;

        public NewsWatcher(AppSettings settings)
        {
            _settings = settings;
        }

        /// <summary>Új hír jelent meg: a legújabb cikk, és hogy rajta kívül hány új van még. Bármelyik szálról jöhet.</summary>
        public event Action<NewsItem, int> NewsFound;

        public bool IsCheckDue => DateTime.UtcNow - _lastCheckUtc >= CheckInterval;

        /// <summary>Bekapcsoláskor innen számítanak újnak a hírek (a kikapcsolt idő alatt megjelentekről nem szólunk).</summary>
        public void ResetBaseline()
        {
            _settings.LastNewsDateUtc = DateTime.UtcNow;
            _settings.LastNewsId = 0;
            _settings.Save();
        }

        public async Task CheckAsync()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1)
                return;
            try
            {
                _lastCheckUtc = DateTime.UtcNow;
                var items = await FetchAsync();
                if (items.Count == 0)
                    return;

                var newest = items[0];
                var lastDate = _settings.LastNewsDateUtc;
                var lastId = _settings.LastNewsId;
                var first = lastDate == default(DateTime);
                var fresh = first
                    ? new List<NewsItem>()
                    : items.Where(i => (i.DateUtc > lastDate || (i.DateUtc == lastDate && i.Id > lastId))
                                       && DateTime.UtcNow - i.DateUtc < MaxAge).ToList();

                if (newest.DateUtc > lastDate || (newest.DateUtc == lastDate && newest.Id > lastId))
                {
                    _settings.LastNewsDateUtc = newest.DateUtc;
                    _settings.LastNewsId = newest.Id;
                    _settings.Save();
                }
                if (fresh.Count > 0)
                {
                    Log.Info($"Új hír: {fresh[0].Title} (+{fresh.Count - 1})");
                    NewsFound?.Invoke(fresh[0], fresh.Count - 1);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is Newtonsoft.Json.JsonException)
            {
                // Nem fontos: a következő körben újra megnézzük.
                Log.Info("A hírek most nem érhetők el: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log.Warn("Hiba a hírek ellenőrzésekor: " + ex);
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }

        /// <returns>A legfrissebb cikkek, a legújabb elöl.</returns>
        private static async Task<List<NewsItem>> FetchAsync()
        {
            var result = new List<NewsItem>();
            using (var response = await Http.Client.GetAsync(HshuConfig.NewsApiUrl))
            {
                response.EnsureSuccessStatusCode();
                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                if (json.Value<bool?>("ok") != true || !(json["items"] is JArray array))
                    return result;
                foreach (var item in array.OfType<JObject>())
                {
                    var title = item.Value<string>("title");
                    var url = item.Value<string>("url");
                    // Csak a saját oldalunk címét nyitjuk meg a böngészőben.
                    if (string.IsNullOrWhiteSpace(title) || url == null || !url.StartsWith(HshuConfig.SiteUrl + "/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!DateTimeOffset.TryParse(item.Value<string>("date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        continue;
                    result.Add(new NewsItem { Id = item.Value<int?>("id") ?? 0, Title = title.Trim(), Url = url, DateUtc = date.UtcDateTime });
                }
            }
            return result.OrderByDescending(i => i.DateUtc).ThenByDescending(i => i.Id).ToList();
        }
    }
}
