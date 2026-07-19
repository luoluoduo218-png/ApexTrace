using System.IO;
using System.Text.Json;

namespace ApexTrace.App;

public sealed record AppPreferences(
    bool UseAxisPedalDisplay = false,
    double RealtimeTrackPanelRatio = 0.54,
    double ReplayTrackPanelRatio = 0.60,
    string? LmuInstallationPath = null,
    string Language = LocalizationManager.ChineseLanguage);

public sealed class AppPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;

    public AppPreferencesStore(string path) => _path = path;

    public AppPreferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppPreferences();
            return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(_path), JsonOptions)
                ?? new AppPreferences();
        }
        catch (IOException)
        {
            return new AppPreferences();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppPreferences();
        }
        catch (JsonException)
        {
            return new AppPreferences();
        }
    }

    public void Save(AppPreferences preferences)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
        File.Move(temporaryPath, _path, true);
    }
}
