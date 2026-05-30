using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.Persistence
{
    /// <summary>
    /// Loads and saves the rolling list of <see cref="SessionRecord"/> entries to
    /// sessions.json in the data folder. Newest first, capped to a sane maximum so
    /// the file never grows without bound.
    /// </summary>
    public sealed class SessionHistoryStore
    {
        private const int MaxEntries = 200;

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly List<SessionRecord> _records = new List<SessionRecord>();

        public IReadOnlyList<SessionRecord> Records => _records;

        public static string GetPath()
        {
            return Path.Combine(SettingsManager.GetSettingsDirectory(), "sessions.json");
        }

        public void Load()
        {
            _records.Clear();
            try
            {
                string path = GetPath();
                if (!File.Exists(path))
                {
                    return;
                }

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var list = JsonSerializer.Deserialize<List<SessionRecord>>(json, Options);
                if (list != null)
                {
                    _records.AddRange(list);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load session history.", ex);
            }
        }

        public bool Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(_records, Options);
                return PersistenceHelper.WriteAtomic(GetPath(), json);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save session history.", ex);
                return false;
            }
        }

        /// <summary>Inserts a record at the front (newest first) and trims + saves.</summary>
        public void Add(SessionRecord record)
        {
            if (record == null)
            {
                return;
            }

            _records.Insert(0, record);
            while (_records.Count > MaxEntries)
            {
                _records.RemoveAt(_records.Count - 1);
            }

            Save();
        }

        public void Clear()
        {
            _records.Clear();
            Save();
        }

        /// <summary>Removes a single record by index and saves.</summary>
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _records.Count)
            {
                return;
            }

            _records.RemoveAt(index);
            Save();
        }
    }
}
