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

        /// <summary>Tálcaértesítés, ha új hír jelenik meg a hearthstonehungary.hu-n.</summary>
        public bool NewsNotifications { get; set; } = true;

        /// <summary>A lejátszott meccsek rögzítése a játék naplójából és feltöltése a HSHU-ra (statisztika).</summary>
        public bool TrackGames { get; set; } = true;

        /// <summary>A legújabb már látott hír megjelenési ideje és azonosítója (az ennél újabbakról szólunk).</summary>
        public DateTime LastNewsDateUtc { get; set; }

        public int LastNewsId { get; set; }

        /// <summary>Megmutattuk-e már, hogy bezáráskor a program a tálcán fut tovább.</summary>
        public bool TrayHintShown { get; set; }

        /// <summary>
        /// Az eszköz (ez a telepítés) azonosítója, az első indításkor jön létre. Az oldal eszközönként ad tokent, és
        /// ugyanennek az eszköznek az újbóli összekötésekor a régi tokent visszavonja.
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>A HSHU token DPAPI-val titkosítva (lásd TokenStore).</summary>
        public string ProtectedToken { get; set; }

        /// <summary>A HSHU felhasználónév, amihez a token tartozik (csak kiírásra).</summary>
        public string LinkedUserName { get; set; }

        public DateTime LastUpdateCheckUtc { get; set; }

        /// <summary>Az utolsó sikeres feltöltés Battle.net fiókonként (ha valaki több fiókkal játszik).</summary>
        public List<UploadRecord> Uploads { get; set; } = new List<UploadRecord>();

        /// <summary>A Hearthstone mappája (a legutóbbi futásból), hogy a naplók akkor is megtalálhatók legyenek, ha épp nem fut.</summary>
        public string HearthstoneDir { get; set; }

        /// <summary>A játék naplójában látott paklik (lásd DeckTracker). A listát mindig újra cseréljük, helyben nem módosítjuk.</summary>
        public List<SavedDeck> Decks { get; set; } = new List<SavedDeck>();

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
                                settings.Decks = settings.Decks ?? new List<SavedDeck>();
                                settings.EnsureDeviceId();
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
            var fresh = new AppSettings();
            fresh.EnsureDeviceId();
            return fresh;
        }

        private void EnsureDeviceId()
        {
            if (DeviceId != null && DeviceId.Length == 32)
                return;
            DeviceId = Guid.NewGuid().ToString("N");
            Save();
        }

        public void Save()
        {
            // Frissítés utáni újraindításkor a régi példány (vagy a víruskereső) egy pillanatig még foghatja a fájlt: pár próbálkozás.
            for (var attempt = 1; ; attempt++)
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
                    return;
                }
                catch (IOException ex) when (attempt < 5)
                {
                    Log.Info("A beállítások fájlja foglalt, újrapróbálás: " + ex.Message);
                    System.Threading.Thread.Sleep(200 * attempt);
                }
                catch (Exception ex)
                {
                    Log.Error("Nem sikerült menteni a beállításokat: " + ex);
                    return;
                }
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

    /// <summary>Egy pakli a játékból: a neve, a játékbeli azonosítója és a pakli kód.</summary>
    [Serializable]
    public class SavedDeck
    {
        public string Name { get; set; }

        /// <summary>A játék pakliazonosítója (0, ha a napló nem írta ki).</summary>
        public long DeckId { get; set; }

        public string Deckstring { get; set; }

        /// <summary>Mikor láttuk utoljára a naplóban.</summary>
        public DateTime SeenUtc { get; set; }

        /// <summary>Mikor indítottak vele utoljára meccset (alapérték, ha még nem).</summary>
        public DateTime PlayedUtc { get; set; }

        /// <summary>Ugyanaz a pakli-e: az azonosító alapján (így a szerkesztett pakli a régit váltja), annak híján a kód alapján.</summary>
        [XmlIgnore]
        public string Key => DeckId > 0 ? "id:" + DeckId : "code:" + Deckstring;

        public SavedDeck Clone() => (SavedDeck)MemberwiseClone();

        public override string ToString() => Name ?? "";
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
