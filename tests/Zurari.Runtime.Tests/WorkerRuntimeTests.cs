using System.Collections.Concurrent;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class WorkerRuntimeTests
{
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
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.ReadDirectory(0, new Location.RealDirectory(dir)));

            var msg = WaitForMsg(queue, TimeSpan.FromSeconds(10));
            var loaded = Assert.IsType<Msg.DirectoryLoaded>(msg);
            Assert.Equal(0, loaded.ColumnIndex);
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
        using var runtime = new WorkerRuntime(queue.Enqueue);
        runtime.Submit(new Effect.ReadDirectory(3, new Location.RealDirectory(dir)));

        var msg = WaitForMsg(queue, TimeSpan.FromSeconds(10));
        var failed = Assert.IsType<Msg.DirectoryLoadFailed>(msg);
        Assert.Equal(3, failed.ColumnIndex);
        Assert.Equal(new Location.RealDirectory(dir), failed.Location);
        Assert.False(string.IsNullOrEmpty(failed.Error));
    }

    [Fact]
    public void ReadDirectory_of_virtual_root_reports_drives()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);
        runtime.Submit(new Effect.ReadDirectory(0, Location.Drives.Instance));

        var msg = WaitForMsg(queue, TimeSpan.FromSeconds(10));
        var loaded = Assert.IsType<Msg.DirectoryLoaded>(msg);
        Assert.Equal(0, loaded.ColumnIndex);
        Assert.Equal(Location.Drives.Instance, loaded.Location);
        Assert.NotEmpty(loaded.Entries);

        // The pane is sectioned now, so it opens with a header and every drive sits under it. The
        // header is a real entry rather than something the view adds, because the cursor lands on it.
        Assert.Equal(EntryKind.Header, loaded.Entries[0].Kind);
        Assert.Equal("ドライブ", loaded.Entries[0].Name);
        Assert.All(loaded.Entries.Skip(1), e => Assert.Equal(EntryKind.Drive, e.Kind));
        Assert.All(loaded.Entries, e => Assert.Equal("drives", e.Group));

        // Every drive reads as something, and the name stays the path so rows stay distinguishable.
        Assert.All(loaded.Entries.Skip(1), e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Label));
            Assert.EndsWith(@"\", e.Name, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Dispose_returns_promptly_even_with_queued_work()
    {
        var queue = new ConcurrentQueue<Msg>();
        var runtime = new WorkerRuntime(queue.Enqueue, workerCount: 1);
        for (var i = 0; i < 200; i++)
        {
            runtime.Submit(new Effect.ReadDirectory(i, Location.Drives.Instance));
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
            using var runtime = new WorkerRuntime(queue.Enqueue);
            var loop = new MessageLoop(
                initial: new AppState { Columns = [new Column(new Location.RealDirectory(dir), Entries: [])], FocusedColumn = 0 },
                runEffect: runtime.Submit);

            loop.Dispatch(new Msg.Refresh());

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline && loop.State.Columns[0].Load == LoadState.Loading)
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

            Assert.Equal(LoadState.Loaded, loop.State.Columns[0].Load);
            Assert.Equal(2, loop.State.Columns[0].Entries.Length);
            Assert.Equal("sub", loop.State.Columns[0].Entries[0].Name);
            Assert.Equal("file.txt", loop.State.Columns[0].Entries[1].Name);
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
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.ReadDirectory(0, new Location.RealDirectory(dir)));
            var loaded = Assert.IsType<Msg.DirectoryLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));

            Assert.Equal(2, loaded.Entries.Length);
            Assert.False(loaded.Entries.Single(e => e.Name == "plain.txt").IsHidden);
            Assert.True(loaded.Entries.Single(e => e.Name == "hidden.txt").IsHidden);
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
            using var runtime = new WorkerRuntime(queue.Enqueue);

            runtime.Submit(new Effect.CreateFolder(0, new Location.RealDirectory(dir)));
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
            using var runtime = new WorkerRuntime(queue.Enqueue);

            runtime.Submit(new Effect.CreateFolder(0, new Location.RealDirectory(dir)));
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
        using var runtime = new WorkerRuntime(queue.Enqueue);

        runtime.Submit(new Effect.CreateFolder(0, Location.Drives.Instance));
        var msg = WaitForMsg(queue, TimeSpan.FromSeconds(10));

        Assert.IsType<Msg.NoticeRaised>(msg);
    }
}
