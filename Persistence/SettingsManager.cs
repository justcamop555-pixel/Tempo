using System;
using System.IO;
using System.Text.Json;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.Persistence
{
    /// <summary>
    /// Loads and saves <see cref="AppSettings"/> as JSON in the user's local
    /// application data folder. All operations are defensive: a corrupt or missing
    /// file simply yields default settings rather than throwing.
    /// </summary>
    public static class SettingsManager
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,

            // Accept a number that arrives quoted — "20" as well as 20.
            //
            // Without this, ONE mistyped field discards the WHOLE file: the loader
            // throws, Load() falls back to CreateDefault(), and every preference the
            // user ever set is gone. Seen for real on this machine — a single
            // "WindowLeft": "-1500" (a script that quoted the value) reset the theme,
            // the wallpaper, the caption engine and every notification setting at once.
            //
            // People do hand-edit this file, and quoting a number is the easiest
            // mistake to make in JSON. Tempo still WRITES numbers unquoted; this only
            // widens what it will read.
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        private static string _resolvedDir;

        public static string GetSettingsDirectory()
        {
            // Resolve once and cache so every caller agrees on the same folder.
            if (_resolvedDir != null)
            {
                return _resolvedDir;
            }

            string roaming = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoClicker");

            // Tempo always keeps its settings, profiles, macros and stats in
            // %LOCALAPPDATA%\AutoClicker - whether it was installed or is run as a
            // portable copy. This is the single, reliable, always-writable location, so
            // saving can't silently fail under Program Files or a read-only/USB path.
            //
            // Earlier builds put a portable copy's data in a "Data" folder beside
            // Tempo.exe. If such a folder exists and we haven't populated AppData yet,
            // copy its contents across once so upgrading users keep their settings.
            try
            {
                if (!File.Exists(Path.Combine(roaming, "settings.json")))
                {
                    MigrateLegacyPortableData(roaming);
                }
            }
            catch
            {
                // Migration is best-effort; never block startup on it.
            }

            _resolvedDir = roaming;
            return _resolvedDir;
        }

        /// <summary>
        /// One-time, non-destructive migration of a pre-1.0.194 portable "Data" folder
        /// (kept next to Tempo.exe) into %LOCALAPPDATA%\AutoClicker. Copies files only;
        /// never overwrites anything already in AppData, and leaves the old folder intact.
        /// </summary>
        private static void MigrateLegacyPortableData(string roaming)
        {
            string exeDir = Utils.DeploymentInfo.ExecutableDirectory;
            if (string.IsNullOrEmpty(exeDir))
            {
                return;
            }
            string legacy = Path.Combine(exeDir, "Data");
            // Only migrate a folder that actually holds Tempo's settings.
            if (!Directory.Exists(legacy) || !File.Exists(Path.Combine(legacy, "settings.json")))
            {
                return;
            }

            Directory.CreateDirectory(roaming);
            int copied = 0;
            foreach (string src in Directory.GetFiles(legacy, "*", SearchOption.AllDirectories))
            {
                try
                {
                    string rel = src.Substring(legacy.Length).TrimStart('\\', '/');
                    string dest = Path.Combine(roaming, rel);
                    if (File.Exists(dest))
                    {
                        continue; // don't clobber existing AppData
                    }
                    string destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    File.Copy(src, dest, false);
                    copied++;
                }
                catch
                {
                    // Skip any single file that won't copy; keep migrating the rest.
                }
            }
            if (copied > 0)
            {
                Logger.Info("[Settings] migrated " + copied + " file(s) from a legacy portable Data folder into " + roaming + ".");
            }
        }

        public static string GetSettingsPath()
        {
            return Path.Combine(GetSettingsDirectory(), "settings.json");
        }

        /// <summary>Deserialises settings, or null when the text is not usable.</summary>
        private static AppSettings TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try { return JsonSerializer.Deserialize<AppSettings>(json, Options); }
            catch { return null; }
        }

        public static AppSettings Load()
        {
            string path = GetSettingsPath();
            try
            {
                if (!File.Exists(path))
                {
                    Logger.Info("[Settings] no settings file found; using defaults.");
                    return AppSettings.CreateDefault();
                }

                AppSettings settings = TryParse(File.ReadAllText(path));
                if (settings != null)
                {
                    return Accept(settings, "[Settings] loaded.");
                }

                // The main file is unreadable. Before resetting everything, try the
                // copy kept from the previous save — this is the case a hard freeze
                // produces, where the file survives at full length with nothing but
                // zeroes in it, and where falling straight back to defaults silently
                // discards every preference the user has.
                string previous = PersistenceHelper.ReadPreviousIfUsable(
                    path, text => TryParse(text) != null);
                AppSettings recovered = TryParse(previous);
                if (recovered != null)
                {
                    PersistenceHelper.BackupCorruptFile(path);
                    return Accept(recovered, "[Settings] recovered from the previous save.");
                }

                // An empty or unparseable file used to return defaults without a word
                // — no log line and no preserved copy, so a silent reset looked exactly
                // like a first run.
                Logger.Warn("[Settings] the settings file could not be read and there is " +
                            "no usable previous copy; starting from defaults.");
                PersistenceHelper.BackupCorruptFile(path);
                return AppSettings.CreateDefault();
            }
            catch (Exception ex)
            {
                Logger.Error("[Settings] failed to load settings; falling back to defaults.", ex);
                PersistenceHelper.BackupCorruptFile(path);
                return AppSettings.CreateDefault();
            }
        }

        private static AppSettings Accept(AppSettings settings, string message)
        {
            settings.EnsureConsistency();
            Logger.Info(message);
            // Localization.Current is set from THIS file's result a moment later, so
            // the very first paint may still be English. The splash repaints while it
            // is up, so it corrects itself before anyone reads it.
            UI.SplashForm.Report(0, Utils.Localization.T("settings ok"));
            return settings;
        }

        public static bool Save(AppSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            try
            {
                string json = JsonSerializer.Serialize(settings, Options);
                bool ok = PersistenceHelper.WriteAtomic(GetSettingsPath(), json);
                if (ok)
                {
                    Logger.Info("[Settings] saved.");
                }
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save settings.", ex);
                return false;
            }
        }

        /// <summary>Writes the given settings to an arbitrary file (for backup/export).</summary>
        public static bool ExportTo(AppSettings settings, string path)
        {
            if (settings == null || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                string json = JsonSerializer.Serialize(settings, Options);
                File.WriteAllText(path, json);
                Logger.Info("[Settings] exported to " + path);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to export settings.", ex);
                return false;
            }
        }

        /// <summary>Reads settings from an arbitrary file. Returns null on failure.</summary>
        public static AppSettings ImportFrom(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (settings == null)
                {
                    return null;
                }

                settings.EnsureConsistency();
                Logger.Info("[Settings] imported from " + path);
                return settings;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to import settings.", ex);
                return null;
            }
        }
    }
}
