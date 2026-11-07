using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LogicReinc.BlendFarm.Objects
{
    /// <summary>
    /// One finished render (still or animation) in the history gallery.
    /// </summary>
    public class RenderHistoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Date { get; set; } = DateTime.Now;
        public string ProjectName { get; set; }
        public string SceneName { get; set; }
        public string BlendFile { get; set; }
        public bool IsAnimation { get; set; }
        public int FrameStart { get; set; }
        public int FrameEnd { get; set; }
        public int FrameCount { get; set; } = 1;
        public double DurationSeconds { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Samples { get; set; }
        public string Engine { get; set; }
        public int NodeCount { get; set; }

        /// <summary>Image file the render was saved to (known for queue items and after Save Image), if any.</summary>
        public string OutputPath { get; set; }
        /// <summary>Folder the render was saved to (the animation folder for animations), if known.</summary>
        public string OutputDirectory { get; set; }

        /// <summary>Small PNG thumbnail, relative to the history folder.</summary>
        public string ThumbnailFile { get; set; }
        /// <summary>Larger preview copy (still: the render, animation: its last frame), relative to the history folder.</summary>
        public string PreviewFile { get; set; }
    }

    /// <summary>
    /// Persisted list of finished renders, newest first and capped, stored as a JSON file
    /// (next to the settings) with thumbnails in a separate folder.
    /// </summary>
    public class RenderHistoryStore
    {
        public const int DefaultMaxEntries = 200;

        private readonly object _lock = new object();
        private readonly List<RenderHistoryEntry> _entries = new List<RenderHistoryEntry>();
        private readonly string _filePath;

        public int MaxEntries { get; }
        /// <summary>Folder that holds thumbnails and previews.</summary>
        public string AssetsDirectory { get; }
        public string ThumbnailDirectory => Path.Combine(AssetsDirectory, "thumbs");
        public string PreviewDirectory => Path.Combine(AssetsDirectory, "previews");

        /// <summary>Raised (possibly from a worker thread) after the list changed.</summary>
        public event Action Changed;

        public RenderHistoryStore(string filePath, string assetsDirectory = null, int maxEntries = DefaultMaxEntries)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            AssetsDirectory = assetsDirectory ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".", "History");
            MaxEntries = Math.Max(1, maxEntries);
        }

        private static RenderHistoryStore _default;
        private static readonly object _defaultLock = new object();
        /// <summary>The application's history, stored next to the client settings.</summary>
        public static RenderHistoryStore Default
        {
            get
            {
                lock (_defaultLock)
                {
                    if (_default == null)
                    {
                        _default = new RenderHistoryStore(AppPaths.Resolve("RenderHistory.json"), AppPaths.Resolve("History"));
                        _default.Load();
                    }
                    return _default;
                }
            }
        }

        public int Count { get { lock (_lock) return _entries.Count; } }

        /// <summary>Copy of the entries, newest first.</summary>
        public List<RenderHistoryEntry> Snapshot()
        {
            lock (_lock)
                return new List<RenderHistoryEntry>(_entries);
        }

        public RenderHistoryEntry Find(string id)
        {
            lock (_lock)
                return _entries.FirstOrDefault(e => e.Id == id);
        }

        /// <summary>Absolute path of a thumbnail/preview file, or null.</summary>
        public string ResolveAsset(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            return Path.IsPathRooted(relative) ? relative : Path.Combine(AssetsDirectory, relative);
        }

        public void Load()
        {
            lock (_lock)
            {
                _entries.Clear();
                if (!File.Exists(_filePath)) return;
                try
                {
                    var loaded = JsonSerializer.Deserialize<List<RenderHistoryEntry>>(File.ReadAllText(_filePath));
                    if (loaded != null)
                    {
                        _entries.AddRange(loaded.Where(e => e != null && !string.IsNullOrEmpty(e.Id))
                            .OrderByDescending(e => e.Date).Take(MaxEntries));
                    }
                }
                catch (Exception ex)
                {
                    //Keep the broken file for recovery and start empty instead of failing
                    Console.WriteLine($"Failed to load render history, starting empty: {ex.Message}");
                    try { File.Copy(_filePath, _filePath + ".corrupt", true); } catch { }
                    _entries.Clear();
                }
            }
        }

        /// <summary>Writes the list; never throws (returns false and logs instead).</summary>
        public bool Save()
        {
            lock (_lock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(_filePath));
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    string json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_filePath + ".tmp", json);
                    File.Move(_filePath + ".tmp", _filePath, true);
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save render history: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>Adds an entry as the newest, trims the list to the limit (deleting the trimmed entries' files) and saves.</summary>
        /// <returns>Entries that were dropped to stay within the limit</returns>
        public List<RenderHistoryEntry> Add(RenderHistoryEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            List<RenderHistoryEntry> removed = new List<RenderHistoryEntry>();
            lock (_lock)
            {
                _entries.Insert(0, entry);
                while (_entries.Count > MaxEntries)
                {
                    removed.Add(_entries[_entries.Count - 1]);
                    _entries.RemoveAt(_entries.Count - 1);
                }
                foreach (var r in removed)
                    DeleteFiles(r);
                Save();
            }
            Changed?.Invoke();
            return removed;
        }

        /// <summary>Removes an entry (and its thumbnail/preview files, never the render output).</summary>
        public bool Remove(string id)
        {
            lock (_lock)
            {
                var entry = _entries.FirstOrDefault(e => e.Id == id);
                if (entry == null) return false;
                _entries.Remove(entry);
                DeleteFiles(entry);
                Save();
            }
            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            lock (_lock)
            {
                foreach (var e in _entries)
                    DeleteFiles(e);
                _entries.Clear();
                Save();
            }
            Changed?.Invoke();
        }

        /// <summary>Changes an entry in place and saves, returns false if it no longer exists.</summary>
        public bool Update(string id, Action<RenderHistoryEntry> change)
        {
            lock (_lock)
            {
                var entry = _entries.FirstOrDefault(e => e.Id == id);
                if (entry == null) return false;
                change(entry);
                Save();
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>The newest entry matching the predicate, or null.</summary>
        public RenderHistoryEntry FindNewest(Func<RenderHistoryEntry, bool> predicate)
        {
            lock (_lock)
                return _entries.FirstOrDefault(predicate);
        }

        private void DeleteFiles(RenderHistoryEntry entry)
        {
            foreach (string rel in new[] { entry.ThumbnailFile, entry.PreviewFile })
            {
                string path = ResolveAsset(rel);
                if (path == null) continue;
                //Only ever delete our own files
                if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(AssetsDirectory), StringComparison.OrdinalIgnoreCase))
                    continue;
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }
    }
}
