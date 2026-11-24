namespace HearthstoneHungary.Core
{
    /// <summary>
    /// Minden külső cím egy helyen. Ha a HSHU oldalon máshova kerül a végpont, csak ezt kell átírni.
    /// A szerveroldali API leírása: docs/API.md
    /// </summary>
    internal static class HshuConfig
    {
        public const string SiteUrl = "https://hearthstonehungary.hu";

        /// <summary>GET: token ellenőrzése, POST: gyűjtemény feltöltése.</summary>
        public const string ApiUrl = SiteUrl + "/api/app/collection.php";

        /// <summary>POST: összekötés indítása / állapota (kód alapján, a böngészőben jóváhagyva), leválasztás.</summary>
        public const string LinkApiUrl = SiteUrl + "/api/app/link.php";

        /// <summary>POST: pakli küldése az oldalra (egyszer használható jegy a pakli importálóhoz).</summary>
        public const string DeckApiUrl = SiteUrl + "/api/app/deck.php";

        /// <summary>GET: a legfrissebb hírek címe és linkje (az "új hír" értesítéshez; token nélkül).</summary>
        public const string NewsApiUrl = SiteUrl + "/api/app/news.php";

        /// <summary>Az alkalmazás leírása; belépve a profil Gyűjtemény fülére visz (összekötött eszközök).</summary>
        public const string AppPageUrl = SiteUrl + "/app";

        /// <summary>A token ebben a HTTP fejlécben megy. (Az Authorization fejlécet az Apache + proxy_fcgi alapból eldobja.)</summary>
        public const string TokenHeader = "X-HSHU-Token";

        public const string GitHubRepo = "Phoenixy/Hearthstone_Card_Export";
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/" + GitHubRepo + "/releases/latest";
        public const string ReleasesPageUrl = "https://github.com/" + GitHubRepo + "/releases/latest";

        /// <summary>A GitHub Release-hez csatolt fájl neve. Ezt tölti le az automatikus frissítés.</summary>
        public const string ReleaseAssetName = "HearthstoneHungary.zip";

        public const string ExeName = "HearthstoneHungary.exe";
        public const string AssemblyName = "HearthstoneHungary";
    }
}
