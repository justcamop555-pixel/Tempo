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

        /// <summary>Soft-deleted profiles, newest last. Kept so they can be restored.</summary>
        private readonly List<ClickProfile> _recycleBin = new List<ClickProfile>();
        private const int MaxRecycleBin = 25;

        public IReadOnlyList<ClickProfile> Profiles => _profiles;
        public IReadOnlyList<ClickProfile> RecycleBin => _recycleBin;

        public int Count => _profiles.Count;

        public static string GetProfilesPath()
        {
            return Path.Combine(SettingsManager.GetSettingsDirectory(), "profiles.json");
        }

        /// <summary>
        /// Where the recycle bin lives — a SEPARATE file, deliberately.
        ///
        /// profiles.json is a bare JSON array and has been since the first release.
        /// Folding the bin into it would mean changing that to an object, which an
        /// older Tempo would read as "no profiles" and helpfully replace with a
        /// default. A second file keeps the format of the important one frozen, and
        /// makes a missing or damaged bin cost nothing at all.
        /// </summary>
        public static string GetRecycleBinPath()
        {
            return Path.Combine(SettingsManager.GetSettingsDirectory(), "profiles.recycle.json");
        }

        /// <summary>Loads profiles from disk, seeding a default if none exist.</summary>
        public void Load()
        {
            _profiles.Clear();
            LoadRecycleBin();

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

            Logger.Info($"[Profiles] loaded {_profiles.Count} profile(s).");
            // The unit word is translated separately from the number, so the splash's
            // right-hand detail column isn't the one English thing left on the screen.
            UI.SplashForm.Report(0, _profiles.Count + " " +
                Utils.Localization.T(_profiles.Count == 1 ? "profile" : "profiles"));
        }

        public bool Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(_profiles, Options);
                bool ok = PersistenceHelper.WriteAtomic(GetProfilesPath(), json);
                if (ok)
                {
                    Logger.Info("[Profiles] saved.");
                }
                SaveRecycleBin();
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save profiles.", ex);
                return false;
            }
        }

        /// <summary>
        /// Reads the soft-deleted profiles back. Any problem here is swallowed to an
        /// empty bin: losing the undo history is a nuisance, and it must never be
        /// allowed to stop the real profiles from loading.
        /// </summary>
        private void LoadRecycleBin()
        {
            _recycleBin.Clear();

            try
            {
                string path = GetRecycleBinPath();
                if (!File.Exists(path)) { return; }

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) { return; }

                var loaded = JsonSerializer.Deserialize<List<ClickProfile>>(json, Options);
                if (loaded == null) { return; }

                foreach (var p in loaded)
                {
                    if (p == null) { continue; }
                    if (p.Points == null) { p.Points = new List<ClickPoint>(); }
                    _recycleBin.Add(p);
                }

                while (_recycleBin.Count > MaxRecycleBin)
                {
                    _recycleBin.RemoveAt(0);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load the profile recycle bin; starting empty.", ex);
                _recycleBin.Clear();
            }
        }

        /// <summary>
        /// Writes the bin alongside the profiles. Without this the soft delete only
        /// lasted until the app closed — Remove() has always moved a profile aside
        /// rather than dropping it, but nothing ever persisted where it went, so
        /// "delete, restart, restore" quietly could not work.
        /// </summary>
        private void SaveRecycleBin()
        {
            try
            {
                string json = JsonSerializer.Serialize(_recycleBin, Options);
                PersistenceHelper.WriteAtomic(GetRecycleBinPath(), json);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save the profile recycle bin.", ex);
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

            // Soft-delete: keep a copy in the recycle bin so it can be restored.
            _recycleBin.Add(existing);
            while (_recycleBin.Count > MaxRecycleBin)
            {
                _recycleBin.RemoveAt(0);
            }

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

        /// <summary>Restores a profile from the recycle bin back into the library.</summary>
        public ClickProfile RestoreFromRecycleBin(string name)
        {
            var found = _recycleBin.FindLast(p =>
                p != null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (found == null)
            {
                return null;
            }
            _recycleBin.Remove(found);
            found.Name = MakeUniqueName(found.Name); // avoid clashing with a new same-name profile
            _profiles.Add(found);
            return found;
        }

        /// <summary>Permanently clears the recycle bin.</summary>
        public void EmptyRecycleBin()
        {
            _recycleBin.Clear();
        }

        /// <summary>Toggles the favourite star on a profile; returns the new state.</summary>
        public bool ToggleFavorite(string name)
        {
            var p = GetByName(name);
            if (p == null) return false;
            p.Favorite = !p.Favorite;
            return p.Favorite;
        }

        /// <summary>
        /// Records that a profile was just activated: bumps its use count and
        /// last-used timestamp. Call when the user sets a profile active.
        /// </summary>
        public void MarkUsed(string name)
        {
            var p = GetByName(name);
            if (p == null) return;
            p.TimesUsed++;
            p.LastUsedUtc = DateTime.UtcNow;
        }

        /// <summary>Adds clicking runtime (seconds) to a profile's cumulative total.</summary>
        public void AddRuntime(string name, long seconds)
        {
            if (seconds <= 0) return;
            var p = GetByName(name);
            if (p == null) return;
            p.TotalRuntimeSeconds += seconds;
        }

        /// <summary>Serialises a single profile to a JSON string (for export/share).</summary>
        public static string ExportToJson(ClickProfile profile)
        {
            if (profile == null) return null;
            try { return JsonSerializer.Serialize(profile, Options); }
            catch (Exception ex) { Logger.Error("Failed to export profile.", ex); return null; }
        }

        /// <summary>
        /// Parses an exported profile JSON string. Returns null (with the reason in
        /// <paramref name="error"/>) when the text isn't a valid profile.
        /// </summary>
        public static ClickProfile ParseExported(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The file is empty.";
                return null;
            }
            try
            {
                var p = JsonSerializer.Deserialize<ClickProfile>(json, Options);
                if (p == null)
                {
                    error = "The file isn't a valid Tempo profile.";
                    return null;
                }
                if (p.Points == null) p.Points = new List<ClickPoint>();
                string vErr = p.Validate();
                if (vErr != null)
                {
                    error = "The profile failed validation: " + vErr;
                    return null;
                }
                return p;
            }
            catch (Exception ex)
            {
                error = "Couldn't read the profile: " + ex.Message;
                return null;
            }
        }

        /// <summary>How an imported profile whose name already exists is handled.</summary>
        public enum ImportConflict { Rename, Overwrite }

        /// <summary>
        /// Imports a parsed profile, resolving a name clash per
        /// <paramref name="conflict"/>. Returns the profile as stored.
        /// </summary>
        public ClickProfile Import(ClickProfile incoming, ImportConflict conflict)
        {
            if (incoming == null) return null;
            if (incoming.Points == null) incoming.Points = new List<ClickPoint>();

            var clash = GetByName(incoming.Name);
            if (clash == null)
            {
                _profiles.Add(incoming);
                return incoming;
            }

            if (conflict == ImportConflict.Overwrite)
            {
                int idx = _profiles.IndexOf(clash);
                incoming.Name = clash.Name; // keep the exact name
                _profiles[idx] = incoming;
                return incoming;
            }

            // Rename: give the import a unique name and keep both.
            incoming.Name = MakeUniqueName(incoming.Name);
            _profiles.Add(incoming);
            return incoming;
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
