using System.Collections.Concurrent;
using System.Collections.Immutable;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class TrashSectionTests
{
    private static Msg WaitFor<T>(ConcurrentQueue<Msg> queue, TimeSpan timeout)
        where T : Msg
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (queue.TryDequeue(out var msg) && msg is T)
            {
                return msg;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException($"No {typeof(T).Name} was posted within the timeout.");
    }

    private static ImmutableArray<Entry> ReadRoot(Func<TrashPlace?>? trash)
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue, trash: trash);
        runtime.Submit(new Effect.ReadDirectory(0, Location.Drives.Instance));
        return ((Msg.DirectoryLoaded)WaitFor<Msg.DirectoryLoaded>(queue, TimeSpan.FromSeconds(10))).Entries;
    }

    [Fact]
    public void Trash_is_its_own_section_at_the_end()
    {
        var entries = ReadRoot(() => new TrashPlace("ゴミ箱 (3)"));

        var trash = entries.Where(e => e.Group == EntryGroups.Trash).ToList();
        Assert.Equal(2, trash.Count);
        Assert.Equal(EntryKind.Header, trash[0].Kind);
        Assert.Equal("ゴミ箱", trash[0].Name);
        Assert.Equal("ゴミ箱 (3)", trash[1].Label);

        // A section of its own, after the drives - not folded in with the favorites.
        Assert.Equal(EntryGroups.Trash, entries[^1].Group);
    }

    [Fact]
    public void The_trash_row_points_at_the_recycle_bin_and_cannot_be_removed()
    {
        var row = ReadRoot(() => new TrashPlace("ゴミ箱"))
            .Single(e => e.Group == EntryGroups.Trash && e.Kind != EntryKind.Header);

        Assert.Equal(Location.RecycleBin.Instance, row.Target);

        // Always there, so Delete on it must do nothing - unlike a place the user added.
        Assert.False(row.IsRemovable);
    }

    [Fact]
    public void No_trash_row_when_the_composition_root_supplies_none()
    {
        Assert.DoesNotContain(ReadRoot(trash: null), e => e.Group == EntryGroups.Trash);
    }

    [Fact]
    public void A_read_of_the_recycle_bin_is_not_the_runtimes_to_serve()
    {
        // It has no filesystem path, so the Runtime has nothing to enumerate; the read is routed to
        // Shell instead. Submitting it here should report a failure rather than pretend.
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);
        runtime.Submit(new Effect.ReadDirectory(0, Location.RecycleBin.Instance));

        var failed = Assert.IsType<Msg.DirectoryLoadFailed>(WaitFor<Msg.DirectoryLoadFailed>(queue, TimeSpan.FromSeconds(10)));
        Assert.Equal(Location.RecycleBin.Instance, failed.Location);
    }
}
