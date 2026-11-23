using System;
using System.Security.Cryptography;
using System.Text;

namespace HearthstoneHungary.Core
{
    /// <summary>
    /// A HSHU tokent a Windows saját titkosításával (DPAPI) mentjük, így a beállítás fájlból
    /// csak ugyanaz a Windows felhasználó tudja visszafejteni.
    /// </summary>
    internal static class TokenStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HearthstoneHungary-app");

        public const int MinLength = 20;
        public const int MaxLength = 200;

        public static string Protect(string token)
        {
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }

        /// <returns>A token, vagy null, ha nincs mentve vagy nem fejthető vissza (pl. másik gépről másolt beállítás).</returns>
        public static string Unprotect(string protectedToken)
        {
            if (string.IsNullOrEmpty(protectedToken))
                return null;
            try
            {
                var data = ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Csak formai ellenőrzés, hogy ne küldjünk el véletlenül bemásolt szöveget.</summary>
        public static bool LooksValid(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length < MinLength || token.Length > MaxLength)
                return false;
            foreach (var c in token)
            {
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';
                if (!ok)
                    return false;
            }
            return true;
        }
    }
}
