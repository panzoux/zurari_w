using System.Collections.Concurrent;
using Zurari.Core;
using Zurari.Runtime;
using static Zurari.TestSupport.TestPanes;

namespace Zurari.Runtime.Tests;

public class WorkerRuntimeTests
{
    /// <summary>Every row after the "ドライブ" header, i.e. the drives themselves.</summary>
    private static List<Entry> DrivesUnderTheHeader(Msg.DirectoryLoaded loaded)
    {
        var drives = new List<Entry>();
        for (var i = 1; i < loaded.Entries.Length; i++)
        {
            drives.Add(loaded.Entries[i]);
        }

        return drives;
    }

    private static string CreateTempDir() => TempDirectory.Create("zurari-runtime-tests");

    private static Msg WaitForMsg(ConcurrentQueue<Msg> queue, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (queue.TryDequeue(out var msg))
            {
                return msg;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException("No message was posted within the timeout.");
    }

    [Fact]
    public void ReadDirectory_of_temp_dir_reports_sorted_entries()
    {
        var dir = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "zeta"));
            Directory.CreateDirectory(Path.Combine(dir, "Alpha"));
            File.WriteAllText(Path.Combine(dir, "beta.txt"), "hello");
            File.WriteAllText(Path.Combine(dir, "Aardvark.txt"), "hi!!");

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));
            runtime.Submit(new Effect.ReadDirectory(Col(0), new Location.RealDirectory(dir)));

            var msg = WaitForMsg(queue, TimeSpan.FromSeconds(10));
            var loaded = Assert.IsType<Msg.DirectoryLoaded>(msg);
            Assert.Equal(0, loaded.At.Index);
            Assert.Equal(new Location.RealDirectory(dir), loaded.Location);
            Assert.Equal(4, loaded.Entries.Length);

            // Directories first, then files; each group ordinal-ignore-case by name.
            Assert.Equal("Alpha", loaded.Entries[0].Name);
            Assert.Equal(EntryKind.Directory, loaded.Entries[0].Kind);
            Assert.Equal(-1, loaded.Entries[0].SizeBytes);

            Assert.Equal("zeta", loaded.Entries[1].Name);
            Assert.Equal(EntryKind.Directory, loaded.Entries[1].Kind);

            Assert.Equal("Aardvark.txt", loaded.Entries[2].Name);
            Assert.Equal(EntryKind.File, loaded.Entries[2].Kind);
            Assert.Equal(4, loaded.Entries[2].SizeBytes);

            Assert.Equal("beta.txt", loaded.Entries[3].Name);
            Assert.Equal(EntryKind.File, loaded.Entries[3].Kind);
            Assert.Equal(5, loaded.Entries[3].SizeBytes);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void ReadDirectory_of_nonexistent_path_reports_failure_with_column_index()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zurari-does-not-exist-" + Guid.NewGuid());
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));
        runtime.Submit(new Effect.ReadDirectory(Col(3), new Location.RealDirectory(dir)));

        var msg = WaitForMsg(queue, TimeSpan.FromSeconds(10));
        var failed = Assert.IsType<Msg.DirectoryLoadFailed>(msg);
        Assert.Equal(3, failed.At.Index);
        Assert.Equal(new Location.RealDirectory(dir), failed.Location);
        Assert.False(string.IsNullOrEmpty(failed.Error));
    }

    /// <summary>
    /// <see cref="PaneId"/>(0) is <c>default(PaneId)</c>, so every other assertion in this file - all
    /// against pane 0 - would pass even if the Runtime silently dropped <see cref="ColumnRef.Pane"/>
    /// and always reported back against the default. A non-default pane id is the only thing that
    /// can catch that.
    /// </summary>
    [Fact]
    public void ReadDirectory_round_trips_a_non_default_pane_id()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));
        var at = new ColumnRef(new PaneId(3), 2);
        runtime.Submit(new Effect.ReadDirectory(at, Location.Drives.Instance));

        var msg = WaitForMsg(queue, TimeSpan.FromSeconds(10));
        var loaded = Assert.IsType<Msg.DirectoryLoaded>(msg);
        Assert.Equal(at, loaded.At);
    }

    [Fact]
    public void ReadDirectory_of_virtual_root_reports_drives()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));
        runtime.Submit(new Effect.ReadDirectory(Col(0), Location.Drives.Instance));

        var msg = WaitForMsg(queue, TimeSpan.FromSeconds(10));
        var loaded = Assert.IsType<Msg.DirectoryLoaded>(msg);
        Assert.Equal(0, loaded.At.Index);
        Assert.Equal(Location.Drives.Instance, loaded.Location);
        Assert.NotEmpty(loaded.Entries);

        // The pane is sectioned now, so it opens with a header and every drive sits under it. The
        // header is a real entry rather than something the view adds, because the cursor lands on it.
        Assert.Equal(EntryKind.Header, loaded.Entries[0].Kind);
        Assert.Equal("ドライブ", loaded.Entries[0].Name);
        Assert.All(DrivesUnderTheHeader(loaded), e => Assert.Equal(EntryKind.Drive, e.Kind));
        Assert.All(loaded.Entries, e => Assert.Equal("drives", e.Group));

        // Every drive reads as something, and the name stays the path so rows stay distinguishable.
        Assert.All(DrivesUnderTheHeader(loaded), e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Label));
            Assert.EndsWith(@"\", e.Name, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Dispose_returns_promptly_even_with_queued_work()
    {
        var queue = new ConcurrentQueue<Msg>();
        var runtime = new WorkerRuntime(queue.Enqueue, workerCount: 1, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));
        for (var i = 0; i < 200; i++)
        {
            runtime.Submit(new Effect.ReadDirectory(Col(i), Location.Drives.Instance));
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        runtime.Dispose();
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Dispose took {sw.Elapsed}");
    }

    [Fact]
    public void MessageLoop_with_real_runtime_loads_directory_end_to_end()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "file.txt"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "sub"));

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));
            var loop = new MessageLoop(
                initial: AppState.SinglePane([new Column(new Location.RealDirectory(dir), Entries: [])]),
                runEffect: runtime.Submit);

            loop.Dispatch(new Msg.Refresh());

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline && loop.State.Focused.Columns[0].Load == LoadState.Loading)
            {
                if (queue.TryDequeue(out var msg))
                {
                    loop.Dispatch(msg);
                }
                else
                {
                    Thread.Sleep(10);
                }
            }

            Assert.Equal(LoadState.Loaded, loop.State.Focused.Columns[0].Load);
            Assert.Equal(2, loop.State.Focused.Columns[0].Entries.Length);
            Assert.Equal("sub", loop.State.Focused.Columns[0].Entries[0].Name);
            Assert.Equal("file.txt", loop.State.Focused.Columns[0].Entries[1].Name);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// Hidden entries come back in the listing, marked. Filtering them out here would mean revealing
    /// them cost a re-read, and Core could not tell "there are none" from "they are not shown".
    /// </summary>
    [Fact]
    public void A_hidden_file_is_listed_and_marked_hidden()
    {
        var dir = CreateTempDir();
        try
        {
            var plain = Path.Combine(dir, "plain.txt");
            var hidden = Path.Combine(dir, "hidden.txt");
            File.WriteAllText(plain, "x");
            File.WriteAllText(hidden, "y");
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));
            runtime.Submit(new Effect.ReadDirectory(Col(0), new Location.RealDirectory(dir)));
            var loaded = Assert.IsType<Msg.DirectoryLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));

            Assert.Equal(2, loaded.Entries.Length);
            Assert.False(Assert.Single(TestEntries.Where(loaded.Entries, e => e.Name == "plain.txt")).IsHidden);
            Assert.True(Assert.Single(TestEntries.Where(loaded.Entries, e => e.Name == "hidden.txt")).IsHidden);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Creating_a_folder_reports_the_name_it_got()
    {
        var dir = CreateTempDir();
        try
        {
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

            runtime.Submit(new Effect.CreateFolder(Col(0), new Location.RealDirectory(dir)));
            var created = Assert.IsType<Msg.FolderCreated>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));

            Assert.Equal("新しいフォルダー", created.Name);
            Assert.True(Directory.Exists(Path.Combine(dir, created.Name)));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// The name has to be decided where the directory can be looked at - which is the reason it is
    /// picked here rather than in Core.
    /// </summary>
    [Fact]
    public void A_second_folder_gets_a_name_that_is_free()
    {
        var dir = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "新しいフォルダー"));

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

            runtime.Submit(new Effect.CreateFolder(Col(0), new Location.RealDirectory(dir)));
            var created = Assert.IsType<Msg.FolderCreated>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));

            Assert.Equal("新しいフォルダー (2)", created.Name);
            Assert.True(Directory.Exists(Path.Combine(dir, created.Name)));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Creating_a_folder_where_there_is_no_directory_says_so()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

        runtime.Submit(new Effect.CreateFolder(Col(0), Location.Drives.Instance));
        var msg = WaitForMsg(queue, TimeSpan.FromSeconds(10));

        Assert.IsType<Msg.NoticeRaised>(msg);
    }

    [Fact]
    public void Renaming_a_file_moves_it_and_reports_the_new_name()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "before.txt"), "x");
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

            runtime.Submit(new Effect.RenameEntry(Col(0), new Location.RealDirectory(dir), "before.txt", "after.txt"));
            var done = Assert.IsType<Msg.RenameCompleted>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));

            Assert.Equal("after.txt", done.NewName);
            Assert.False(File.Exists(Path.Combine(dir, "before.txt")));
            Assert.True(File.Exists(Path.Combine(dir, "after.txt")));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Renaming_a_folder_works_the_same_way()
    {
        var dir = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "before"));
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

            runtime.Submit(new Effect.RenameEntry(Col(0), new Location.RealDirectory(dir), "before", "after"));
            Assert.IsType<Msg.RenameCompleted>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));

            Assert.True(Directory.Exists(Path.Combine(dir, "after")));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// A name already taken is a refusal, not an overwrite. File.Move on Windows would happily
    /// replace the other file, which for a rename is silent data loss.
    /// </summary>
    [Fact]
    public void Renaming_onto_a_name_that_exists_refuses_rather_than_overwriting()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "before.txt"), "mine");
            File.WriteAllText(Path.Combine(dir, "taken.txt"), "theirs");
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

            runtime.Submit(new Effect.RenameEntry(Col(0), new Location.RealDirectory(dir), "before.txt", "taken.txt"));
            Assert.IsType<Msg.RenameFailed>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));

            Assert.Equal("mine", File.ReadAllText(Path.Combine(dir, "before.txt")));
            Assert.Equal("theirs", File.ReadAllText(Path.Combine(dir, "taken.txt")));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// Changing only the casing is a real rename, and must not be mistaken for a collision with
    /// itself - Windows compares names case-insensitively.
    /// </summary>
    [Fact]
    public void Changing_only_the_casing_is_allowed()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "x");
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

            runtime.Submit(new Effect.RenameEntry(Col(0), new Location.RealDirectory(dir), "readme.txt", "README.TXT"));
            var done = Assert.IsType<Msg.RenameCompleted>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));

            Assert.Equal("README.TXT", done.NewName);
            Assert.Equal("README.TXT", new DirectoryInfo(dir).GetFiles()[0].Name);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Renaming_something_that_is_not_there_says_so()
    {
        var dir = CreateTempDir();
        try
        {
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

            runtime.Submit(new Effect.RenameEntry(Col(0), new Location.RealDirectory(dir), "ghost.txt", "other.txt"));

            Assert.IsType<Msg.RenameFailed>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    private static List<Msg> WaitFor(ConcurrentQueue<Msg> queue, TimeSpan timeout, Func<List<Msg>, bool> done)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var seen = new List<Msg>(queue);
            if (done(seen))
            {
                return seen;
            }

            Thread.Sleep(10);
        }

        return new List<Msg>(queue);
    }

    private static List<Msg.PreviewLoaded> PreviewsIn(List<Msg> msgs)
    {
        var loaded = new List<Msg.PreviewLoaded>();
        foreach (var msg in msgs)
        {
            if (msg is Msg.PreviewLoaded preview)
            {
                loaded.Add(preview);
            }
        }

        return loaded;
    }

    /// <summary>Polls <paramref name="condition"/> until it holds; false if it never does in time.</summary>
    private static bool PollUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    /// <summary>A file the preview treats as video by its extension; its shell thumbnail is what blocks.</summary>
    private static string VideoFile(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x70, 0x34, 0x32]);
        return path;
    }

    [Fact]
    public void Two_panes_previews_load_independently()
    {
        var dir = TempDirectory.Create("zurari-two-previews");
        var a = Path.Combine(dir, "a.txt");
        var b = Path.Combine(dir, "b.txt");
        File.WriteAllText(a, "alpha");
        File.WriteAllText(b, "bravo");
        var posted = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(posted.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

        // Pane 0 is already at generation 5; pane 1 starts at 1. With one shared "highest
        // generation" the second request would be dropped as stale.
        runtime.Submit(new Effect.LoadPreview(new PaneId(0), 5, a));
        runtime.Submit(new Effect.LoadPreview(new PaneId(1), 1, b));

        var loaded = PreviewsIn(WaitFor(posted, TimeSpan.FromSeconds(5), seen => PreviewsIn(seen).Count >= 2));
        Assert.Contains(loaded, m => m.Pane == new PaneId(0) && m.Text == "alpha");
        Assert.Contains(loaded, m => m.Pane == new PaneId(1) && m.Text == "bravo");
        TempDirectory.Delete(dir);
    }

    [Fact]
    public void Cancelling_one_panes_preview_leaves_the_others_at_the_same_generation_running()
    {
        var dir = TempDirectory.Create("zurari-cancel-one-pane");
        using var release = new ManualResetEventSlim(false);
        try
        {
            var paneA = new PaneId(0);
            var paneB = new PaneId(1);
            var posted = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                posted.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) =>
                {
                    release.Wait(TimeSpan.FromSeconds(30));
                    return null;
                },
                settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

            // Both panes are at generation 3 and both are held in the (blocked) shell extractor.
            runtime.Submit(new Effect.LoadPreview(paneA, 3, VideoFile(dir, "a.mp4")));
            runtime.Submit(new Effect.LoadPreview(paneB, 3, VideoFile(dir, "b.mp4")));
            Assert.True(PollUntil(() => runtime.PreviewSlotCount == 2), "Both previews should be in flight.");

            runtime.Submit(new Effect.CancelPreview(paneA, 3));

            // A's cancel also releases A's slot, which is how we know it has been applied.
            Assert.True(PollUntil(() => runtime.PreviewSlotCount == 1), "Pane A's cancel was never applied.");
            Assert.False(runtime.IsPreviewInFlight(paneA));
            Assert.True(runtime.IsPreviewInFlight(paneB), "Cancelling pane A must not cancel pane B.");
        }
        finally
        {
            release.Set();
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void A_panes_slot_is_released_when_its_preview_finishes()
    {
        var dir = TempDirectory.Create("zurari-slot-finish");
        try
        {
            var file = Path.Combine(dir, "a.txt");
            File.WriteAllText(file, "alpha");
            var posted = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(posted.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

            runtime.Submit(new Effect.LoadPreview(new PaneId(0), 1, file));
            runtime.Submit(new Effect.LoadPreview(new PaneId(1), 1, file));

            var loaded = PreviewsIn(WaitFor(posted, TimeSpan.FromSeconds(10), seen => PreviewsIn(seen).Count >= 2));
            Assert.Equal(2, loaded.Count);
            Assert.True(PollUntil(() => runtime.PreviewSlotCount == 0), "Finished previews should not keep their slots.");
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void A_stale_load_after_the_slot_is_released_does_not_start()
    {
        var dir = TempDirectory.Create("zurari-stale-after-finish");
        try
        {
            var pane = new PaneId(0);
            var file = Path.Combine(dir, "a.txt");
            File.WriteAllText(file, "alpha");
            var posted = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(posted.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

            runtime.Submit(new Effect.LoadPreview(pane, 5, file));
            WaitFor(posted, TimeSpan.FromSeconds(10), seen => PreviewsIn(seen).Count >= 1);
            Assert.True(PollUntil(() => runtime.PreviewSlotCount == 0), "The slot should be released.");

            // Older generation, then a newer one. If the older were accepted its result would be
            // posted; once the newer one has arrived, nothing else for this pane may be there.
            runtime.Submit(new Effect.LoadPreview(pane, 4, file));
            Assert.Equal(0, runtime.PreviewSlotCount);
            runtime.Submit(new Effect.LoadPreview(pane, 6, file));

            var loaded = PreviewsIn(WaitFor(posted, TimeSpan.FromSeconds(10), seen => HasGeneration(seen, 6)));
            Assert.True(HasGeneration(new List<Msg>(posted), 6), "The newer generation should still start.");
            AssertNoGeneration(loaded, 4);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void A_stale_load_after_a_cancel_released_the_slot_does_not_start()
    {
        var dir = TempDirectory.Create("zurari-stale-after-cancel");
        using var release = new ManualResetEventSlim(false);
        try
        {
            var pane = new PaneId(2);
            var posted = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                posted.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) =>
                {
                    release.Wait(TimeSpan.FromSeconds(30));
                    return null;
                },
                settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));
            var text = Path.Combine(dir, "a.txt");
            File.WriteAllText(text, "alpha");

            runtime.Submit(new Effect.LoadPreview(pane, 5, VideoFile(dir, "a.mp4")));
            Assert.True(PollUntil(() => runtime.IsPreviewInFlight(pane)), "The preview should be in flight.");
            runtime.Submit(new Effect.CancelPreview(pane, 5));
            Assert.True(PollUntil(() => runtime.PreviewSlotCount == 0), "The cancel should release the slot.");

            runtime.Submit(new Effect.LoadPreview(pane, 4, text));
            Assert.Equal(0, runtime.PreviewSlotCount);
            runtime.Submit(new Effect.LoadPreview(pane, 6, text));

            var seen = WaitFor(posted, TimeSpan.FromSeconds(10), msgs => HasGeneration(msgs, 6));
            Assert.True(HasGeneration(seen, 6), "The newer generation should still start.");
            AssertNoGeneration(PreviewsIn(seen), 4);
        }
        finally
        {
            release.Set();
            TempDirectory.Delete(dir);
        }
    }

    private static bool HasGeneration(List<Msg> msgs, int generation)
    {
        foreach (var preview in PreviewsIn(msgs))
        {
            if (preview.Generation == generation)
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertNoGeneration(List<Msg.PreviewLoaded> loaded, int generation)
    {
        foreach (var preview in loaded)
        {
            Assert.NotEqual(generation, preview.Generation);
        }
    }

    [Fact]
    public void A_panes_slot_is_released_when_its_preview_is_cancelled()
    {
        var dir = TempDirectory.Create("zurari-slot-cancel");
        using var release = new ManualResetEventSlim(false);
        try
        {
            var pane = new PaneId(4);
            var posted = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                posted.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) =>
                {
                    release.Wait(TimeSpan.FromSeconds(30));
                    return null;
                },
                settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

            runtime.Submit(new Effect.LoadPreview(pane, 1, VideoFile(dir, "a.mp4")));
            Assert.True(PollUntil(() => runtime.IsPreviewInFlight(pane)), "The preview should be in flight.");

            runtime.Submit(new Effect.CancelPreview(pane, 1));

            Assert.True(PollUntil(() => runtime.PreviewSlotCount == 0), "A cancelled pane's slot should be released.");
        }
        finally
        {
            release.Set();
            TempDirectory.Delete(dir);
        }
    }
}
