using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace HearthstoneHungary.Core
{
    /// <summary>A rögzített meccsek. Helye: %AppData%\HearthstoneHungary\games.json (a legutóbbi <see cref="MaxGames"/> meccs).</summary>
    internal sealed class GameStore
    {
        private const string FileName = "games.json";
        private const int MaxGames = 3000;

        private readonly object _lock = new object();
        private readonly List<GameRecord> _games;
        private readonly HashSet<string> _uids;

        private GameStore(List<GameRecord> games)
        {
            _games = games;
            _uids = new HashSet<string>(games.Select(g => g.Uid), StringComparer.Ordinal);
        }

        private static string FilePath => Path.Combine(AppSettings.DataDir, FileName);

        public static GameStore Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var games = JsonConvert.DeserializeObject<List<GameRecord>>(File.ReadAllText(FilePath));
                    if (games != null)
                        return new GameStore(games.Where(g => g != null && !string.IsNullOrEmpty(g.Uid)).ToList());
                }
            }
            catch (Exception ex)
            {
                Log.Error("Nem sikerült betölteni a meccseket, üres listával indulunk: " + ex.Message);
            }
            return new GameStore(new List<GameRecord>());
        }

        /// <summary>A meccsek másolata, a legújabb elöl.</summary>
        public List<GameRecord> Snapshot()
        {
            lock (_lock)
                return _games.OrderByDescending(g => g.StartedUtc).ToList();
        }

        public bool Contains(string uid)
        {
            lock (_lock)
                return _uids.Contains(uid);
        }

        /// <returns>false, ha ez a meccs már megvolt.</returns>
        public bool Add(GameRecord game)
        {
            lock (_lock)
            {
                if (!_uids.Add(game.Uid))
                    return false;
                _games.Add(game);
                if (_games.Count > MaxGames)
                {
                    foreach (var old in _games.OrderBy(g => g.StartedUtc).Take(_games.Count - MaxGames).ToList())
                    {
                        _games.Remove(old);
                        _uids.Remove(old.Uid);
                    }
                }
                Save();
                return true;
            }
        }

        /// <summary>A még fel nem töltött meccsek, a legrégebbi elöl.</summary>
        public List<GameRecord> Pending(int max)
        {
            lock (_lock)
                return _games.Where(g => !g.Uploaded).OrderBy(g => g.StartedUtc).Take(max).ToList();
        }

        public int PendingCount
        {
            get
            {
                lock (_lock)
                    return _games.Count(g => !g.Uploaded);
            }
        }

        public void MarkUploaded(IEnumerable<GameRecord> games)
        {
            lock (_lock)
            {
                foreach (var game in games)
                    game.Uploaded = true;
                Save();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataDir);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(_games, Formatting.None));
                if (File.Exists(FilePath))
                    File.Replace(temp, FilePath, null);
                else
                    File.Move(temp, FilePath);
            }
            catch (Exception ex)
            {
                Log.Error("Nem sikerült menteni a meccseket: " + ex.Message);
            }
        }
    }
}
