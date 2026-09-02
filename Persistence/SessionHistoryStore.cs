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

        /// <summary>How many runs are kept, so the UI can warn before the oldest drop off.</summary>
        public static int Capacity => MaxEntries;

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
                // Back up the unreadable file before any later Save() overwrites it,
                // so a corrupt sessions.json doesn't silently destroy the user's
                // history (mirrors SettingsManager's corrupt-file handling).
                Logger.Error("Failed to load session history.", ex);
                try { PersistenceHelper.BackupCorruptFile(GetPath()); } catch { }
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
                // Say so. The lifetime aggregates already carry this run's totals
                // forward, so nothing is lost from the numbers — but the row itself is
                // gone for good, and a silent drop is how someone discovers months
                // later that their history only ever went back so far.
                var dropped = _records[_records.Count - 1];
                _records.RemoveAt(_records.Count - 1);
                Logger.Info("[history] at the " + MaxEntries + "-run limit; dropped the oldest run (" +
                            (dropped != null ? dropped.WhenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "?") +
                            "). Export CSV keeps a copy.");
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
