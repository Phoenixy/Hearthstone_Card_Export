using System;
using System.IO;
using Microsoft.Win32;

namespace HearthstoneHungary.Core
{
    /// <summary>
    /// A telepítő (installer\HearthstoneHungary.iss) Eltávolítás bejegyzése a Windows Programok listájában.
    /// Az önfrissítés a fájlokat cseréli, ezért itt írjuk át a verziót is, különben a lista a telepítéskorit mutatná.
    /// </summary>
    internal static class InstallerRegistry
    {
        // Az Inno Setup az AppId-ből ({{9F6C…} → {9F6C…}) + "_is1" néven hozza létre; ha az .iss-ben változik az AppId, ezt is át kell írni.
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{9F6C4B2A-7E1D-4C8A-B3F5-2A8D6E1C4B90}_is1";

        /// <summary>Csak akkor ír, ha a telepített példány frissült (a hordozható, zipes változatnál nincs ilyen bejegyzés, vagy máshová mutat).</summary>
        public static void UpdateDisplayVersion(string appDir, Version version)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey, writable: true))
                {
                    if (key == null || !(key.GetValue("InstallLocation") is string location) || !SameDirectory(location, appDir))
                        return;
                    var text = version.ToString(3);
                    key.SetValue("DisplayVersion", text);
                    Log.Info("A Programok listájában a verzió frissítve: " + text);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Nem sikerült frissíteni a verziót a Programok listájában: " + ex.Message);
            }
        }

        private static bool SameDirectory(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
