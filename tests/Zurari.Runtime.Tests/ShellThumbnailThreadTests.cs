using System.Collections.Concurrent;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

/// <summary>
/// 6c.7: the shell thumbnail call is synchronous and cannot be cancelled, so it gets one dedicated
/// STA thread, one extraction in flight, at most one waiting, and a time budget past which the
/// preview stops waiting for it.
/// </summary>
public class ShellThumbnailThreadTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

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

    /// <summary>
    /// A document, because documents go straight to the shell. A video would reach it only after
    /// ffmpeg has had its turn (6c.7 put ffmpeg first), and that delay is exactly what turns a
    /// timing test into a flaky one.
    /// </summary>
    private static string DocumentFile(string dir, string name = "paper.pdf")
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "%PDF-1.4 1 0 obj << /Type /Catalog >> endobj %%EOF");
        return path;
    }

    /// <summary>
    /// Thumbnail providers are commonly STA-only. Today the call runs on an MTA thread-pool thread
    /// and the shell marshals it to an apartment of its choosing; this pins it to one we own.
    /// </summary>
    [Fact]
    public void The_shell_is_asked_on_a_dedicated_sta_thread()
    {
        var dir = TempDirectory.Create("zurari-shellthread");
        try
        {
            var document = DocumentFile(dir);
            ApartmentState? apartment = null;
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) =>
                {
                    apartment = Thread.CurrentThread.GetApartmentState();
                    return OnePixelPng;
                });

            runtime.Submit(new Effect.LoadPreview(1, document));
            WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));

            Assert.Equal(ApartmentState.STA, apartment);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// The bound that 6c.7 exists for: two previews landing while an extraction is in flight must
    /// not produce two extractions, because the call cannot be cancelled and each costs ~500 ms.
    /// </summary>
    [Fact]
    public void Extractions_never_overlap()
    {
        var dir = TempDirectory.Create("zurari-shellthread");
        try
        {
            var first = DocumentFile(dir, "a.pdf");
            var second = DocumentFile(dir, "b.pdf");
            var running = 0;
            var highest = 0;
            var gate = new object();
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) =>
                {
                    var now = Interlocked.Increment(ref running);
                    lock (gate)
                    {
                        highest = Math.Max(highest, now);
                    }

                    Thread.Sleep(300);
                    Interlocked.Decrement(ref running);
                    return OnePixelPng;
                });

            runtime.Submit(new Effect.LoadPreview(1, first));
            runtime.Submit(new Effect.LoadPreview(2, second));
            WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(20));
            Thread.Sleep(500);

            lock (gate)
            {
                Assert.Equal(1, highest);
            }
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Every_request_is_served_by_the_same_thread()
    {
        var dir = TempDirectory.Create("zurari-shellthread");
        try
        {
            var first = DocumentFile(dir, "a.pdf");
            var second = DocumentFile(dir, "b.pdf");
            var threads = new ConcurrentQueue<int>();
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) =>
                {
                    threads.Enqueue(Environment.CurrentManagedThreadId);
                    return OnePixelPng;
                });

            runtime.Submit(new Effect.LoadPreview(1, first));
            WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));
            runtime.Submit(new Effect.LoadPreview(2, second));
            WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));

            Assert.Equal(2, threads.Count);
            Assert.Single(threads.Distinct());
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// Latest-wins: holding an arrow down costs one extraction in flight and one waiting, not one
    /// per row. The file the cursor passed over is dropped without ever being asked for.
    /// </summary>
    [Fact]
    public void A_newer_request_replaces_the_one_still_waiting()
    {
        var dir = TempDirectory.Create("zurari-shellthread");
        try
        {
            var held = DocumentFile(dir, "held.pdf");
            var passed = DocumentFile(dir, "passed-over.pdf");
            var landed = DocumentFile(dir, "landed-on.pdf");
            var asked = new ConcurrentQueue<string>();
            using var release = new ManualResetEventSlim(false);
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (path, _) =>
                {
                    asked.Enqueue(Path.GetFileName(path));
                    if (Path.GetFileName(path) == "held.pdf")
                    {
                        release.Wait(TimeSpan.FromSeconds(10));
                    }

                    return OnePixelPng;
                });

            runtime.Submit(new Effect.LoadPreview(1, held));
            Thread.Sleep(400);
            runtime.Submit(new Effect.LoadPreview(2, passed));
            Thread.Sleep(400);
            runtime.Submit(new Effect.LoadPreview(3, landed));
            Thread.Sleep(400);
            release.Set();
            Thread.Sleep(700);

            Assert.Equal(["held.pdf", "landed-on.pdf"], asked);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// The budget: an extraction slower than it must not hold the pane. The preview shows what it
    /// would have shown anyway, and the picture replaces it if it turns up while that generation is
    /// still on screen - Msg.PreviewLoaded already carries the generation, so a late one is applied
    /// or ignored with no new machinery.
    /// </summary>
    /// <remarks>
    /// Deliberately slow (it waits out the real budget rather than a test-only knob), because the
    /// constant is the thing under test: measured shell extractions ran 282-722 ms on real videos,
    /// so a budget that fired on those would flip every video preview from hex dump to picture.
    /// </remarks>
    [Fact]
    public void A_slow_extraction_does_not_hold_the_preview_past_its_budget()
    {
        var dir = TempDirectory.Create("zurari-shellthread");
        try
        {
            var pdf = Path.Combine(dir, "paper.pdf");
            File.WriteAllText(pdf, "%PDF-1.4 1 0 obj << /Type /Catalog >> endobj trailer << /Root 1 0 R >> %%EOF");
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) =>
                {
                    Thread.Sleep(TimeSpan.FromSeconds(2.2));
                    return OnePixelPng;
                });

            runtime.Submit(new Effect.LoadPreview(1, pdf));

            var first = WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));
            Assert.Equal(PreviewKind.Text, first.Kind);

            var late = WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));
            Assert.Equal(PreviewKind.Image, late.Kind);
            Assert.Equal(first.Generation, late.Generation);
            Assert.Equal("PDF Document", late.Text);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// A handler that is both slow and empty is the worst case this item exists for: the budget
    /// stops it holding the pane, and the verdict is still remembered so the next landing on that
    /// file costs nothing at all.
    /// </summary>
    [Fact]
    public void A_slow_extraction_that_finds_nothing_is_still_remembered()
    {
        var dir = TempDirectory.Create("zurari-shellthread");
        try
        {
            var pdf = Path.Combine(dir, "paper.pdf");
            File.WriteAllText(pdf, "%PDF-1.4 1 0 obj << /Type /Catalog >> endobj %%EOF");
            var asked = 0;
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) =>
                {
                    Interlocked.Increment(ref asked);
                    Thread.Sleep(TimeSpan.FromSeconds(2));
                    return null;
                });

            runtime.Submit(new Effect.LoadPreview(1, pdf));
            WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));

            // Let the extraction outrun its budget and record what it found.
            Thread.Sleep(TimeSpan.FromSeconds(1.5));

            runtime.Submit(new Effect.LoadPreview(2, pdf));
            WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));

            Assert.Equal(1, asked);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }
}
