using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Utility.Logging;

namespace exportalas.Internal
{
    /// <summary>A plugin beállításai. Helye: %AppData%\HearthstoneDeckTracker\HSHU_Gyujtemeny\settings.xml</summary>
    [Serializable]
    public class PluginSettings
    {
        private const string FolderName = "HSHU_Gyujtemeny";
        private const string FileName = "settings.xml";
        private static readonly object FileLock = new object();

        /// <summary>Feltöltés magától, ha változott a gyűjtemény.</summary>
        public bool AutoUpload { get; set; } = true;

        /// <summary>Új verzió letöltése magától a GitHubról.</summary>
        public bool AutoUpdate { get; set; } = true;

        /// <summary>A HSHU token DPAPI-val titkosítva (lásd TokenStore).</summary>
        public string ProtectedToken { get; set; }

        /// <summary>A HSHU felhasználónév, amihez a token tartozik (csak kiírásra).</summary>
        public string LinkedUserName { get; set; }

        public DateTime LastUpdateCheckUtc { get; set; }

        /// <summary>Az utolsó sikeres feltöltés Battle.net fiókonként (ha valaki több fiókkal játszik).</summary>
        public List<UploadRecord> Uploads { get; set; } = new List<UploadRecord>();

        public static string DataDir => Path.Combine(Config.Instance.DataDir, FolderName);

        private static string FilePath => Path.Combine(DataDir, FileName);

        public static PluginSettings Load()
        {
            try
            {
                lock (FileLock)
                {
                    if (File.Exists(FilePath))
                    {
                        using (var reader = new StreamReader(FilePath))
                        {
                            var settings = (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(reader);
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
                Log.Error("[HSHU] Nem sikerült betölteni a beállításokat, alapértékekkel indulunk: " + ex);
            }
            return new PluginSettings();
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
                        new XmlSerializer(typeof(PluginSettings)).Serialize(writer, this);
                    if (File.Exists(FilePath))
                        File.Replace(tempFile, FilePath, null);
                    else
                        File.Move(tempFile, FilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[HSHU] Nem sikerült menteni a beállításokat: " + ex);
            }
        }

        public UploadRecord FindUpload(string battleTag) =>
            Uploads.FirstOrDefault(u => string.Equals(u.BattleTag, battleTag, StringComparison.OrdinalIgnoreCase));

        [XmlIgnore]
        public UploadRecord LastUpload => Uploads.OrderByDescending(u => u.UploadedAtUtc).FirstOrDefault();

        public void RememberUpload(string battleTag, string hash, int totalCards)
        {
            var record = FindUpload(battleTag);
            if (record == null)
            {
                record = new UploadRecord { BattleTag = battleTag };
                Uploads.Add(record);
            }
            record.Hash = hash;
            record.TotalCards = totalCards;
            record.UploadedAtUtc = DateTime.UtcNow;
        }
    }

    [Serializable]
    public class UploadRecord
    {
        public string BattleTag { get; set; }
        public string Hash { get; set; }
        public int TotalCards { get; set; }
        public DateTime UploadedAtUtc { get; set; }
    }
}
