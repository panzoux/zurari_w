using System.Collections.Concurrent;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

/// <summary>
/// The preview for a row that is a place rather than a file: how full a volume is, or how much is
/// in the recycle bin.
/// </summary>
public class CapacityPreviewTests
{
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
    public void The_system_drive_reports_a_total_and_a_plausible_used_share()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);

        runtime.Submit(new Effect.LoadPreview(1, SystemDrive(), PreviewTarget.Volume));
        var loaded = WaitFor<Msg.PreviewCapacityLoaded>(queue, TimeSpan.FromSeconds(10));

        var capacity = loaded.Capacity;
        Assert.Equal(1, loaded.Generation);
        Assert.NotNull(capacity.TotalBytes);
        Assert.True(capacity.TotalBytes > 0, "a mounted volume has a size");
        Assert.InRange(capacity.UsedBytes!.Value, 0, capacity.TotalBytes!.Value);
        Assert.InRange(capacity.UsedFraction!.Value, 0, 1);
        Assert.Equal(capacity.TotalBytes - capacity.UsedBytes, capacity.FreeBytes);
        Assert.NotEmpty(capacity.TypeName);
    }

    /// <summary>
    /// The bin's totals come from the shell, which this layer may not touch - so they arrive through
    /// the same delegate the composition root already supplies for the row's label.
    /// </summary>
    [Fact]
    public void The_recycle_bin_reports_what_the_composition_root_supplied()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(
            queue.Enqueue, trash: () => new TrashPlace("ゴミ箱 (413)", ItemCount: 413, TotalBytes: 12345));

        runtime.Submit(new Effect.LoadPreview(7, "::whatever", PreviewTarget.RecycleBin));
        var loaded = WaitFor<Msg.PreviewCapacityLoaded>(queue, TimeSpan.FromSeconds(10));

        Assert.Equal(7, loaded.Generation);
        Assert.Equal(413, loaded.Capacity.ItemCount);
        Assert.Equal(12345, loaded.Capacity.UsedBytes);

        // A bin has a size but no size limit, so there is nothing for a bar to be a fraction of.
        Assert.Null(loaded.Capacity.TotalBytes);
        Assert.Null(loaded.Capacity.UsedFraction);
    }

    /// <summary>
    /// An empty optical drive is not an error - it is an empty drive, and saying what kind of drive
    /// it is remains useful. Reporting it as a failed preview said only that something went wrong.
    /// </summary>
    [Fact]
    public void A_drive_with_nothing_in_it_still_says_what_kind_of_drive_it_is()
    {
        var notReady = DriveInfo.GetDrives().FirstOrDefault(d => !d.IsReady);
        if (notReady is null)
        {
            return; // every drive on this machine is ready; nothing to describe.
        }

        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);

        runtime.Submit(new Effect.LoadPreview(5, notReady.Name, PreviewTarget.Volume));
        var loaded = WaitFor<Msg.PreviewCapacityLoaded>(queue, TimeSpan.FromSeconds(10));

        Assert.NotEmpty(loaded.Capacity.TypeName);
        Assert.Equal("準備できていません", loaded.Capacity.Status);
        Assert.Null(loaded.Capacity.TotalBytes);
        Assert.Null(loaded.Capacity.UsedBytes);
        Assert.Null(loaded.Capacity.UsedFraction);
    }

    /// <summary>
    /// A letter nothing is mounted on behaves the same as an empty optical drive: it says it is not
    /// ready rather than throwing, or hanging, or reporting a zero-byte volume.
    /// </summary>
    [Fact]
    public void A_letter_nothing_is_mounted_on_answers_rather_than_throwing()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);

        runtime.Submit(new Effect.LoadPreview(3, MissingDriveLetter(), PreviewTarget.Volume));
        var loaded = WaitFor<Msg.PreviewCapacityLoaded>(queue, TimeSpan.FromSeconds(10));

        Assert.Equal(3, loaded.Generation);
        Assert.Equal("準備できていません", loaded.Capacity.Status);
        Assert.Null(loaded.Capacity.TotalBytes);
    }

    /// <summary>A volume request must not be mistaken for "read the directory at this path".</summary>
    [Fact]
    public void A_drive_root_is_not_read_as_a_file()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);

        runtime.Submit(new Effect.LoadPreview(1, SystemDrive(), PreviewTarget.Volume));
        WaitFor<Msg.PreviewCapacityLoaded>(queue, TimeSpan.FromSeconds(10));

        Assert.DoesNotContain(queue, m => m is Msg.PreviewLoaded);
    }

    private static string SystemDrive() => Path.GetPathRoot(Environment.SystemDirectory)!;

    /// <summary>A drive letter nothing is mounted on, or Z: if the machine somehow uses them all.</summary>
    private static string MissingDriveLetter()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        for (var letter = 'Z'; letter >= 'D'; letter--)
        {
            if (!used.Contains(letter))
            {
                return letter + @":\";
            }
        }

        return @"Z:\";
    }
}
