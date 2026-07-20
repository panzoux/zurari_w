using System.Text.Json;

namespace Zurari.Runtime;

/// <summary>Persisted user preferences. All values optional; absent means "use the default".</summary>
public sealed record UserSettings(double? PreviewWidth = null);

/// <summary>
/// Loads/saves <see cref="UserSettings"/> as JSON under %APPDATA%\zurari. Lives in Runtime because
/// it is file I/O (Zurari.App is banned from touching the filesystem directly). Best-effort on
/// both ends: a missing or corrupt file loads as defaults, and a failed save is silently dropped —
/// preferences are never worth crashing or blocking the app for.
/// </summary>
public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "zurari", "settings.json");

    public static UserSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                return new UserSettings();
            }

            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new UserSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UserSettings();
        }
    }

    public static void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            var path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are best-effort; losing one save is acceptable.
        }
    }
}
