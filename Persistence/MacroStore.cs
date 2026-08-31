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
    /// Stores recorded macros in a JSON file so they survive between sessions.
    /// </summary>
    public sealed class MacroStore
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly List<Macro> _macros = new List<Macro>();

        public IReadOnlyList<Macro> Macros => _macros;

        /// <summary>Reorders the macros in place using the given comparison and saves.</summary>
        public void SortBy(Comparison<Macro> comparison)
        {
            if (comparison == null)
            {
                return;
            }

            _macros.Sort(comparison);
            Save();
        }

        public static string GetMacrosPath()
        {
            return Path.Combine(SettingsManager.GetSettingsDirectory(), "macros.json");
        }

        public void Load()
        {
            _macros.Clear();

            try
            {
                string path = GetMacrosPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var loaded = JsonSerializer.Deserialize<List<Macro>>(json, Options);
                        if (loaded != null)
                        {
                            foreach (var m in loaded)
                            {
                                if (m != null)
                                {
                                    if (m.Actions == null)
                                    {
                                        m.Actions = new List<MacroAction>();
                                    }
                                    _macros.Add(m);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load macros.", ex);
                PersistenceHelper.BackupCorruptFile(GetMacrosPath());
                _macros.Clear();
            }

            Logger.Info($"[Macro] loaded {_macros.Count} macro(s).");
            UI.SplashForm.Report(1, _macros.Count + " " +
                Utils.Localization.T(_macros.Count == 1 ? "macro" : "macros"));
        }

        public bool Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(_macros, Options);
                return PersistenceHelper.WriteAtomic(GetMacrosPath(), json);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save macros.", ex);
                return false;
            }
        }

        public Macro GetByName(string name)
        {
            return _macros.FirstOrDefault(m =>
                string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public Macro Add(Macro macro)
        {
            if (macro == null)
            {
                throw new ArgumentNullException(nameof(macro));
            }

            macro.Name = MakeUniqueName(macro.Name);
            _macros.Add(macro);
            return macro;
        }

        public bool Remove(string name)
        {
            var existing = GetByName(name);
            if (existing == null)
            {
                return false;
            }

            _macros.Remove(existing);
            return true;
        }

        /// <summary>Renames a macro. Returns false on a name clash or if missing.</summary>
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

            var clash = GetByName(newName);
            if (clash != null && !ReferenceEquals(clash, existing))
            {
                return false;
            }

            existing.Name = newName;
            return true;
        }

        /// <summary>Creates a copy of a macro under a unique name.</summary>
        public Macro Duplicate(string name)
        {
            var src = GetByName(name);
            if (src == null)
            {
                return null;
            }

            var copy = src.Clone();
            copy.Name = MakeUniqueName(src.Name + " Copy");
            // A duplicate is a fresh, never-run macro — don't inherit the original's usage
            // history (it would show a bogus "played N× · last …" and skew the play-count /
            // newest sorts). User-authored fields (Notes, speed/loops, favourite) stay.
            copy.TimesPlayed = 0;
            copy.LastPlayedUtc = null;
            copy.CreatedUtc = DateTime.UtcNow;
            _macros.Add(copy);
            return copy;
        }

        /// <summary>Replaces the stored macro that matches by name, or adds it.</summary>
        public void Update(Macro macro)
        {
            if (macro == null)
            {
                return;
            }

            for (int i = 0; i < _macros.Count; i++)
            {
                if (string.Equals(_macros[i].Name, macro.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _macros[i] = macro;
                    return;
                }
            }

            _macros.Add(macro);
        }

        /// <summary>Moves a macro up or down in the list (delta -1 or +1).</summary>
        public bool Move(string name, int delta)
        {
            int index = IndexOf(name);
            if (index < 0)
            {
                return false;
            }

            int target = index + delta;
            if (target < 0 || target >= _macros.Count)
            {
                return false;
            }

            Macro temp = _macros[index];
            _macros[index] = _macros[target];
            _macros[target] = temp;
            return true;
        }

        public int IndexOf(string name)
        {
            for (int i = 0; i < _macros.Count; i++)
            {
                if (string.Equals(_macros[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Writes a single macro to a standalone JSON file.</summary>
        public static bool ExportToFile(Macro macro, string path)
        {
            if (macro == null || string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                string json = JsonSerializer.Serialize(macro, Options);
                File.WriteAllText(path, json);
                Logger.Info($"[Macro] exported '{macro.Name}' to {path}.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to export macro.", ex);
                return false;
            }
        }

        /// <summary>
        /// Reads a macro from a JSON file and adds it under a unique name. Returns
        /// the imported macro, or null on failure.
        /// </summary>
        public Macro ImportFromFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path);
                var macro = JsonSerializer.Deserialize<Macro>(json, Options);
                if (macro == null)
                {
                    return null;
                }

                if (macro.Actions == null)
                {
                    macro.Actions = new System.Collections.Generic.List<MacroAction>();
                }

                if (string.IsNullOrWhiteSpace(macro.Name))
                {
                    macro.Name = System.IO.Path.GetFileNameWithoutExtension(path);
                }

                macro.Name = MakeUniqueName(macro.Name);
                _macros.Add(macro);
                Logger.Info($"[Macro] imported '{macro.Name}' from {path}.");
                return macro;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to import macro.", ex);
                return null;
            }
        }

        /// <summary>Writes every macro to a single JSON file (bulk backup).</summary>
        public static bool ExportAllToFile(IEnumerable<Macro> macros, string path)
        {
            try
            {
                if (macros == null || string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                var list = new List<Macro>(macros);
                string json = JsonSerializer.Serialize(list, Options);
                File.WriteAllText(path, json);
                Logger.Info($"[Macro] exported {list.Count} macro(s) to {path}.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to export all macros.", ex);
                return false;
            }
        }

        /// <summary>
        /// Imports every macro from a bulk file, giving each a unique name. Returns
        /// the number imported, or -1 on failure.
        /// </summary>
        public int ImportAllFromFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return -1;
                }

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return 0;
                }

                var list = JsonSerializer.Deserialize<List<Macro>>(json, Options);
                if (list == null)
                {
                    return -1;
                }

                int count = 0;
                foreach (Macro macro in list)
                {
                    if (macro == null)
                    {
                        continue;
                    }

                    if (macro.Actions == null)
                    {
                        macro.Actions = new List<MacroAction>();
                    }
                    if (string.IsNullOrWhiteSpace(macro.Name))
                    {
                        macro.Name = "Macro";
                    }

                    macro.Name = MakeUniqueName(macro.Name);
                    _macros.Add(macro);
                    count++;
                }

                Logger.Info($"[Macro] imported {count} macro(s) from {path}.");
                return count;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to import all macros.", ex);
                return -1;
            }
        }

        private string MakeUniqueName(string desired)
        {
            if (string.IsNullOrWhiteSpace(desired))
            {
                desired = "Macro";
            }

            if (GetByName(desired) == null)
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
            while (GetByName(candidate) != null);

            return candidate;
        }
    }
}
