using Hearthstone_Deck_Tracker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Hearthstone_Deck_Tracker.Utility.Logging;
using exportalas.Internal;

namespace exportalas.Internal
{
    [Serializable]
    public class PluginSettings
    {
        public ModuleVersion CurrentVersion { get; set; }

        public string ActiveAccount { get; set; }

        public List<AccountSummary> Accounts { get; set; }

        public double CollectionWindowWidth { get; set; }

        public double CollectionWindowHeight { get; set; }

        public bool DefaultShowAllCards { get; set; }

        public bool NotifyNewDeckMissingCards { get; set; }

        public bool EnableDesiredCardsFeature { get; set; }

		public bool EnableAutoImport { get; set; }

        public bool UseDecksForDesiredCards { get; set; }


        private const string STORAGE_FILE_NAME = "config.xml";

        public void SetActiveAccount(string accountName, bool forceReload = false)
        {
            if (accountName == ActiveAccount && !forceReload)
            {
                return;
            }
            var activeAccount = Accounts.FirstOrDefault(ac => ac.AccountName == accountName);
            if (activeAccount == null)
            {
                Log.WriteLine("Cannot set active account " + accountName + " because it does not exist", LogType.Debug, "CollectionTracker.PluginSettings");
                return;
            }
            ActiveAccount = accountName;
        }


        public static PluginSettings LoadSettings(string dataDir)
        {
            string settingsFilePath = Path.Combine(dataDir, STORAGE_FILE_NAME);
            PluginSettings settings;
            if (File.Exists(settingsFilePath))
            {
                settings = XmlManager<PluginSettings>.Load(settingsFilePath);
            }
            else
            {
                string collectionFilePath = Path.Combine(ExportalasPlugin.PluginDataDir, "Collection_Default.xml");
                settings = new PluginSettings()
                {
                    CurrentVersion = new ModuleVersion(ExportalasPlugin.PluginVersion),
                    Accounts = new List<AccountSummary>()
                    {
                        new AccountSummary()
                        {
                            AccountName = "Default",
                            FileStoragePath = collectionFilePath
                        }
                    },
                    ActiveAccount = "Default",
                    CollectionWindowWidth = 395,
                    CollectionWindowHeight = 560,
                    DefaultShowAllCards = false
                };
            }

            settings.SetActiveAccount(settings.ActiveAccount, true);

            return settings;
        }

        public void RenameCurrentAccount(string newName)
        {
            var account = Accounts.First(a => a.AccountName == ActiveAccount);
            account.AccountName = newName;
            ActiveAccount = newName;
        }

        public void SaveSettings(string dataDir)
        {
            string settingsFilePath = Path.Combine(dataDir, STORAGE_FILE_NAME);
            XmlManager<PluginSettings>.Save(settingsFilePath, this);
        }
    }

    [Serializable]
    public class AccountSummary
    {
        public string AccountName { get; set; }

        public string FileStoragePath { get; set; }

        public override bool Equals(object obj)
        {
            if (!(obj is AccountSummary) || obj == null)
                return false;
            var o = obj as AccountSummary;
            return AccountName.Equals(o.AccountName) && FileStoragePath.Equals(o.FileStoragePath);
        }
    }
}
