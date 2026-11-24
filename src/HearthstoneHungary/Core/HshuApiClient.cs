using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HearthstoneHungary.Core
{
    internal enum ApiStatus
    {
        Ok,
        InvalidToken,
        AppOutdated,
        RateLimited,
        /// <summary>A szerver hibásnak ítélte az adatot (újraküldve sem menne át).</summary>
        BadRequest,
        ServerError,
        NetworkError
    }

    internal sealed class ApiResult
    {
        public ApiResult(ApiStatus status, string userName, string message, int? cards = null)
        {
            Status = status;
            UserName = userName;
            Message = message;
            Cards = cards;
        }

        public ApiStatus Status { get; }

        /// <summary>A HSHU felhasználónév, ha a szerver visszaküldte.</summary>
        public string UserName { get; }

        /// <summary>A szerver üzenete (magyarul), vagy a hálózati hiba leírása.</summary>
        public string Message { get; }

        /// <summary>Feltöltésnél: hány lapot mentett a szerver (csak az oldalon megjelenő lapok).</summary>
        public int? Cards { get; }
    }

    /// <summary>Összekötés indítása: a böngészőben megnyitandó oldal és a kód (a <see cref="DeviceCode"/> titok, csak a lekérdezéshez kell).</summary>
    internal sealed class LinkStart
    {
        public string DeviceCode;
        public string UserCode;
        public string VerifyUrl;
        public TimeSpan Interval;
        public TimeSpan ExpiresIn;
    }

    internal enum LinkPollStatus
    {
        /// <summary>Még nem hagyták jóvá.</summary>
        Pending,
        Approved,
        /// <summary>Elutasították, lejárt, nincs jogosultság, elérte az eszközkorlátot: újra kell kezdeni (az üzenet a szerveré).</summary>
        Failed,
        /// <summary>Hálózati vagy szerverhiba: a következő lekérdezéssel újrapróbáljuk.</summary>
        Retry
    }

    internal sealed class LinkPoll
    {
        public LinkPollStatus Status;
        public string Token;
        public string UserName;
        public string Message;
    }

    /// <summary>A hearthstonehungary.hu alkalmazás API hívásai. A formátum leírása: docs/API.md</summary>
    internal static class HshuApiClient
    {
        public const int FormatVersion = 2;

        /// <summary>A meccsek feltöltésének formátuma (api/app/games.php).</summary>
        public const int GamesFormatVersion = 1;

        /// <summary>Összekötés indítása. Hibánál null, és az <paramref name="error"/> a kiírandó üzenet.</summary>
        public static async Task<(LinkStart Start, string Error)> StartLinkAsync(string deviceId, string deviceName)
        {
            var payload = new JObject
            {
                ["action"] = "start",
                ["device_id"] = deviceId,
                ["device_name"] = deviceName,
                ["app_version"] = App.Version.ToString()
            };
            var (status, json, error) = await PostLinkAsync(payload, null).ConfigureAwait(false);
            if (error != null)
                return (null, error);
            if (status != HttpStatusCode.OK || json?.Value<bool?>("ok") != true)
                return (null, json?.Value<string>("message") ?? $"A szerver hibát adott vissza (HTTP {(int)status}).");

            var start = new LinkStart
            {
                DeviceCode = json.Value<string>("device_code"),
                UserCode = json.Value<string>("user_code"),
                VerifyUrl = json.Value<string>("verify_url"),
                Interval = TimeSpan.FromSeconds(Math.Max(2, json.Value<int?>("interval") ?? 3)),
                ExpiresIn = TimeSpan.FromSeconds(Math.Max(60, json.Value<int?>("expires_in") ?? 600))
            };
            // Csak a saját oldalunkat nyitjuk meg a böngészőben.
            if (string.IsNullOrEmpty(start.DeviceCode) || string.IsNullOrEmpty(start.UserCode)
                || start.VerifyUrl == null || !start.VerifyUrl.StartsWith(HshuConfig.SiteUrl + "/", StringComparison.OrdinalIgnoreCase))
                return (null, "A szerver hibás választ adott, próbáld újra később.");
            return (start, null);
        }

        public static async Task<LinkPoll> PollLinkAsync(string deviceCode)
        {
            var payload = new JObject { ["action"] = "poll", ["device_code"] = deviceCode };
            var (status, json, error) = await PostLinkAsync(payload, null).ConfigureAwait(false);
            if (error != null || (int)status >= 500 || json == null)
                return new LinkPoll { Status = LinkPollStatus.Retry, Message = error };

            if (status == HttpStatusCode.OK && json.Value<bool?>("ok") == true)
            {
                if (json.Value<string>("status") != "approved")
                    return new LinkPoll { Status = LinkPollStatus.Pending };
                var token = json.Value<string>("token");
                if (!TokenStore.LooksValid(token))
                    return new LinkPoll { Status = LinkPollStatus.Failed, Message = "A szerver hibás tokent küldött, kezdd újra az összekötést." };
                return new LinkPoll { Status = LinkPollStatus.Approved, Token = token, UserName = json.Value<string>("user") };
            }
            if ((int)status == 429)
                return new LinkPoll { Status = LinkPollStatus.Retry };
            return new LinkPoll { Status = LinkPollStatus.Failed, Message = json.Value<string>("message") ?? "Az összekötés nem sikerült, kezdd újra." };
        }

        /// <summary>Leválasztáskor a szerveren is visszavonjuk ennek az eszköznek a tokenjét (ha nem sikerül, a profilban lehet).</summary>
        public static async Task<bool> UnlinkAsync(string token)
        {
            var (status, json, _) = await PostLinkAsync(new JObject { ["action"] = "unlink" }, token).ConfigureAwait(false);
            return status == HttpStatusCode.OK && json?.Value<bool?>("ok") == true;
        }

        /// <summary>
        /// Pakli küldése az oldalra: a szerver egyszer használható címet ad, amit a böngészőben megnyitva a pakli létrejön
        /// (<paramref name="mode"/> = "instant"), vagy a pakli készítő nyílik meg vele ("builder").
        /// </summary>
        /// <returns>Siker esetén a megnyitandó cím az üzenetben (Status = Ok); különben a hiba.</returns>
        public static async Task<ApiResult> CreateDeckTicketAsync(string token, string mode, string deckstring, string name)
        {
            var payload = new JObject { ["action"] = "ticket", ["mode"] = mode, ["deckstring"] = deckstring, ["name"] = name };
            var (status, json, error) = await PostJsonAsync(HshuConfig.DeckApiUrl, payload, token).ConfigureAwait(false);
            if (error != null)
                return new ApiResult(ApiStatus.NetworkError, null, error);
            var message = json?.Value<string>("message");
            if (status == HttpStatusCode.OK && json?.Value<bool?>("ok") == true)
            {
                var url = json.Value<string>("url");
                // Csak a saját oldalunkat nyitjuk meg a böngészőben.
                if (url == null || !url.StartsWith(HshuConfig.SiteUrl + "/", StringComparison.OrdinalIgnoreCase))
                    return new ApiResult(ApiStatus.ServerError, null, "A szerver hibás választ adott, próbáld újra később.");
                return new ApiResult(ApiStatus.Ok, null, url);
            }
            if (status == HttpStatusCode.Unauthorized)
                return new ApiResult(ApiStatus.InvalidToken, null, message);
            if ((int)status == 429)
                return new ApiResult(ApiStatus.RateLimited, null, message ?? "Túl sok kérés, próbáld újra később.");
            return new ApiResult(ApiStatus.ServerError, null, message ?? $"A szerver hibát adott vissza (HTTP {(int)status}).");
        }

        /// <summary>Lejátszott meccsek feltöltése (legfeljebb 50 egyszerre). Egy meccs csak egyszer kerül be, ezért bátran újraküldhető.</summary>
        public static async Task<ApiResult> UploadGamesAsync(string token, IEnumerable<GameRecord> games)
        {
            var payload = new JObject
            {
                ["format"] = GamesFormatVersion,
                ["app_version"] = App.Version.ToString(),
                ["games"] = new JArray(games.Select(g => new JObject
                {
                    ["uid"] = g.Uid,
                    ["played_at"] = g.StartedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
                    ["duration"] = g.Duration,
                    ["game_type"] = g.GameType,
                    ["format_type"] = g.FormatType,
                    ["result"] = g.Result,
                    ["first"] = g.First,
                    ["player_hero"] = g.PlayerHero,
                    ["opponent_hero"] = g.OpponentHero,
                    ["deck_id"] = g.DeckId,
                    ["deck_name"] = g.DeckName,
                    ["deckstring"] = g.Deckstring,
                    ["build"] = g.Build
                }))
            };
            var (status, json, error) = await PostJsonAsync(HshuConfig.GamesApiUrl, payload, token).ConfigureAwait(false);
            if (error != null)
                return new ApiResult(ApiStatus.NetworkError, null, error);
            var message = json?.Value<string>("message");
            if (status == HttpStatusCode.OK && json?.Value<bool?>("ok") == true)
                return new ApiResult(ApiStatus.Ok, null, message);
            switch ((int)status)
            {
                case 400:
                    return new ApiResult(ApiStatus.BadRequest, null, message);
                case 401:
                    return new ApiResult(ApiStatus.InvalidToken, null, message);
                case 426:
                    return new ApiResult(ApiStatus.AppOutdated, null, message);
                case 429:
                    return new ApiResult(ApiStatus.RateLimited, null, message);
                default:
                    return new ApiResult(ApiStatus.ServerError, null, message ?? $"A szerver hibát adott vissza (HTTP {(int)status}).");
            }
        }

        private static Task<(HttpStatusCode Status, JObject Json, string Error)> PostLinkAsync(JObject payload, string token) =>
            PostJsonAsync(HshuConfig.LinkApiUrl, payload, token);

        private static async Task<(HttpStatusCode Status, JObject Json, string Error)> PostJsonAsync(string url, JObject payload, string token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                if (token != null)
                    request.Headers.Add(HshuConfig.TokenHeader, token);
                request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await Http.Client.SendAsync(request).ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return (response.StatusCode, TryParse(body), null);
                    }
                }
                catch (TaskCanceledException)
                {
                    return (0, null, "A Hearthstone Hungary nem válaszolt időben.");
                }
                catch (HttpRequestException ex)
                {
                    return (0, null, "Nem sikerült elérni a Hearthstone Hungary-t: " + (ex.InnerException?.Message ?? ex.Message));
                }
            }
        }

        /// <summary>
        /// Él-e még a token (feltöltés nélkül). Ha a profilban leválasztották az eszközt, 401 jön (InvalidToken),
        /// így az alkalmazás akkor is észreveszi, ha épp nem tölt fel semmit.
        /// </summary>
        public static async Task<ApiResult> CheckTokenAsync(string token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, HshuConfig.ApiUrl))
            {
                request.Headers.Add(HshuConfig.TokenHeader, token);
                return await SendAsync(request).ConfigureAwait(false);
            }
        }

        public static async Task<ApiResult> UploadAsync(string token, CollectionSnapshot snapshot)
        {
            var payload = new JObject
            {
                ["format"] = FormatVersion,
                ["app_version"] = App.Version.ToString(),
                ["battletag"] = snapshot.BattleTag,
                ["hash"] = snapshot.Hash,
                ["total_cards"] = snapshot.TotalCards,
                ["cards"] = new JArray(snapshot.Cards.Select(c => new JObject
                {
                    ["card_id"] = c.CardId,
                    ["normal"] = c.Normal,
                    ["golden"] = c.Golden,
                    ["diamond"] = c.Diamond,
                    ["signature"] = c.Signature
                }))
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, HshuConfig.ApiUrl))
            {
                request.Headers.Add(HshuConfig.TokenHeader, token);
                request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                return await SendAsync(request).ConfigureAwait(false);
            }
        }

        private static async Task<ApiResult> SendAsync(HttpRequestMessage request)
        {
            HttpResponseMessage response;
            try
            {
                response = await Http.Client.SendAsync(request).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return new ApiResult(ApiStatus.NetworkError, null, "A Hearthstone Hungary nem válaszolt időben.");
            }
            catch (HttpRequestException ex)
            {
                return new ApiResult(ApiStatus.NetworkError, null, "Nem sikerült elérni a Hearthstone Hungary-t: " + (ex.InnerException?.Message ?? ex.Message));
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var json = TryParse(body);
                var message = json?.Value<string>("message");
                var userName = json?.Value<string>("user");
                var ok = json?.Value<bool?>("ok") == true;

                if (response.IsSuccessStatusCode && ok)
                    return new ApiResult(ApiStatus.Ok, userName, message, json?.Value<int?>("cards"));

                switch ((int)response.StatusCode)
                {
                    case (int)HttpStatusCode.Unauthorized:
                    case (int)HttpStatusCode.Forbidden:
                        return new ApiResult(ApiStatus.InvalidToken, null, message ?? "A token érvénytelen vagy vissza lett vonva.");
                    case (int)HttpStatusCode.UpgradeRequired:
                        return new ApiResult(ApiStatus.AppOutdated, null, message ?? "Az alkalmazás túl régi, frissítsd a legújabb verzióra.");
                    case 429:
                        return new ApiResult(ApiStatus.RateLimited, null, message ?? "Túl sok feltöltés rövid időn belül, próbáld később.");
                    default:
                        return new ApiResult(ApiStatus.ServerError, null, message ?? $"A szerver hibát adott vissza (HTTP {(int)response.StatusCode}).");
                }
            }
        }

        private static JObject TryParse(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;
            try
            {
                return JObject.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
