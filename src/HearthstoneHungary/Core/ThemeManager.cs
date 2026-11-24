using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace HearthstoneHungary.Core
{
    /// <summary>
    /// Világos / sötét megjelenés a Windows beállítása szerint (Beállítások → Személyre szabás → Színek → Alkalmazás mód).
    ///
    /// A színek és a háttérkép az Application.Resources-be kerülnek, az ablak DynamicResource-szal hivatkozik rájuk,
    /// így a Windows átállításakor a nyitott ablak is azonnal átvált.
    /// </summary>
    internal static class ThemeManager
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string LightThemeValue = "AppsUseLightTheme";

        private static readonly Dictionary<string, string> LightColors = new Dictionary<string, string>
        {
            ["WindowBase"] = "#FFFFFFFF",
            ["CardBackground"] = "#EBFFFFFF",
            ["CardBorder"] = "#40427FED",
            ["HshuText"] = "#FF1F2328",
            ["HshuMuted"] = "#FF57606A",
            ["HshuBlue"] = "#FF427FED",
            ["HshuBlueDark"] = "#FF0E4262",
            ["HshuLink"] = "#FF427FED",
            ["SecondaryBackground"] = "#FF6B7280",
            ["InputBackground"] = "#FFFFFFFF",
            ["InputBorder"] = "#FFABADB3",
            ["HshuGreen"] = "#FF137333",
            ["HshuOrange"] = "#FFB06000",
        };

        // A hearthstonehungary.hu sötét témájának színei (fekete háttér, #191919 dobozok, #5C5C5C keretek).
        private static readonly Dictionary<string, string> DarkColors = new Dictionary<string, string>
        {
            ["WindowBase"] = "#FF000000",
            ["CardBackground"] = "#E6191919",
            ["CardBorder"] = "#FF333333",
            ["HshuText"] = "#FFD4D6D8",
            ["HshuMuted"] = "#FF9BA1A6",
            ["HshuBlue"] = "#FF427FED",
            ["HshuBlueDark"] = "#FF1A78A6",
            ["HshuLink"] = "#FF6EA2F7",
            ["SecondaryBackground"] = "#FF4B5563",
            ["InputBackground"] = "#FF232323",
            ["InputBorder"] = "#FF5C5C5C",
            ["HshuGreen"] = "#FF81C995",
            ["HshuOrange"] = "#FFE8A33D",
        };

        private static ImageBrush _lightImage;
        private static ImageBrush _darkImage;
        private static bool _listening;

        public static bool IsDark { get; private set; }

        /// <summary>A megjelenés megváltozott (a UI szálon).</summary>
        public static event Action Changed;

        /// <summary>Indításkor: beállítja a Windows szerinti megjelenést, és figyeli a változását.</summary>
        public static void Initialize()
        {
            Apply(ReadSystemDark());
            if (!_listening)
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                _listening = true;
            }
        }

        public static void Shutdown()
        {
            if (!_listening)
                return;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _listening = false;
        }

        /// <summary>Sötét címsor az ablaknak (Windows 10 20H1-től és Windows 11-en; régebbi rendszeren nem csinál semmit).</summary>
        public static void ApplyTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;
                var value = IsDark ? 1 : 0;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE; a Windows 10 korábbi buildjei még a 19-et ismerik.
                if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
            }
            catch (Exception ex)
            {
                Log.Warn("Nem sikerült beállítani a címsor színét: " + ex.Message);
            }
        }

        // A téma váltásakor a Windows a General kategóriával jelez (WM_SETTINGCHANGE "ImmersiveColorSet").
        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General)
                return;
            var dark = ReadSystemDark();
            var app = Application.Current;
            if (dark == IsDark || app == null)
                return;
            app.Dispatcher.BeginInvoke(new Action(() => Apply(dark)));
        }

        private static bool ReadSystemDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    // Ha nincs ilyen érték (régebbi Windows), világos.
                    return key?.GetValue(LightThemeValue) is int light && light == 0;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Nem sikerült kiolvasni a Windows témáját: " + ex.Message);
                return false;
            }
        }

        private static void Apply(bool dark)
        {
            var resources = Application.Current?.Resources;
            if (resources == null)
                return;
            IsDark = dark;
            foreach (var color in dark ? DarkColors : LightColors)
                resources[color.Key] = CreateBrush(color.Value);
            resources["WindowImage"] = dark
                ? _darkImage ?? (_darkImage = CreateImage("bg-dark-padded.png"))
                : _lightImage ?? (_lightImage = CreateImage("bg-bright-padded.png"));
            Log.Info("Megjelenés: " + (dark ? "sötét" : "világos"));
            Changed?.Invoke();
        }

        private static SolidColorBrush CreateBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static ImageBrush CreateImage(string fileName)
        {
            var image = new BitmapImage(new Uri("pack://application:,,,/Resources/" + fileName, UriKind.Absolute));
            image.Freeze();
            var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            return brush;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
