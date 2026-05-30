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
            WriteIndented = true
        };

        public static string GetSettingsDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoClicker");
        }

        public static string GetSettingsPath()
        {
            return Path.Combine(GetSettingsDirectory(), "settings.json");
        }

        public static AppSettings Load()
        {
            try
            {
                string path = GetSettingsPath();
                if (!File.Exists(path))
                {
                    Logger.Info("No settings file found; using defaults.");
                    return AppSettings.CreateDefault();
                }

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return AppSettings.CreateDefault();
                }

                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (settings == null)
                {
                    return AppSettings.CreateDefault();
                }

                settings.EnsureConsistency();
                Logger.Info("Settings loaded.");
                return settings;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load settings; falling back to defaults.", ex);
                PersistenceHelper.BackupCorruptFile(GetSettingsPath());
                return AppSettings.CreateDefault();
            }
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
                    Logger.Info("Settings saved.");
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
                Logger.Info("Settings exported to " + path);
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
                Logger.Info("Settings imported from " + path);
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
