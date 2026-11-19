using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class UserSettingsStoreTests
{
    private static string CreateTempDir() => TempDirectory.Create("zurari-settings");

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
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// The bug this exists for: closing the window saved the preview width as a fresh record, so
    /// every pinned place and collapsed section was erased on the way out. Nothing caught it because
    /// no test ever wrote two preferences from two different callers.
    /// </summary>
    [Fact]
    public void Updating_one_preference_leaves_the_others_alone()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new UserSettingsStore(Path.Combine(dir, "settings"));
            store.Save(new UserSettings(PinnedPaths: [@"C:\work"], CollapsedGroups: ["drives"]));

            store.Update(s => s with { PreviewWidth = 321.5 });

            var reloaded = new UserSettingsStore(Path.Combine(dir, "settings")).Load();
            Assert.Equal(321.5, reloaded.PreviewWidth);
            Assert.Equal([@"C:\work"], reloaded.PinnedPaths);
            Assert.Equal(["drives"], reloaded.CollapsedGroups);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Updating_before_anything_was_saved_starts_from_defaults()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new UserSettingsStore(Path.Combine(dir, "settings"));

            store.Update(s => s with { PreviewWidth = 200 });

            var reloaded = new UserSettingsStore(Path.Combine(dir, "settings")).Load();
            Assert.Equal(200, reloaded.PreviewWidth);
            Assert.Null(reloaded.PinnedPaths);
        }
        finally
        {
            TempDirectory.Delete(dir);
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
            TempDirectory.Delete(dir);
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
            TempDirectory.Delete(dir);
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
            TempDirectory.Delete(dir);
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
            Assert.Equal(["settings.json"], Array.ConvertAll(Directory.GetFiles(settingsDir), Path.GetFileName));
        }
        finally
        {
            TempDirectory.Delete(dir);
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
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Settings_left_in_the_legacy_directory_are_loaded_until_the_new_one_has_its_own()
    {
        var dir = CreateTempDir();
        try
        {
            var legacyDir = Path.Combine(dir, "zurari");
            new UserSettingsStore(legacyDir).Save(new UserSettings(PreviewWidth: 77, PinnedPaths: [@"C:\pinned"]));

            var store = new UserSettingsStore(Path.Combine(dir, "zurari_w"), legacyDir);

            var loaded = store.Load();
            Assert.Equal(77, loaded.PreviewWidth);
            Assert.Equal([@"C:\pinned"], loaded.PinnedPaths!);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Saving_writes_to_the_new_directory_and_leaves_the_legacy_file_alone()
    {
        var dir = CreateTempDir();
        try
        {
            var legacyDir = Path.Combine(dir, "zurari");
            var legacy = new UserSettingsStore(legacyDir);
            legacy.Save(new UserSettings(PreviewWidth: 77, PinnedPaths: [@"C:\pinned"]));
            var store = new UserSettingsStore(Path.Combine(dir, "zurari_w"), legacyDir);

            // The first save carries the legacy values across, because Update reads before it writes.
            store.Update(s => s with { PreviewWidth = 300 });

            Assert.Equal(300, store.Load().PreviewWidth);
            Assert.Equal([@"C:\pinned"], store.Load().PinnedPaths!);
            Assert.Equal(77, legacy.Load().PreviewWidth);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// The UI thread and the workers share one store. Updates from several threads must each land:
    /// a read-modify-write increment loses counts if two of them read the same file.
    /// </summary>
    [Fact]
    public void Concurrent_updates_lose_nothing()
    {
        const int threads = 6;
        const int perThread = 20;
        var dir = CreateTempDir();
        try
        {
            var store = new UserSettingsStore(Path.Combine(dir, "settings"));
            store.Save(new UserSettings(PreviewWidth: 0));
            var workers = new Thread[threads];
            for (var t = 0; t < threads; t++)
            {
                var id = t;
                workers[t] = new Thread(() =>
                {
                    for (var i = 0; i < perThread; i++)
                    {
                        store.Update(s => s with { PreviewWidth = (s.PreviewWidth ?? 0) + 1 });
                    }

                    store.Update(s => id % 2 == 0 ? s with { ShowHidden = true } : s with { DirectoriesFirst = true });
                });
                workers[t].Start();
            }

            foreach (var worker in workers)
            {
                worker.Join();
            }

            var settings = store.Load();
            Assert.Equal(threads * perThread, settings.PreviewWidth);
            Assert.True(settings.ShowHidden);
            Assert.True(settings.DirectoriesFirst);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Once_the_new_directory_has_settings_the_legacy_ones_are_ignored()
    {
        var dir = CreateTempDir();
        try
        {
            var legacyDir = Path.Combine(dir, "zurari");
            var newDir = Path.Combine(dir, "zurari_w");
            new UserSettingsStore(legacyDir).Save(new UserSettings(PreviewWidth: 77));
            new UserSettingsStore(newDir).Save(new UserSettings(PreviewWidth: 300));

            Assert.Equal(300, new UserSettingsStore(newDir, legacyDir).Load().PreviewWidth);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Old_single_view_fields_read_as_the_top_panes_view_and_survive_the_first_save()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "settings.json"),
                """{ "PreviewWidth": 321, "PinnedPaths": ["C:/p"], "SortMode": "Size", "ShowHidden": true }""");
            var store = new UserSettingsStore(dir);

            var top = Assert.Single(store.Load().EffectivePaneViews());
            Assert.Equal("Size", top.SortMode);
            Assert.True(top.ShowHidden);

            store.Update(s => s.WithPaneViews([new PaneViewSettings("Name", false, true, false)]));

            var saved = store.Load();
            Assert.Equal(321, saved.PreviewWidth);
            Assert.Equal(["C:/p"], saved.PinnedPaths);
            Assert.Equal("Size", saved.SortMode);
            Assert.Equal("Name", Assert.Single(saved.EffectivePaneViews()).SortMode);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Saving_fewer_views_keeps_the_stored_bottom_one()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new UserSettingsStore(dir);
            store.Update(s => s.WithPaneViews([new PaneViewSettings("Name", false, true, false), new PaneViewSettings("Size", true, true, true)]));

            store.Update(s => s.WithPaneViews([new PaneViewSettings("Modified", false, true, false)]));

            var views = store.Load().EffectivePaneViews();
            Assert.Equal(2, views.Count);
            Assert.Equal("Modified", views[0].SortMode);
            Assert.Equal("Size", views[1].SortMode);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void The_split_fields_round_trip()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new UserSettingsStore(dir);
            store.Update(s => s with { SplitOpen = true, SplitRatio = 0.3 });

            var loaded = store.Load();
            Assert.True(loaded.SplitOpen);
            Assert.Equal(0.3, loaded.SplitRatio);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void A_null_pane_view_entry_reads_as_the_default_view()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "PaneViews": [null, {}] }""");

            var views = new UserSettingsStore(dir).Load().EffectivePaneViews();

            Assert.Equal(2, views.Count);
            foreach (var view in views)
            {
                Assert.Equal(Zurari.Core.ViewOptions.Default, WorkerRuntime.ReadViewOptions(view));
            }
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }
}
