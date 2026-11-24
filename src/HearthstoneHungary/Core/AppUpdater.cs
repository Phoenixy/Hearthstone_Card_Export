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
using Newtonsoft.Json.Linq;

namespace HearthstoneHungary.Core
{
    internal enum UpdateState
    {
        Idle,
        Checking,
        UpToDate,
        /// <summary>Az új verzió a helyén van, a program újraindítása után lesz érvényes.</summary>
        Installed,
        /// <summary>Van új verzió, de nem tudtuk magunk telepíteni.</summary>
        ManualRequired,
        Failed
    }

    /// <summary>
    /// Automatikus frissítés a GitHub Release-ekből (HearthstoneHungary.zip).
    ///
    /// A futó program fájljait a Windows nem engedi felülírni, de átnevezni igen: a régi fájlok .old végűek lesznek,
    /// mellettük az újak, és a program újraindítása után már az új verzió fut. A .old fájlokat a következő indítás törli.
    /// </summary>
    internal sealed class AppUpdater
    {
        private const long MaxDownloadBytes = 20 * 1024 * 1024;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

        private readonly AppSettings _settings;
        private int _busy;

        public AppUpdater(AppSettings settings)
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

                if (release == null || release.Version <= App.Version)
                {
                    SetState(UpdateState.UpToDate, $"Az alkalmazás naprakész (v{App.Version}).");
                    return;
                }

                Log.Info($"Új verzió érhető el: v{release.Version}");
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
                InstallerRegistry.UpdateDisplayVersion(targetDir, release.Version);

                InstalledVersion = release.Version;
                Log.Info($"v{release.Version} telepítve ide: {targetDir}");
                SetState(UpdateState.Installed, $"A v{release.Version} telepítve. A program újraindítása után lesz érvényes.");
            }
            catch (Exception ex)
            {
                Log.Error("Frissítési hiba: " + ex);
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

        /// <summary>"v2.0.1" → 2.0.1 (mindig 3 tagú, hogy összevethető legyen az alkalmazás verziójával).</summary>
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
                    // Csak a fájlnevet használjuk, így a zipből semmi nem kerülhet a program mappáján kívülre.
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

            if (!files.TryGetValue(HshuConfig.ExeName, out var exe))
                throw new InvalidDataException($"A csomagban nincs {HshuConfig.ExeName}.");
            ValidateExe(exe, release.Version);
            return files;
        }

        /// <summary>Csak olyan programot telepítünk, ami tényleg ez az alkalmazás, a várt verzióval, és 64 bitesen is fut.</summary>
        private static void ValidateExe(byte[] exe, Version expected)
        {
            var temp = Path.Combine(Path.GetTempPath(), "hshu-app-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                File.WriteAllBytes(temp, exe);
                var name = AssemblyName.GetAssemblyName(temp);
                if (!string.Equals(name.Name, HshuConfig.AssemblyName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"A letöltött program nem ez az alkalmazás ({name.Name}).");
                var v = name.Version;
                if (v == null || new Version(v.Major, v.Minor, Math.Max(0, v.Build)) != expected)
                    throw new InvalidDataException($"A letöltött program verziója ({v}) nem egyezik a kiadáséval (v{expected}).");
                if (name.ProcessorArchitecture == ProcessorArchitecture.X86)
                    throw new InvalidDataException("A letöltött program x86-ra van fordítva, nem tudná olvasni a 64 bites Hearthstone-t.");
            }
            catch (BadImageFormatException)
            {
                throw new InvalidDataException("A letöltött fájl nem érvényes .NET program.");
            }
            finally
            {
                try { File.Delete(temp); } catch { /* nem baj, ha a temp fájl ott marad */ }
            }
        }

        private const string OldSuffix = ".old";

        private static void Install(string targetDir, Dictionary<string, byte[]> files)
        {
            foreach (var file in files)
            {
                var dest = Path.Combine(targetDir, file.Key);
                var temp = dest + ".new";
                File.WriteAllBytes(temp, file.Value);
                if (File.Exists(dest))
                {
                    // A futó exe-t és a betöltött DLL-eket nem lehet felülírni, de átnevezni igen.
                    var old = dest + OldSuffix;
                    if (File.Exists(old))
                        File.Delete(old);
                    File.Move(dest, old);
                }
                File.Move(temp, dest);
            }
        }

        /// <summary>Az előző frissítésből maradt .old fájlok törlése (indításkor).</summary>
        public static void CleanupOldFiles()
        {
            try
            {
                foreach (var old in Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory, "*" + OldSuffix))
                {
                    try { File.Delete(old); } catch { /* ha még fogja valami, majd legközelebb */ }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Nem sikerült törölni a régi fájlokat: " + ex.Message);
            }
        }

        /// <summary>A program mappája, ha írható; különben null (pl. Program Files alá másolva csak kézzel frissíthető).</summary>
        private static string FindInstallDirectory()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                var probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return dir;
            }
            catch (Exception ex)
            {
                Log.Warn("A program mappája nem írható, csak kézzel frissíthető: " + ex.Message);
                return null;
            }
        }

        private void SetState(UpdateState state, string text)
        {
            State = state;
            StatusText = text ?? "";
            StatusChanged?.Invoke();
        }
    }
}
