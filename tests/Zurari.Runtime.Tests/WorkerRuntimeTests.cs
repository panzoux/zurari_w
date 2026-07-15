using System.Collections.Concurrent;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class WorkerRuntimeTests
{
    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "zurari-runtime-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

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
            runtime.Submit(new Effect.ReadDirectory(0, dir));

            var msg = WaitForMsg(queue, TimeSpan.FromSeconds(10));
            var loaded = Assert.IsType<Msg.DirectoryLoaded>(msg);
            Assert.Equal(0, loaded.ColumnIndex);
            Assert.Equal(dir, loaded.Path);
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
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadDirectory_of_nonexistent_path_reports_failure_with_column_index()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zurari-does-not-exist-" + Guid.NewGuid());
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);
        runtime.Submit(new Effect.ReadDirectory(3, dir));

        var msg = WaitForMsg(queue, TimeSpan.FromSeconds(10));
        var failed = Assert.IsType<Msg.DirectoryLoadFailed>(msg);
        Assert.Equal(3, failed.ColumnIndex);
        Assert.Equal(dir, failed.Path);
        Assert.False(string.IsNullOrEmpty(failed.Error));
    }

    [Fact]
    public void ReadDirectory_of_virtual_root_reports_drives()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);
        runtime.Submit(new Effect.ReadDirectory(0, ""));

        var msg = WaitForMsg(queue, TimeSpan.FromSeconds(10));
        var loaded = Assert.IsType<Msg.DirectoryLoaded>(msg);
        Assert.Equal(0, loaded.ColumnIndex);
        Assert.Equal("", loaded.Path);
        Assert.NotEmpty(loaded.Entries);
        Assert.All(loaded.Entries, e => Assert.Equal(EntryKind.Drive, e.Kind));
    }

    [Fact]
    public void Dispose_returns_promptly_even_with_queued_work()
    {
        var queue = new ConcurrentQueue<Msg>();
        var runtime = new WorkerRuntime(queue.Enqueue, workerCount: 1);
        for (var i = 0; i < 200; i++)
        {
            runtime.Submit(new Effect.ReadDirectory(i, ""));
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
                initial: new AppState { Columns = [new Column(Path: dir, Entries: [])], FocusedColumn = 0 },
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
            Directory.Delete(dir, recursive: true);
        }
    }
}
