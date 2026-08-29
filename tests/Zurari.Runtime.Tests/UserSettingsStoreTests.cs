using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class UserSettingsStoreTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zurari-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Save_then_Load_round_trips_the_preview_width()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new UserSettingsStore(Path.Combine(dir, "settings"));

            store.Save(new UserSettings(PreviewWidth: 321.5));

            Assert.Equal(321.5, new UserSettingsStore(Path.Combine(dir, "settings")).Load().PreviewWidth);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_returns_defaults_when_nothing_was_ever_saved()
    {
        var dir = CreateTempDir();
        try
        {
            // Previously untestable: the store wrote to the real %APPDATA%, so a test could not
            // assert on "no settings file" without deleting the user's own preferences.
            var settings = new UserSettingsStore(Path.Combine(dir, "never-written")).Load();

            Assert.NotNull(settings);
            Assert.Null(settings.PreviewWidth);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_returns_defaults_when_the_file_is_corrupt()
    {
        var dir = CreateTempDir();
        try
        {
            var settingsDir = Path.Combine(dir, "settings");
            Directory.CreateDirectory(settingsDir);
            File.WriteAllText(Path.Combine(settingsDir, "settings.json"), "{ this is not json");

            // The other case that could not be covered before. A corrupt file must degrade to
            // defaults rather than take the app down on startup.
            var settings = new UserSettingsStore(settingsDir).Load();

            Assert.NotNull(settings);
            Assert.Null(settings.PreviewWidth);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Saving_replaces_what_was_there_before()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new UserSettingsStore(Path.Combine(dir, "settings"));
            store.Save(new UserSettings(PreviewWidth: 100));
            store.Save(new UserSettings(PreviewWidth: 200));

            Assert.Equal(200, store.Load().PreviewWidth);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Saving_leaves_no_temporary_file_behind()
    {
        var dir = CreateTempDir();
        try
        {
            var settingsDir = Path.Combine(dir, "settings");
            new UserSettingsStore(settingsDir).Save(new UserSettings(PreviewWidth: 42));

            // The write goes to a temp name and is moved into place, so that a crash partway cannot
            // leave a truncated file that would load as defaults and lose every preference.
            Assert.Equal(["settings.json"], Directory.GetFiles(settingsDir).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_store_over_an_unusable_directory_degrades_to_defaults_without_throwing()
    {
        var dir = CreateTempDir();
        try
        {
            // A directory path that cannot exist, because a file occupies it.
            var file = Path.Combine(dir, "occupied");
            File.WriteAllText(file, "x");
            var store = new UserSettingsStore(Path.Combine(file, "settings"));

            store.Save(new UserSettings(PreviewWidth: 123));

            Assert.Null(store.Load().PreviewWidth);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
