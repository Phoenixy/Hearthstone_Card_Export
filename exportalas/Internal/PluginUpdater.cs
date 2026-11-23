using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Utility.Logging;
using Newtonsoft.Json.Linq;

namespace exportalas.Internal
{
    internal enum UpdateState
    {
        Idle,
        Checking,
        UpToDate,
        /// <summary>Az új verzió a helyén van, a HDT következő indításakor töltődik be.</summary>
        Installed,
        /// <summary>Van új verzió, de nem tudtuk magunk telepíteni.</summary>
        ManualRequired,
        Failed
    }

    /// <summary>
    /// Automatikus frissítés a GitHub Release-ekből.
    ///
    /// A HDT a pluginokat a %AppData%\HearthstoneDeckTracker\Plugins mappából indításkor átmásolja a saját
    /// telepítési mappájába, és onnan tölti be. Az AppData-s példány tehát nincs zárolva: oda írjuk az új
    /// verziót, és a HDT következő indításakor magától átmásolja és betölti.
    /// </summary>
    internal sealed class PluginUpdater
    {
        private const long MaxDownloadBytes = 20 * 1024 * 1024;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

        private readonly PluginSettings _settings;
        private int _busy;

        public PluginUpdater(PluginSettings settings)
        {
            _settings = settings;
        }

        public event Action StatusChanged;

        public UpdateState State { get; private set; } = UpdateState.Idle;

        public string StatusText { get; private set; } = "";

        public Version InstalledVersion { get; private set; }

        public bool IsCheckDue => DateTime.UtcNow - _settings.LastUpdateCheckUtc > CheckInterval;

        /// <param name="manual">Kézi ellenőrzésnél a "nincs új verzió" és a hibák is megjelennek.</param>
        public async Task CheckAndInstallAsync(bool manual)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1)
                return;
            try
            {
                if (State == UpdateState.Installed)
                {
                    StatusChanged?.Invoke();
                    return;
                }
                if (manual)
                    SetState(UpdateState.Checking, "Frissítés keresése…");

                var release = await GetLatestReleaseAsync();
                _settings.LastUpdateCheckUtc = DateTime.UtcNow;
                _settings.Save();

                if (release == null || release.Version <= ExportalasPlugin.PluginVersion)
                {
                    SetState(UpdateState.UpToDate, $"A plugin naprakész (v{ExportalasPlugin.PluginVersion}).");
                    return;
                }

                Log.Info($"[HSHU] Új verzió érhető el: v{release.Version}");
                var targetDir = FindInstallDirectory();
                if (targetDir == null || release.DownloadUrl == null)
                {
                    SetState(UpdateState.ManualRequired,
                        $"Új verzió érhető el (v{release.Version}), de nem tudom magától telepíteni. Töltsd le innen: {HshuConfig.ReleasesPageUrl}");
                    return;
                }

                if (manual)
                    SetState(UpdateState.Checking, $"v{release.Version} letöltése…");
                var files = await DownloadAndValidateAsync(release);
                Install(targetDir, files);

                InstalledVersion = release.Version;
                Log.Info($"[HSHU] v{release.Version} telepítve ide: {targetDir}");
                SetState(UpdateState.Installed, $"A v{release.Version} letöltve. A HDT újraindítása után lesz érvényes.");
            }
            catch (Exception ex)
            {
                Log.Error("[HSHU] Frissítési hiba: " + ex);
                SetState(UpdateState.Failed, manual
                    ? "Nem sikerült a frissítés: " + ex.Message
                    : "Az automatikus frissítés nem sikerült, később újrapróbálom.");
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }

        private sealed class ReleaseInfo
        {
            public Version Version;
            public string DownloadUrl;
        }

        private static async Task<ReleaseInfo> GetLatestReleaseAsync()
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, HshuConfig.LatestReleaseApiUrl))
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (var response = await Http.Client.SendAsync(request))
                {
                    // Még nincs egyetlen kiadás sem.
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        return null;
                    response.EnsureSuccessStatusCode();
                    var json = JObject.Parse(await response.Content.ReadAsStringAsync());

                    var version = ParseVersion(json.Value<string>("tag_name"));
                    if (version == null)
                        return null;
                    var asset = (json["assets"] as JArray)?
                        .OfType<JObject>()
                        .FirstOrDefault(a => string.Equals(a.Value<string>("name"), HshuConfig.ReleaseAssetName, StringComparison.OrdinalIgnoreCase));
                    return new ReleaseInfo { Version = version, DownloadUrl = asset?.Value<string>("browser_download_url") };
                }
            }
        }

        /// <summary>"v2.0.1" → 2.0.1 (mindig 3 tagú, hogy összevethető legyen a plugin verziójával).</summary>
        internal static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;
            var text = tag.Trim().TrimStart('v', 'V');
            var dash = text.IndexOf('-');
            if (dash >= 0)
                text = text.Substring(0, dash);
            if (!Version.TryParse(text, out var v))
                return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        private static async Task<Dictionary<string, byte[]>> DownloadAndValidateAsync(ReleaseInfo release)
        {
            byte[] zipBytes;
            using (var response = await Http.Client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxDownloadBytes)
                    throw new InvalidDataException("A letöltött csomag túl nagy.");
                zipBytes = await response.Content.ReadAsByteArrayAsync();
            }
            if (zipBytes.Length > MaxDownloadBytes)
                throw new InvalidDataException("A letöltött csomag túl nagy.");

            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    // Csak a fájlnevet használjuk, így a zipből semmi nem kerülhet a plugin mappáján kívülre.
                    var name = Path.GetFileName(entry.FullName);
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (entry.Length > MaxDownloadBytes)
                        throw new InvalidDataException("A csomagban túl nagy fájl van: " + name);
                    if (files.ContainsKey(name))
                        throw new InvalidDataException("A csomagban kétszer szerepel: " + name);
                    using (var stream = entry.Open())
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        files[name] = buffer.ToArray();
                    }
                }
            }

            if (!files.TryGetValue(HshuConfig.PluginDllName, out var dll))
                throw new InvalidDataException($"A csomagban nincs {HshuConfig.PluginDllName}.");
            ValidatePluginDll(dll, release.Version);
            return files;
        }

        /// <summary>Csak olyan DLL-t telepítünk, ami tényleg ez a plugin, a várt verzióval, és 64 bites HDT-ben is betölthető.</summary>
        private static void ValidatePluginDll(byte[] dll, Version expected)
        {
            var temp = Path.Combine(Path.GetTempPath(), "hshu-plugin-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                File.WriteAllBytes(temp, dll);
                var name = AssemblyName.GetAssemblyName(temp);
                if (!string.Equals(name.Name, HshuConfig.PluginAssemblyName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"A letöltött DLL nem ez a plugin ({name.Name}).");
                var v = name.Version;
                if (v == null || new Version(v.Major, v.Minor, Math.Max(0, v.Build)) != expected)
                    throw new InvalidDataException($"A letöltött DLL verziója ({v}) nem egyezik a kiadáséval (v{expected}).");
                if (name.ProcessorArchitecture == ProcessorArchitecture.X86)
                    throw new InvalidDataException("A letöltött DLL x86-ra van fordítva, a 64 bites HDT nem tudná betölteni.");
            }
            catch (BadImageFormatException)
            {
                throw new InvalidDataException("A letöltött fájl nem érvényes .NET DLL.");
            }
            finally
            {
                try { File.Delete(temp); } catch { /* nem baj, ha a temp fájl ott marad */ }
            }
        }

        private static void Install(string targetDir, Dictionary<string, byte[]> files)
        {
            foreach (var file in files)
            {
                var dest = Path.Combine(targetDir, file.Key);
                var temp = dest + ".hshu-tmp";
                File.WriteAllBytes(temp, file.Value);
                if (File.Exists(dest))
                    File.Replace(temp, dest, null);
                else
                    File.Move(temp, dest);
                // A HDT a módosítás dátuma alapján dönti el, hogy újramásolja-e a plugint.
                File.SetLastWriteTimeUtc(dest, DateTime.UtcNow);
            }
        }

        /// <summary>
        /// Megkeresi az AppData-s plugin mappában azt a mappát, ahonnan a HDT a futó példányt másolta.
        /// </summary>
        /// <returns>null, ha nem található (ilyenkor csak kézzel lehet frissíteni).</returns>
        private static string FindInstallDirectory()
        {
            var loadedPath = typeof(ExportalasPlugin).Assembly.Location;
            // Ugyanazok a mappák, mint a HDT PluginManager-ében (az osztály internal, ezért nem tudjuk közvetlenül használni).
            var sourceRoot = Path.Combine(Config.AppDataPath, "Plugins");
            if (string.IsNullOrEmpty(loadedPath) || !Directory.Exists(sourceRoot))
                return null;

            if (IsUnder(loadedPath, sourceRoot))
                return Path.GetDirectoryName(loadedPath);

            var localRoots = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins"),
                Path.GetFullPath("Plugins")
            };
            foreach (var localRoot in localRoots)
            {
                if (!IsUnder(loadedPath, localRoot))
                    continue;
                var relative = loadedPath.Substring(TrimSeparator(localRoot).Length + 1);
                var candidate = Path.Combine(sourceRoot, relative);
                if (File.Exists(candidate))
                    return Path.GetDirectoryName(candidate);
            }

            var found = Directory.EnumerateFiles(sourceRoot, HshuConfig.PluginDllName, SearchOption.AllDirectories).FirstOrDefault();
            return found == null ? null : Path.GetDirectoryName(found);
        }

        private static bool IsUnder(string path, string root) =>
            path.StartsWith(TrimSeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private static string TrimSeparator(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private void SetState(UpdateState state, string text)
        {
            State = state;
            StatusText = text ?? "";
            StatusChanged?.Invoke();
        }
    }
}
