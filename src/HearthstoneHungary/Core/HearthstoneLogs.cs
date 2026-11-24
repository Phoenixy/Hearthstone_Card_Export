using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace HearthstoneHungary.Core
{
    /// <summary>
    /// A Hearthstone naplófájljai. A játék csak akkor ír naplót, ha a %LocalAppData%\Blizzard\Hearthstone\log.config
    /// fájlban be van kapcsolva az adott rész; ezt (mint a Deck Tracker és a Firestone) mi kapcsoljuk be.
    /// A naplók a játék mappájában vannak: Logs\Hearthstone_ÉÉÉÉ_HH_NN_ÓÓ_PP_MM\*.log (indításonként új mappa).
    /// </summary>
    internal static class HearthstoneLogs
    {
        private const string ProcessName = "Hearthstone";

        /// <summary>Decks: a meccshez választott pakli kódja. Power: a meccs eseményei (eredmény, hősök). LoadingScreen, Arena: játékmód, aréna.</summary>
        private static readonly string[] Sections = { "Decks", "Power", "LoadingScreen", "Arena" };

        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blizzard", "Hearthstone", "log.config");

        /// <summary>
        /// Bekapcsolja a szükséges naplókat, a fájl többi részét (más programok beállításait) érintetlenül hagyja.
        /// </summary>
        /// <returns>true, ha módosítani kellett a fájlt (a játék csak újraindítás után veszi figyelembe).</returns>
        public static bool EnsureLoggingEnabled()
        {
            try
            {
                var path = ConfigPath;
                var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
                var changed = false;
                foreach (var section in Sections)
                {
                    changed |= SetValue(lines, section, "LogLevel", "1");
                    changed |= SetValue(lines, section, "FilePrinting", "True");
                    changed |= SetValue(lines, section, "ConsolePrinting", "False", onlyIfMissing: true);
                    changed |= SetValue(lines, section, "ScreenPrinting", "False", onlyIfMissing: true);
                    // A Power naplóban a részletes mód kell (a lapok és a hősök azonosítójához).
                    if (section == "Power")
                        changed |= SetValue(lines, section, "Verbose", "True");
                }
                if (!changed)
                    return false;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, lines, new UTF8Encoding(false));
                Log.Info("A Hearthstone naplózása bekapcsolva: " + path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Nem sikerült bekapcsolni a Hearthstone naplózását: " + ex.Message);
                return false;
            }
        }

        /// <summary>Egy [szakasz] kulcs=érték sorának beállítása; a szakaszt és a kulcsot létrehozza, ha nincs.</summary>
        private static bool SetValue(List<string> lines, string section, string key, string value, bool onlyIfMissing = false)
        {
            var start = lines.FindIndex(l => string.Equals(l.Trim(), "[" + section + "]", StringComparison.OrdinalIgnoreCase));
            if (start < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0)
                    lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(key + "=" + value);
                return true;
            }

            var end = start + 1;
            while (end < lines.Count && !lines[end].TrimStart().StartsWith("[", StringComparison.Ordinal))
                end++;
            for (var i = start + 1; i < end; i++)
            {
                var eq = lines[i].IndexOf('=');
                if (eq <= 0 || !string.Equals(lines[i].Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (onlyIfMissing || string.Equals(lines[i].Substring(eq + 1).Trim(), value, StringComparison.OrdinalIgnoreCase))
                    return false;
                lines[i] = key + "=" + value;
                return true;
            }

            // A szakasz utolsó nem üres sora után szúrjuk be.
            var insert = end;
            while (insert > start + 1 && lines[insert - 1].Trim().Length == 0)
                insert--;
            lines.Insert(insert, key + "=" + value);
            return true;
        }

        /// <summary>A futó játék mappája, vagy null, ha nem fut (vagy nem olvasható ki).</summary>
        public static string FindInstallDirectory()
        {
            var processes = Process.GetProcessesByName(ProcessName);
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(process.MainModule.FileName);
                        if (!string.IsNullOrEmpty(dir))
                            return dir;
                    }
                    catch (Exception ex)
                    {
                        Log.Info("A Hearthstone mappája nem olvasható ki: " + ex.Message);
                    }
                }
                return null;
            }
            finally
            {
                foreach (var p in processes)
                    p.Dispose();
            }
        }

        /// <summary>A legutóbbi játékindítás naplómappája, vagy null, ha még nincs.</summary>
        public static string FindLogDirectory(string installDir)
        {
            try
            {
                if (string.IsNullOrEmpty(installDir))
                    return null;
                var logs = Path.Combine(installDir, "Logs");
                if (!Directory.Exists(logs))
                    return null;
                // A mappanév időbélyege ábécérendben is időrendet ad.
                var latest = Directory.GetDirectories(logs, "Hearthstone_*")
                    .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                    .FirstOrDefault();
                return latest ?? logs;
            }
            catch (Exception ex)
            {
                Log.Info("A Hearthstone naplómappája nem olvasható: " + ex.Message);
                return null;
            }
        }

        /// <summary>Naplófájl beolvasása úgy, hogy a játék közben tovább írhatja.</summary>
        public static List<string> ReadAllLines(string path)
        {
            var lines = new List<string>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    lines.Add(line);
            }
            return lines;
        }
    }
}
