using System;
using System.Net;
using System.Net.Http;

namespace exportalas.Internal
{
    /// <summary>Egyetlen, közösen használt HttpClient (így nem fogynak el a socketek).</summary>
    internal static class Http
    {
        private static readonly Lazy<HttpClient> LazyClient = new Lazy<HttpClient>(Create);

        public static HttpClient Client => LazyClient.Value;

        public static string UserAgent => "HSHU-Gyujtemeny-Plugin/" + ExportalasPlugin.PluginVersion;

        private static HttpClient Create()
        {
            // A HDT alapból az operációs rendszerre bízza a TLS verziót (SystemDefault), ezt nem írjuk felül.
            // Ha valami mégis régi beállításra állította, a TLS 1.2-t hozzáadjuk, mert a GitHub és a HSHU is ezt kéri.
            var protocols = ServicePointManager.SecurityProtocol;
            if (protocols != SecurityProtocolType.SystemDefault && (protocols & SecurityProtocolType.Tls12) == 0)
                ServicePointManager.SecurityProtocol = protocols | SecurityProtocolType.Tls12;

            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return client;
        }
    }
}
