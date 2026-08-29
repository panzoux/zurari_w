using System.Collections.Concurrent;
using System.Collections.Immutable;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class RootPaneTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zurari-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
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

    private static ImmutableArray<Entry> ReadRoot(Func<IReadOnlyList<RootPlace>> places)
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue, places: places);
        runtime.Submit(new Effect.ReadDirectory(0, Location.Drives.Instance));
        var loaded = Assert.IsType<Msg.DirectoryLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
        return loaded.Entries;
    }

    [Fact]
    public void Favorites_appear_above_the_drives_under_their_own_header()
    {
        var dir = CreateTempDir();
        try
        {
            var entries = ReadRoot(() => [new RootPlace(dir, "テスト")]);

            Assert.Equal(EntryKind.Header, entries[0].Kind);
            Assert.Equal("お気に入り", entries[0].Name);
            Assert.Equal("テスト", entries[1].Label);
            Assert.Equal("favorites", entries[1].Group);

            // The path is the identity; the label is only what is shown.
            Assert.Equal(dir, entries[1].Name);
            Assert.Equal(new Location.RealDirectory(dir), entries[1].Target);

            // Drives still follow, under their own header.
            var drivesHeader = entries.ToList().FindIndex(e => e is { Kind: EntryKind.Header, Name: "ドライブ" });
            Assert.True(drivesHeader > 1, "the drives section should come after the favorites");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_favorite_that_does_not_exist_is_left_out()
    {
        var missing = Path.Combine(Path.GetTempPath(), "zurari-no-such-" + Guid.NewGuid().ToString("N"));

        var entries = ReadRoot(() => [new RootPlace(missing, "無い")]);

        // No favorite survived, so the section contributes no header either.
        Assert.DoesNotContain(entries, e => e.Group == "favorites");
    }

    [Fact]
    public void Two_favorites_that_read_alike_are_qualified_with_their_paths()
    {
        var root = CreateTempDir();
        try
        {
            var a = Path.Combine(root, "a", "fol1");
            var b = Path.Combine(root, "b", "fol1");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);

            var entries = ReadRoot(() => [new RootPlace(a, "fol1"), new RootPlace(b, "fol1")]);

            var labels = entries
                .Where(e => e.Group == "favorites" && e.Kind != EntryKind.Header)
                .Select(e => e.Label)
                .ToList();
            Assert.Equal([$"fol1 ({a})", $"fol1 ({b})"], labels);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_favorite_with_a_name_of_its_own_is_left_unqualified()
    {
        var root = CreateTempDir();
        try
        {
            var a = Path.Combine(root, "alone");
            Directory.CreateDirectory(a);

            var entries = ReadRoot(() => [new RootPlace(a, "ひとつだけ")]);

            // Only ambiguous labels get an address after them.
            Assert.Equal(
                "ひとつだけ",
                entries.Single(e => e.Group == "favorites" && e.Kind != EntryKind.Header).Label);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void With_no_places_supplied_the_pane_is_just_the_drives()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);
        runtime.Submit(new Effect.ReadDirectory(0, Location.Drives.Instance));

        var loaded = Assert.IsType<Msg.DirectoryLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));

        Assert.All(loaded.Entries, e => Assert.Equal("drives", e.Group));
    }
}
