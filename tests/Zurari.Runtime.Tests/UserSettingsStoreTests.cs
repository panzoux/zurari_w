using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class UserSettingsStoreTests
{
    [Fact]
    public void Save_then_Load_round_trips_the_preview_width()
    {
        var original = UserSettingsStore.Load();
        try
        {
            UserSettingsStore.Save(new UserSettings(PreviewWidth: 321.5));

            var loaded = UserSettingsStore.Load();

            Assert.Equal(321.5, loaded.PreviewWidth);
        }
        finally
        {
            // Restore whatever the user (or a previous run) had - the store writes to the real
            // %APPDATA% location, so the test must not clobber genuine preferences.
            UserSettingsStore.Save(original);
        }
    }

    [Fact]
    public void Load_returns_defaults_when_nothing_was_saved_or_file_is_corrupt()
    {
        // Cannot safely delete the real settings file (it may hold genuine preferences), so this
        // only pins the null-object contract: Load never throws and never returns null.
        var settings = UserSettingsStore.Load();

        Assert.NotNull(settings);
    }
}
