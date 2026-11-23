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

        /// <summary>Itt hoz létre (és von vissza) tokent a felhasználó.</summary>
        public const string TokenPageUrl = SiteUrl + "/app";

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
