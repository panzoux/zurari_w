using System.Text.Json;

namespace Zurari.Runtime;

/// <summary>Persisted user preferences. All values optional; absent means "use the default".</summary>
/// <param name="PreviewWidth">Width of the preview pane, in device-independent pixels.</param>
/// <param name="PinnedPaths">Places the user pinned into the drive pane, in the order added.</param>
/// <param name="CollapsedGroups">
/// Sections of the drive pane the user had collapsed. Names, not indices, so adding a section does
/// not silently collapse a different one.
/// </param>
/// <param name="SortMode">Name of the <c>Zurari.Core.SortMode</c> last chosen.</param>
/// <param name="SortDescending">Whether that mode was reversed.</param>
/// <param name="DirectoriesFirst">Whether folders were kept above files.</param>
/// <param name="ShowHidden">Whether hidden and system entries were being shown.</param>
/// <param name="SplitOpen">Whether the window was showing two panes.</param>
/// <param name="SplitRatio">The top pane's share of the height when it was split, 0..1.</param>
/// <param name="PaneViews">
/// Each pane position's view options, top first. Entries beyond the open panes are kept, so closing
/// the split does not forget the bottom pane's setup.
/// </param>
public sealed record UserSettings(
    double? PreviewWidth = null,
    IReadOnlyList<string>? PinnedPaths = null,
    IReadOnlyList<string>? CollapsedGroups = null,
    string? SortMode = null,
    bool? SortDescending = null,
    bool? DirectoriesFirst = null,
    bool? ShowHidden = null,
    bool? SplitOpen = null,
    double? SplitRatio = null,
    IReadOnlyList<PaneViewSettings>? PaneViews = null)
{
    /// <summary>
    /// The stored per-position views. A file from before the split has none; its single set of
    /// fields is what the one pane used, so it reads as the top pane's.
    /// </summary>
    public IReadOnlyList<PaneViewSettings> EffectivePaneViews() =>
        PaneViews is { Count: > 0 } views
            ? views
            : [new PaneViewSettings(SortMode, SortDescending, DirectoriesFirst, ShowHidden)];

    /// <summary>
    /// Stores <paramref name="topFirst"/>, keeping any stored positions beyond it: closing the split
    /// saves one view, and that must not forget how the bottom pane was set up.
    /// </summary>
    public UserSettings WithPaneViews(IReadOnlyList<PaneViewSettings> topFirst)
    {
        ArgumentNullException.ThrowIfNull(topFirst);
        var existing = EffectivePaneViews();
        var merged = new List<PaneViewSettings>(topFirst);
        for (var i = topFirst.Count; i < existing.Count; i++)
        {
            merged.Add(existing[i]);
        }

        return this with { PaneViews = merged };
    }
}

/// <summary>One pane position's view options, as stored.</summary>
public sealed record PaneViewSettings(string? SortMode, bool? SortDescending, bool? DirectoriesFirst, bool? ShowHidden);

/// <summary>
/// Loads and saves <see cref="UserSettings"/> as JSON, by default under <c>%APPDATA%\zurari_w</c>.
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

    private const string SettingsFileName = "settings.json";

    private readonly string? directory;
    private readonly string? legacyDirectory;

    /// <summary>
    /// Serialises <see cref="Update"/>'s read-modify-write and <see cref="Save"/>'s write. The UI
    /// thread (preview width) and worker threads (view options, collapsed groups, pins) share one
    /// store; without this two updates could lose one another's field, and both write the same
    /// <c>.tmp</c> file.
    /// </summary>
    private readonly object gate = new();

    /// <summary>
    /// Creates a store over <paramref name="directory"/>, defaulting to <c>%APPDATA%\zurari_w</c>.
    /// </summary>
    /// <param name="directory">Where settings are read and written.</param>
    /// <param name="legacyDirectory">
    /// Where settings were kept before, read only while <paramref name="directory"/> has none of its
    /// own. Defaults to <c>%APPDATA%\zurari</c> when <paramref name="directory"/> is defaulted too.
    /// </param>
    /// <remarks>
    /// zurari_w used to share <c>%APPDATA%\zurari</c> with zurari, the TUI filer it succeeds, which
    /// keeps its own <c>bookmarks.txt</c> and <c>operations.log</c> there. The legacy file is only ever
    /// read: the first save lands in the new directory with the old values carried across by
    /// <see cref="Update"/>, and from then on the new file wins. Nothing is moved or deleted, because
    /// that directory is not only ours.
    /// </remarks>
    public UserSettingsStore(string? directory = null, string? legacyDirectory = null)
    {
        this.directory = directory ?? AppDataDirectory("zurari_w");
        this.legacyDirectory = legacyDirectory ?? (directory is null ? AppDataDirectory("zurari") : null);
    }

    private string? SettingsPath => directory is null ? null : Path.Combine(directory, SettingsFileName);

    private string? LegacySettingsPath =>
        legacyDirectory is null ? null : Path.Combine(legacyDirectory, SettingsFileName);

    private static string? AppDataDirectory(string name)
    {
        try
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return roaming.Length == 0 ? null : Path.Combine(roaming, name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the saved preferences, or defaults when there are none, the file is unreadable, or its
    /// contents are not valid settings. Falls back to the legacy directory while the current one has
    /// no settings file.
    /// </summary>
    public UserSettings Load()
    {
        if (SettingsPath is not { } current)
        {
            return new UserSettings();
        }

        try
        {
            var path = !File.Exists(current) && LegacySettingsPath is { } legacy && File.Exists(legacy)
                ? legacy
                : current;
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

    /// <summary>
    /// Changes one preference and writes the result, leaving every other stored value alone.
    /// </summary>
    /// <remarks>
    /// The way to save a single preference. <see cref="Save"/> replaces the whole file, so a caller
    /// holding only the value it cares about silently erases the rest - which is exactly what
    /// happened: closing the window saved the preview width as a fresh record, and every pinned
    /// place and collapsed section the user had disappeared with it. Read-modify-write here means
    /// the caller cannot forget the fields it does not know about.
    /// </remarks>
    public void Update(Func<UserSettings, UserSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (gate)
        {
            Save(change(Load()));
        }
    }

    /// <summary>
    /// Writes <paramref name="settings"/>, replacing whatever was stored. Best effort.
    /// </summary>
    /// <remarks>
    /// Internal, so that <see cref="Update"/> is the only way to write a preference from anywhere
    /// else. This replaces the file wholesale: a caller holding one value erases every other, which
    /// is not a mistake worth leaving available - it is the one that lost every pinned place and
    /// collapsed section each time the window closed.
    /// </remarks>
    internal void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (SettingsPath is not { } path)
        {
            return;
        }

        lock (gate)
        {
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
}
