using System.Text.Json;

namespace MHUpkManager.UI;

internal sealed class UpkBrowsePreferences
{
    public string DefaultUpkFolder { get; set; } = string.Empty;
    public List<string> RecentUpkPaths { get; set; } = [];
}

internal sealed class UpkBrowsePreferencesStore
{
    private const int MaxRecentEntries = 12;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _preferencesPath;

    public UpkBrowsePreferencesStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MHUpkManager");
        Directory.CreateDirectory(root);
        _preferencesPath = Path.Combine(root, "upk-browse-preferences.json");
    }

    public UpkBrowsePreferences Load()
    {
        try
        {
            if (!File.Exists(_preferencesPath))
                return new UpkBrowsePreferences();

            UpkBrowsePreferences preferences = JsonSerializer.Deserialize<UpkBrowsePreferences>(File.ReadAllText(_preferencesPath), JsonOptions);
            return preferences ?? new UpkBrowsePreferences();
        }
        catch
        {
            return new UpkBrowsePreferences();
        }
    }

    public void Save(UpkBrowsePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        File.WriteAllText(_preferencesPath, JsonSerializer.Serialize(preferences, JsonOptions));
    }

    public UpkBrowsePreferences RegisterOpenedUpk(UpkBrowsePreferences preferences, string filePath)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (string.IsNullOrWhiteSpace(filePath))
            return preferences;

        string fullPath = Path.GetFullPath(filePath);
        preferences.RecentUpkPaths.RemoveAll(path => string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase));
        preferences.RecentUpkPaths.Insert(0, fullPath);
        if (preferences.RecentUpkPaths.Count > MaxRecentEntries)
            preferences.RecentUpkPaths.RemoveRange(MaxRecentEntries, preferences.RecentUpkPaths.Count - MaxRecentEntries);

            string folder = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                preferences.DefaultUpkFolder = folder;

        return preferences;
    }

    public UpkBrowsePreferences ClearRecents(UpkBrowsePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.RecentUpkPaths.Clear();
        return preferences;
    }
}
