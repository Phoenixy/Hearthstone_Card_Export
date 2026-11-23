using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace HearthstoneHungary.Core
{
    /// <summary>Az alkalmazás beállításai. Helye: %AppData%\HearthstoneHungary\settings.xml</summary>
    [Serializable]
    public class AppSettings
    {
        private const string FolderName = "HearthstoneHungary";
        private const string FileName = "settings.xml";
        private static readonly object FileLock = new object();

        /// <summary>Feltöltés magától, ha változott a gyűjtemény.</summary>
        public bool AutoUpload { get; set; } = true;

        /// <summary>Új verzió letöltése magától a GitHubról.</summary>
        public bool AutoUpdate { get; set; } = true;

        /// <summary>Indulás a Windows-szal (a tálcára, ablak nélkül).</summary>
        public bool StartWithWindows { get; set; } = true;

        /// <summary>Megmutattuk-e már, hogy bezáráskor a program a tálcán fut tovább.</summary>
        public bool TrayHintShown { get; set; }

        /// <summary>A HSHU token DPAPI-val titkosítva (lásd TokenStore).</summary>
        public string ProtectedToken { get; set; }

        /// <summary>A HSHU felhasználónév, amihez a token tartozik (csak kiírásra).</summary>
        public string LinkedUserName { get; set; }

        public DateTime LastUpdateCheckUtc { get; set; }

        /// <summary>Az utolsó sikeres feltöltés Battle.net fiókonként (ha valaki több fiókkal játszik).</summary>
        public List<UploadRecord> Uploads { get; set; } = new List<UploadRecord>();

        public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

        private static string FilePath => Path.Combine(DataDir, FileName);

        public static AppSettings Load()
        {
            try
            {
                lock (FileLock)
                {
                    if (File.Exists(FilePath))
                    {
                        using (var reader = new StreamReader(FilePath))
                        {
                            var settings = (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(reader);
                            if (settings != null)
                            {
                                settings.Uploads = settings.Uploads ?? new List<UploadRecord>();
                                return settings;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Nem sikerült betölteni a beállításokat, alapértékekkel indulunk: " + ex);
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                lock (FileLock)
                {
                    Directory.CreateDirectory(DataDir);
                    var tempFile = FilePath + ".tmp";
                    using (var writer = new StreamWriter(tempFile))
                        new XmlSerializer(typeof(AppSettings)).Serialize(writer, this);
                    if (File.Exists(FilePath))
                        File.Replace(tempFile, FilePath, null);
                    else
                        File.Move(tempFile, FilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Nem sikerült menteni a beállításokat: " + ex);
            }
        }

        public UploadRecord FindUpload(string battleTag) =>
            Uploads.FirstOrDefault(u => string.Equals(u.BattleTag, battleTag, StringComparison.OrdinalIgnoreCase));

        [XmlIgnore]
        public UploadRecord LastUpload => Uploads.OrderByDescending(u => u.UploadedAtUtc).FirstOrDefault();

        public void RememberUpload(string battleTag, string hash, int totalCards, int shownCards)
        {
            var record = FindUpload(battleTag);
            if (record == null)
            {
                record = new UploadRecord { BattleTag = battleTag };
                Uploads.Add(record);
            }
            record.Hash = hash;
            record.TotalCards = totalCards;
            record.ShownCards = shownCards;
            record.UploadedAtUtc = DateTime.UtcNow;
        }
    }

    [Serializable]
    public class UploadRecord
    {
        public string BattleTag { get; set; }
        public string Hash { get; set; }
        /// <summary>A játékból kiolvasott darabszám (ebből vesszük észre a gyanúsan hiányos kiolvasást).</summary>
        public int TotalCards { get; set; }

        /// <summary>Amennyit a szerver elmentett: csak az oldalon megjelenő lapok (Classic formátum, hősök stb. nélkül).</summary>
        public int ShownCards { get; set; }

        public DateTime UploadedAtUtc { get; set; }
    }
}
