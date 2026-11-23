using System;
using Microsoft.Win32;

namespace HearthstoneHungary.Core
{
    /// <summary>Indulás a Windows-szal: HKCU\Software\Microsoft\Windows\CurrentVersion\Run (nem kell hozzá rendszergazdai jog).</summary>
    internal static class WindowsStartup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "HearthstoneHungary";

        /// <summary>Ezzel a kapcsolóval indítja a Windows: ilyenkor csak a tálcán jelenünk meg.</summary>
        public const string MinimizedArg = "--minimized";

        /// <summary>A beállításnak megfelelően beírja vagy törli a bejegyzést (az aktuális exe útvonalával, ha közben áthelyezték).</summary>
        public static void Apply(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null)
                        return;
                    if (enabled)
                    {
                        var command = $"\"{System.Reflection.Assembly.GetEntryAssembly().Location}\" {MinimizedArg}";
                        if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
                            key.SetValue(ValueName, command);
                    }
                    else if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, throwOnMissingValue: false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Nem sikerült beállítani az indulást a Windows-szal: " + ex.Message);
            }
        }
    }
}
