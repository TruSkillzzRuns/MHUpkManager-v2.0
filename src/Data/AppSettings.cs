using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MHUpkManager.Data
{
    /// <summary>
    /// Persistent application settings serialized to
    /// %AppData%\MHUpkManager\settings.json.
    /// </summary>
    public sealed class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MHUpkManager",
            "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // ---- persisted fields -----------------------------------------------

        /// <summary>Root folder of the MHO game installation (contains CookedPC\*.upk).</summary>
        public string GamePath { get; set; }

        /// <summary>Path to the .mpk package index file built by UpkIndexGenerator.</summary>
        public string IndexPath { get; set; }

        /// <summary>Last UPK file opened in the SWF editor tab.</summary>
        public string LastUpkPath { get; set; }

        // ---- singleton ------------------------------------------------------

        private static AppSettings _instance;
        public static AppSettings Instance => _instance ??= Load();

        // ---- I/O ------------------------------------------------------------

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                }
            }
            catch { /* corrupt/missing — start fresh */ }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
            }
            catch { /* best-effort */ }
        }
    }
}
