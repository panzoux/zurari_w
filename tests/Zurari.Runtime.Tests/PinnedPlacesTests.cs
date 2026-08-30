using System.Collections.Concurrent;
using System.Collections.Immutable;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class PinnedPlacesTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zurari-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

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

    private static ImmutableArray<Entry> ReadRoot(WorkerRuntime runtime, ConcurrentQueue<Msg> queue)
    {
        runtime.Submit(new Effect.ReadDirectory(0, Location.Drives.Instance));
        var loaded = (Msg.DirectoryLoaded)WaitFor<Msg.DirectoryLoaded>(queue, TimeSpan.FromSeconds(10));
        return loaded.Entries;
    }

    [Fact]
    public void Pinning_a_folder_puts_it_in_the_network_section_and_persists_it()
    {
        var dir = CreateTempDir();
        try
        {
            var target = Path.Combine(dir, "share");
            Directory.CreateDirectory(target);
            var store = new UserSettingsStore(Path.Combine(dir, "cfg"));

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: store);

            runtime.Submit(new Effect.SetPinned(target, Pin: true));
            WaitFor<Msg.PlacesChanged>(queue, TimeSpan.FromSeconds(10));

            // Persisted, so a later run sees it.
            Assert.Equal([target], store.Load().PinnedPaths);

            var entries = ReadRoot(runtime, queue);
            var pinned = entries.Where(e => e.Group == EntryGroups.Pinned).ToList();
            Assert.Equal(EntryKind.Header, pinned[0].Kind);
            Assert.Equal("ネットワーク", pinned[0].Name);
            Assert.Equal(target, pinned[1].Name);
            Assert.Equal("share", pinned[1].Label);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Unpinning_removes_it_again()
    {
        var dir = CreateTempDir();
        try
        {
            var target = Path.Combine(dir, "share");
            Directory.CreateDirectory(target);
            var store = new UserSettingsStore(Path.Combine(dir, "cfg"));

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: store);

            runtime.Submit(new Effect.SetPinned(target, Pin: true));
            WaitFor<Msg.PlacesChanged>(queue, TimeSpan.FromSeconds(10));
            runtime.Submit(new Effect.SetPinned(target, Pin: false));
            WaitFor<Msg.PlacesChanged>(queue, TimeSpan.FromSeconds(10));

            Assert.Empty(store.Load().PinnedPaths!);
            Assert.DoesNotContain(ReadRoot(runtime, queue), e => e.Group == EntryGroups.Pinned);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Pinning_the_same_place_twice_does_not_duplicate_it()
    {
        var dir = CreateTempDir();
        try
        {
            var target = Path.Combine(dir, "share");
            Directory.CreateDirectory(target);
            var store = new UserSettingsStore(Path.Combine(dir, "cfg"));

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: store);

            runtime.Submit(new Effect.SetPinned(target, Pin: true));
            WaitFor<Msg.PlacesChanged>(queue, TimeSpan.FromSeconds(10));

            // Different casing, same folder: Windows paths are case-insensitive, so this is the
            // same place and must not become a second row.
            runtime.Submit(new Effect.SetPinned(target.ToUpperInvariant(), Pin: true));
            WaitFor<Msg.PlacesChanged>(queue, TimeSpan.FromSeconds(10));

            Assert.Single(store.Load().PinnedPaths!);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_pin_that_cannot_be_reached_is_skipped_but_kept()
    {
        var dir = CreateTempDir();
        try
        {
            var gone = Path.Combine(dir, "offline-share");
            var store = new UserSettingsStore(Path.Combine(dir, "cfg"));
            store.Save(new UserSettings(PinnedPaths: [gone]));

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: store);

            Assert.DoesNotContain(ReadRoot(runtime, queue), e => e.Group == EntryGroups.Pinned);

            // Not forgotten: a laptop away from its network is the normal case, and dropping the pin
            // because the share was unreachable once would be worse than showing nothing today.
            Assert.Equal([gone], store.Load().PinnedPaths);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_pinned_folder_colliding_with_a_favorite_is_qualified_with_its_path()
    {
        var dir = CreateTempDir();
        try
        {
            var favorite = Path.Combine(dir, "a", "shared-name");
            var pinned = Path.Combine(dir, "b", "shared-name");
            Directory.CreateDirectory(favorite);
            Directory.CreateDirectory(pinned);

            var store = new UserSettingsStore(Path.Combine(dir, "cfg"));
            store.Save(new UserSettings(PinnedPaths: [pinned]));

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue,
                settings: store,
                places: () => [new RootPlace(favorite, "shared-name")]);

            var entries = ReadRoot(runtime, queue);

            // Disambiguation spans both sections - a collision is confusing wherever it sits.
            Assert.Equal(
                $"shared-name ({favorite})",
                entries.Single(e => e.Group == EntryGroups.Favorites && e.Kind != EntryKind.Header).Label);
            Assert.Equal(
                $"shared-name ({pinned})",
                entries.Single(e => e.Group == EntryGroups.Pinned && e.Kind != EntryKind.Header).Label);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
