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

        /// <summary>
        /// Builds the JSON description of profiles ahead of the first Load — see
        /// SettingsManager.WarmUp. 26 of the 32 ms reading the 2 KB recycle bin at start-up was this.
        /// </summary>
        internal static void WarmUp()
        {
            try
            {
                // A round trip, so every property's accessors are made too (see SettingsManager.WarmUp).
                var p = new ClickProfile("warm-up");
                p.Points.Add(new ClickPoint());
                string json = JsonSerializer.Serialize(new List<ClickProfile> { p }, Options);
                JsonSerializer.Deserialize<List<ClickProfile>>(json, Options);
            }
            catch (Exception ex) { Logger.Swallow("ProfileManager.WarmUp", ex); }
        }

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

            bool recovered = false;
            bool sanitized = false;
            try
            {
                string path = GetProfilesPath();
                // A missing file stays missing — deleting it is the README's way to reset profiles.
                if (File.Exists(path))
                {
                    List<ClickProfile> loaded = TryParseProfiles(File.ReadAllText(path));
                    if (loaded == null)
                    {
                        // Empty or unreadable: recover the previous save, as settings always have. Without
                        // this, a damaged file meant losing every profile — and the default seeded below was
                        // saved at once, so the save after that rotated it over the good ".1" for good.
                        string previous = PersistenceHelper.ReadPreviousIfUsable(path, t => TryParseProfiles(t) != null);
                        loaded = TryParseProfiles(previous);
                        PersistenceHelper.BackupCorruptFile(path);
                        recovered = loaded != null;
                    }
                    if (loaded != null)
                    {
                        foreach (var p in loaded)
                        {
                            if (p != null)
                            {
                                // Damage the editors cannot show — see ClickProfile.Sanitize (enum
                                // values are left for a newer Tempo that may have written them).
                                if (p.Sanitize(imported: false)) { sanitized = true; }
                                _profiles.Add(p);
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

            // Names are the identity every lookup goes by (GetByName, the Clicker combo, the tray,
            // LastProfileName), compared without case. Two profiles called "Fast" and "fast", or
            // one with no name at all, left the second unreachable: choosing it loaded the first.
            // Only a hand-edited or damaged file can hold one — every path in the app keeps names
            // unique — but it was taken on trust. The FIRST keeps its name; the rest are renamed,
            // and the file is saved so the fix sticks.
            bool renamed = MakeLoadedNamesUnique();
            if (sanitized)
            {
                Logger.Warn("[Profiles] some profile values were outside what Tempo can show (a hand-edited "
                            + "or damaged file, or one from a newer Tempo) and were brought back into range.");
            }

            // Recovered profiles go straight back to disk (the damaged file was set aside).
            if ((recovered || renamed || sanitized) && _profiles.Count > 0) { Save(); }

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

        /// <summary>
        /// Gives every loaded profile a name of its own: blank names become "Profile", and a
        /// name already taken (ignoring case) gets a number. Returns true when anything changed.
        /// </summary>
        private bool MakeLoadedNamesUnique()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (ClickProfile p in _profiles)
            {
                string original = p.Name;
                // Not trimmed: a name is matched against LastProfileName as stored, and
                // tidying one would quietly change which profile opens at start-up.
                string name = string.IsNullOrWhiteSpace(original) ? "Profile" : original;
                if (seen.Contains(name))
                {
                    int n = 2;
                    string candidate;
                    do { candidate = name + " " + n; n++; } while (seen.Contains(candidate));
                    name = candidate;
                }
                seen.Add(name);
                if (!string.Equals(name, original, StringComparison.Ordinal))
                {
                    Logger.Warn("[Profiles] a profile named '" + (original ?? "") + "' clashed with another "
                                + "(or had no name) — renamed to '" + name + "' so it can be chosen.");
                    p.Name = name;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>The profile list in <paramref name="json"/>, or null when it is empty or not a profile list.</summary>
        private static List<ClickProfile> TryParseProfiles(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) { return null; }
            try { return JsonSerializer.Deserialize<List<ClickProfile>>(json, Options); }
            catch { return null; }
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
                    p.Sanitize(imported: false);    // restored later, it is loaded like any profile
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
            return RestoreFromRecycleBin(_recycleBin.FindLast(p =>
                p != null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Restores exactly this binned profile. By name was ambiguous: delete "Fishing", make a
        /// new "Fishing" and delete that too, and the bin holds two — choosing the older one in
        /// the bin brought back the newer.
        /// </summary>
        public ClickProfile RestoreFromRecycleBin(ClickProfile binned)
        {
            if (binned == null || !_recycleBin.Remove(binned))
            {
                return null;
            }
            binned.Name = MakeUniqueName(binned.Name); // avoid clashing with a new same-name profile
            _profiles.Add(binned);
            return binned;
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
        /// Records that a run just started with a profile: bumps its use count and last-used
        /// timestamp. Call when a run starts (see ClickProfile.TimesUsed for why not on switch).
        /// </summary>
        public void MarkUsed(string name)
        {
            var p = GetByName(name);
            if (p == null) return;
            p.TimesUsed++;
            p.LastUsedUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Adds clicking runtime (seconds) to a profile's cumulative total. A profile deleted
        /// while its run was going is found in the recycle bin, so the run is still there if it
        /// is restored — it used to be dropped, the name matching nothing any more.
        /// </summary>
        public void AddRuntime(string name, long seconds)
        {
            if (seconds <= 0) return;
            var p = GetByName(name) ?? _recycleBin.FindLast(b =>
                b != null && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
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
            // Every message in the user's language. The two with a detail used to be English
            // glued to the detail, which no table can hold, so they stayed English everywhere.
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = Utils.Localization.T("The file is empty.");
                return null;
            }
            try
            {
                var p = JsonSerializer.Deserialize<ClickProfile>(json, Options);
                if (p == null)
                {
                    error = Utils.Localization.T("The file isn't a valid Tempo profile.");
                    return null;
                }
                // Someone else's file: everything back inside what the editors can show,
                // unknown enum values included — see ClickProfile.Sanitize.
                p.Sanitize();
                if (!string.IsNullOrEmpty(p.Name) && p.Name.Length > ClickProfile.MaxNameLength)
                {
                    p.Name = p.Name.Substring(0, ClickProfile.MaxNameLength).TrimEnd();
                }
                string vErr = p.Validate();
                if (vErr != null)
                {
                    error = Utils.Localization.F("The profile failed validation: {0}", Utils.Localization.T(vErr));
                    return null;
                }
                return p;
            }
            catch (Exception ex)
            {
                error = Utils.Localization.F("Couldn't read the profile: {0}", ex.Message);
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
            if (clash != null && conflict == ImportConflict.Overwrite)
            {
                // Replacing updates the SAME entry in this library: its history and star stay,
                // and the setup it had goes to the recycle bin — "Replace" used to drop it for
                // good, the one way to lose a profile in Tempo that no undo could reach.
                int idx = _profiles.IndexOf(clash);
                incoming.Name = clash.Name; // keep the exact name
                incoming.CreatedUtc = clash.CreatedUtc;
                incoming.LastUsedUtc = clash.LastUsedUtc;
                incoming.TimesUsed = clash.TimesUsed;
                incoming.TotalRuntimeSeconds = clash.TotalRuntimeSeconds;
                incoming.Favorite = clash.Favorite;
                // So do the keybinds and look saved with it on this PC, unless the file brings its
                // own (the user was asked about those): a file without them says nothing about them.
                if (incoming.Keybinds == null) { incoming.Keybinds = clash.Keybinds?.Clone(); }
                if (incoming.AppSettings == null) { incoming.AppSettings = clash.AppSettings?.Clone(); }
                _profiles[idx] = incoming;
                _recycleBin.Add(clash);
                while (_recycleBin.Count > MaxRecycleBin)
                {
                    _recycleBin.RemoveAt(0);
                }
                return incoming;
            }

            // A new entry: its history starts here, and the star is this user's to give. The file
            // carried the exporting PC's use count, run time and dates.
            incoming.StartHistoryNow();
            incoming.Favorite = false;
            if (clash != null)
            {
                // Rename: give the import a unique name and keep both.
                incoming.Name = MakeUniqueName(incoming.Name);
            }
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
            // In the user's language ("Fishing (copia)"); the English form is unchanged.
            copy.Name = MakeUniqueName(Utils.Localization.F("{0} Copy", src.Name));

            // A copy is a NEW profile. Clone() carries the original's history across (the
            // engine needs an exact copy), so a duplicate used to show "used 48× · 6h 12m"
            // and sort as recently used before it had ever run once.
            copy.StartHistoryNow();

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
