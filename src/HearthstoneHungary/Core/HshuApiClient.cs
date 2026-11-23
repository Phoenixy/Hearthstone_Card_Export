using System;
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

    /// <summary>A hearthstonehungary.hu alkalmazás API hívásai. A formátum leírása: docs/API.md</summary>
    internal static class HshuApiClient
    {
        public const int FormatVersion = 2;

        /// <summary>Token ellenőrzése feltöltés nélkül.</summary>
        public static async Task<ApiResult> WhoAmIAsync(string token)
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
