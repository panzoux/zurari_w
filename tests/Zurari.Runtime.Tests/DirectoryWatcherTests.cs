using System.Collections.Concurrent;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class DirectoryWatcherTests
{
    private static string CreateTempDir() => TempDirectory.Create("zurari-dirwatcher-tests");

    private static bool WaitForAny(ConcurrentQueue<Msg.ExternalDirectoryChanged> queue, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!queue.IsEmpty)
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    [Fact]
    public void External_file_creation_posts_ExternalDirectoryChanged_within_a_few_seconds()
    {
        var dir = CreateTempDir();
        try
        {
            var queue = new ConcurrentQueue<Msg.ExternalDirectoryChanged>();
            using var watcher = new DirectoryWatcher(post: m =>
            {
                if (m is Msg.ExternalDirectoryChanged changed)
                {
                    queue.Enqueue(changed);
                }
            });
            watcher.SetWatchedPaths([dir]);

            File.WriteAllText(Path.Combine(dir, "external.txt"), "hello");

            Assert.True(WaitForAny(queue, TimeSpan.FromSeconds(3)), "Expected an ExternalDirectoryChanged Msg.");
            Assert.Equal(dir, queue.First().Path);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Two_rapid_writes_debounce_into_at_least_one_message_after_the_burst_goes_quiet()
    {
        var dir = CreateTempDir();
        try
        {
            var queue = new ConcurrentQueue<Msg.ExternalDirectoryChanged>();
            using var watcher = new DirectoryWatcher(post: m =>
            {
                if (m is Msg.ExternalDirectoryChanged changed)
                {
                    queue.Enqueue(changed);
                }
            });
            watcher.SetWatchedPaths([dir]);

            File.WriteAllText(Path.Combine(dir, "a.txt"), "1");
            Thread.Sleep(50);
            File.WriteAllText(Path.Combine(dir, "b.txt"), "2");

            // Nothing should have arrived yet - both writes fall inside the ~500ms debounce window.
            Thread.Sleep(200);
            Assert.True(queue.IsEmpty, "Expected the debounce window to swallow both rapid writes so far.");

            Assert.True(WaitForAny(queue, TimeSpan.FromSeconds(3)), "Expected at least one ExternalDirectoryChanged Msg after the burst.");
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void SetWatchedPaths_reconcile_removes_watchers_no_longer_present()
    {
        var dirA = CreateTempDir();
        var dirB = CreateTempDir();
        try
        {
            var queue = new ConcurrentQueue<Msg.ExternalDirectoryChanged>();
            using var watcher = new DirectoryWatcher(post: m =>
            {
                if (m is Msg.ExternalDirectoryChanged changed)
                {
                    queue.Enqueue(changed);
                }
            });
            watcher.SetWatchedPaths([dirA]);
            watcher.SetWatchedPaths([dirB]); // dirA should no longer be watched

            File.WriteAllText(Path.Combine(dirA, "unwatched.txt"), "x");

            Assert.False(WaitForAny(queue, TimeSpan.FromSeconds(1.5)), "Expected no Msg for a path removed from the watched set.");
        }
        finally
        {
            TempDirectory.Delete(dirA);
            TempDirectory.Delete(dirB);
        }
    }

    [Fact]
    public void Dispose_stops_all_watchers()
    {
        var dir = CreateTempDir();
        try
        {
            var queue = new ConcurrentQueue<Msg.ExternalDirectoryChanged>();
            var watcher = new DirectoryWatcher(post: m =>
            {
                if (m is Msg.ExternalDirectoryChanged changed)
                {
                    queue.Enqueue(changed);
                }
            });
            watcher.SetWatchedPaths([dir]);
            watcher.Dispose();

            File.WriteAllText(Path.Combine(dir, "after-dispose.txt"), "x");

            Assert.False(WaitForAny(queue, TimeSpan.FromSeconds(1.5)), "Expected no Msg after Dispose.");
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void SetWatchedPaths_ignores_empty_and_duplicate_paths_without_throwing()
    {
        var dir = CreateTempDir();
        try
        {
            using var watcher = new DirectoryWatcher(post: _ => { });

            var ex = Record.Exception(() => watcher.SetWatchedPaths([dir, dir, string.Empty]));

            Assert.Null(ex);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }
}
