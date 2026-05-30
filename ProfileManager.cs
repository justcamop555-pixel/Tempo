using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.Persistence
{
    /// <summary>
    /// Maintains the in-memory list of <see cref="ClickProfile"/> objects and
    /// persists the whole collection to a single JSON file. Provides convenience
    /// methods for add / remove / rename / lookup used by the UI.
    /// </summary>
    public sealed class ProfileManager
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly List<ClickProfile> _profiles = new List<ClickProfile>();

        public IReadOnlyList<ClickProfile> Profiles => _profiles;

        public int Count => _profiles.Count;

        public static string GetProfilesPath()
        {
            return Path.Combine(SettingsManager.GetSettingsDirectory(), "profiles.json");
        }

        /// <summary>Loads profiles from disk, seeding a default if none exist.</summary>
        public void Load()
        {
            _profiles.Clear();

            try
            {
                string path = GetProfilesPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var loaded = JsonSerializer.Deserialize<List<ClickProfile>>(json, Options);
                        if (loaded != null)
                        {
                            foreach (var p in loaded)
                            {
                                if (p != null)
                                {
                                    if (p.Points == null)
                                    {
                                        p.Points = new List<ClickPoint>();
                                    }
                                    _profiles.Add(p);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load profiles.", ex);
                PersistenceHelper.BackupCorruptFile(GetProfilesPath());
                _profiles.Clear();
            }

            if (_profiles.Count == 0)
            {
                _profiles.Add(CreateDefaultProfile());
                Save();
            }

            Logger.Info($"Loaded {_profiles.Count} profile(s).");
        }

        public bool Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(_profiles, Options);
                bool ok = PersistenceHelper.WriteAtomic(GetProfilesPath(), json);
                if (ok)
                {
                    Logger.Info("Profiles saved.");
                }
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save profiles.", ex);
                return false;
            }
        }

        public ClickProfile GetByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            return _profiles.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public bool Exists(string name)
        {
            return GetByName(name) != null;
        }

        /// <summary>
        /// Adds a profile. If the name collides, a numeric suffix is appended so
        /// names remain unique.
        /// </summary>
        public ClickProfile Add(ClickProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            profile.Name = MakeUniqueName(profile.Name);
            _profiles.Add(profile);
            return profile;
        }

        public bool Remove(string name)
        {
            var existing = GetByName(name);
            if (existing == null)
            {
                return false;
            }

            _profiles.Remove(existing);

            // Never leave the library completely empty.
            if (_profiles.Count == 0)
            {
                _profiles.Add(CreateDefaultProfile());
            }

            return true;
        }

        public bool Rename(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                return false;
            }

            var existing = GetByName(oldName);
            if (existing == null)
            {
                return false;
            }

            // If something else already uses the new name, refuse.
            var clash = GetByName(newName);
            if (clash != null && !ReferenceEquals(clash, existing))
            {
                return false;
            }

            existing.Name = newName;
            return true;
        }

        /// <summary>Replaces the stored profile that matches by name.</summary>
        public void Update(ClickProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            for (int i = 0; i < _profiles.Count; i++)
            {
                if (string.Equals(_profiles[i].Name, profile.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _profiles[i] = profile;
                    return;
                }
            }

            // Not found by name: add it.
            _profiles.Add(profile);
        }

        public ClickProfile Duplicate(string name)
        {
            var src = GetByName(name);
            if (src == null)
            {
                return null;
            }

            var copy = src.Clone();
            copy.Name = MakeUniqueName(src.Name + " Copy");
            _profiles.Add(copy);
            return copy;
        }

        private string MakeUniqueName(string desired)
        {
            if (string.IsNullOrWhiteSpace(desired))
            {
                desired = "Profile";
            }

            if (!Exists(desired))
            {
                return desired;
            }

            int suffix = 2;
            string candidate;
            do
            {
                candidate = $"{desired} {suffix}";
                suffix++;
            }
            while (Exists(candidate));

            return candidate;
        }

        public static ClickProfile CreateDefaultProfile()
        {
            return new ClickProfile("Default")
            {
                Description = "A simple 100 ms left-click profile.",
                IntervalMilliseconds = 100,
                Button = MouseButtonType.Left,
                Style = ClickStyle.Single,
                Mode = ClickMode.Interval,
                PositionMode = PositionMode.CurrentPosition,
                RepeatMode = RepeatMode.UntilStopped
            };
        }
    }
}
