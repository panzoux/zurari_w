using System.Text.Json;

namespace Zurari.Runtime;

/// <summary>Persisted user preferences. All values optional; absent means "use the default".</summary>
/// <param name="PreviewWidth">Width of the preview pane, in device-independent pixels.</param>
/// <param name="PinnedPaths">Places the user pinned into the drive pane, in the order added.</param>
/// <param name="CollapsedGroups">
/// Sections of the drive pane the user had collapsed. Names, not indices, so adding a section does
/// not silently collapse a different one.
/// </param>
public sealed record UserSettings(
    double? PreviewWidth = null,
    IReadOnlyList<string>? PinnedPaths = null,
    IReadOnlyList<string>? CollapsedGroups = null);

/// <summary>
/// Loads and saves <see cref="UserSettings"/> as JSON, by default under <c>%APPDATA%\zurari</c>.
/// Lives in Runtime because it is file I/O (Zurari.App is banned from touching the filesystem
/// directly). Best-effort on both ends: a missing or corrupt file loads as defaults, and a failed
/// save is silently dropped — preferences are never worth crashing or blocking the app for.
/// </summary>
/// <remarks>
/// <para>
/// The directory is a constructor argument rather than a fixed location, so a test can point the
/// store at a temporary folder. It used to resolve its path from a static property, which meant its
/// own tests wrote to the real <c>%APPDATA%</c> and save-restored around it — and the two cases most
/// worth covering, a corrupt file and a missing one, could not be tested at all without risking a
/// person's actual preferences. Both reference implementations already do this: rwf's
/// <c>ConfigManager::with_paths</c>, and zurari's <c>_pathOverride</c> on its bookmark store.
/// </para>
/// <para>
/// One file, deliberately. rwf ends up with six because it has six matured concerns; splitting
/// before there is a second one just makes two places to look. Keybindings are the likely first
/// reason to split.
/// </para>
/// </remarks>
public sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string? directory;

    /// <summary>
    /// Creates a store over <paramref name="directory"/>, defaulting to <c>%APPDATA%\zurari</c>.
    /// </summary>
    public UserSettingsStore(string? directory = null)
    {
        this.directory = directory ?? DefaultDirectory();
    }

    private string? SettingsPath => directory is null ? null : Path.Combine(directory, "settings.json");

    private static string? DefaultDirectory()
    {
        try
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return roaming.Length == 0 ? null : Path.Combine(roaming, "zurari");
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the saved preferences, or defaults when there are none, the file is unreadable, or its
    /// contents are not valid settings.
    /// </summary>
    public UserSettings Load()
    {
        if (SettingsPath is not { } path)
        {
            return new UserSettings();
        }

        try
        {
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

    /// <summary>Writes <paramref name="settings"/>, replacing whatever was stored. Best effort.</summary>
    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (SettingsPath is not { } path)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory!);

            // Written alongside and moved into place. A crash or a full disk partway through a
            // direct write leaves a truncated file, which loads as defaults - silently discarding
            // every preference the user had rather than the one being saved.
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Preferences are best-effort; losing one save is acceptable.
        }
    }
}
