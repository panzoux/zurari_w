using System.Collections.Concurrent;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

/// <summary>
/// The persistence half of a collapsed section: <c>Transition</c> decides what is collapsed and
/// emits the set, and it comes back on the next read of the drive pane.
/// </summary>
public class CollapsedSectionsTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zurari-collapse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static T WaitFor<T>(ConcurrentQueue<Msg> queue, TimeSpan timeout)
        where T : Msg
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (queue.TryDequeue(out var msg) && msg is T match)
            {
                return match;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException($"No {typeof(T).Name} was posted within the timeout.");
    }

    [Fact]
    public void A_collapsed_section_is_written_to_settings()
    {
        var dir = CreateTempDir();
        try
        {
            var store = new UserSettingsStore(Path.Combine(dir, "cfg"));
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: store);

            runtime.Submit(new Effect.SetCollapsedGroups([EntryGroups.Drives, EntryGroups.Trash]));

            // The effect reports nothing back, so wait for the file rather than for a message.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (store.Load().CollapsedGroups is null && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }

            Assert.Equal([EntryGroups.Drives, EntryGroups.Trash], store.Load().CollapsedGroups);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Reading_the_drive_pane_reports_what_was_collapsed_last_time()
    {
        var dir = CreateTempDir();
        try
        {
            var settingsDir = Path.Combine(dir, "cfg");
            new UserSettingsStore(settingsDir).Save(new UserSettings(CollapsedGroups: [EntryGroups.Drives]));

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(settingsDir));

            runtime.Submit(new Effect.ReadDirectory(0, Location.Drives.Instance));

            // The listing arrives first; what is hidden in it follows, because a section cannot be
            // collapsed before its rows exist.
            WaitFor<Msg.DirectoryLoaded>(queue, TimeSpan.FromSeconds(10));
            var restored = WaitFor<Msg.CollapsedGroupsRestored>(queue, TimeSpan.FromSeconds(10));

            Assert.Equal(0, restored.ColumnIndex);
            Assert.Equal([EntryGroups.Drives], restored.Groups);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The file seeds the session and nothing more. After the first read the state carries the
    /// collapse - a refresh preserves it - so asking the file again could only lose a change the user
    /// just made and has not finished being written.
    /// </summary>
    [Fact]
    public void The_stored_set_is_applied_once_and_a_refresh_does_not_ask_again()
    {
        var dir = CreateTempDir();
        try
        {
            var settingsDir = Path.Combine(dir, "cfg");
            new UserSettingsStore(settingsDir).Save(new UserSettings(CollapsedGroups: [EntryGroups.Drives]));

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue, settings: new UserSettingsStore(settingsDir));

            runtime.Submit(new Effect.ReadDirectory(0, Location.Drives.Instance));
            WaitFor<Msg.CollapsedGroupsRestored>(queue, TimeSpan.FromSeconds(10));

            runtime.Submit(new Effect.ReadDirectory(0, Location.Drives.Instance));
            WaitFor<Msg.DirectoryLoaded>(queue, TimeSpan.FromSeconds(10));

            Thread.Sleep(100);
            Assert.DoesNotContain(queue, m => m is Msg.CollapsedGroupsRestored);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void An_ordinary_directory_read_says_nothing_about_sections()
    {
        var dir = CreateTempDir();
        try
        {
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue, settings: new UserSettingsStore(Path.Combine(dir, "cfg")));

            runtime.Submit(new Effect.ReadDirectory(0, new Location.RealDirectory(dir)));
            WaitFor<Msg.DirectoryLoaded>(queue, TimeSpan.FromSeconds(10));

            Thread.Sleep(100);
            Assert.DoesNotContain(queue, m => m is Msg.CollapsedGroupsRestored);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
