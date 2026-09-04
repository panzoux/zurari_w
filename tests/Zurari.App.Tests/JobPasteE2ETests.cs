using System.Collections.Concurrent;
using System.IO;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.App.Tests;

/// <summary>
/// End-to-end coverage of <see cref="Msg.PasteRequested"/> wired through a real
/// <see cref="JobEngine"/> (composition-root style: routes <see cref="Effect.RunFileJob"/> /
/// <see cref="Effect.CancelJob"/> to the job engine and <see cref="Effect.ReadDirectory"/> to
/// <see cref="WorkerRuntime"/>, mirroring <c>MainWindow.RunEffect</c>) over real temp
/// directories, without ever creating a WPF window or touching the OS clipboard.
/// </summary>
public class JobPasteE2ETests
{
    private static string CreateTempDir(string suffix) => TempDirectory.Create("zurari-job-tests-" + suffix);

    private static void DrainUntil(MessageLoop loop, ConcurrentQueue<Msg> queue, Func<bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !done())
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

        Assert.True(done(), "Timed out waiting for the expected state.");
    }

    private static (MessageLoop Loop, ConcurrentQueue<Msg> Queue, WorkerRuntime Runtime, JobEngine JobEngine) BuildLoop(
        AppState initial)
    {
        var queue = new ConcurrentQueue<Msg>();
        var runtime = new WorkerRuntime(queue.Enqueue);
        var jobEngine = new JobEngine(queue.Enqueue);

        void RunEffect(Effect effect)
        {
            switch (effect)
            {
                case Effect.ReadDirectory:
                    runtime.Submit(effect);
                    break;
                case Effect.RunFileJob:
                case Effect.CancelJob:
                    jobEngine.Submit(effect);
                    break;
            }
        }

        var loop = new MessageLoop(initial, RunEffect);
        return (loop, queue, runtime, jobEngine);
    }

    [Fact]
    public void PasteRequested_copy_completes_job_and_reloads_destination_column()
    {
        var sourceDir = CreateTempDir("copy-src");
        var destDir = CreateTempDir("copy-dest");
        try
        {
            var sourceFile = Path.Combine(sourceDir, "hello.txt");
            File.WriteAllText(sourceFile, "hello");

            var initial = new AppState
            {
                Columns = [new Column(new Location.RealDirectory(destDir), Entries: [])],
                FocusedColumn = 0,
            };
            var (loop, queue, runtime, jobEngine) = BuildLoop(initial);
            try
            {
                loop.Dispatch(new Msg.PasteRequested(0, [sourceFile], IsMove: false));

                Assert.Single(loop.State.Jobs);

                // A clean (no-skip) Completed job is removed from AppState.Jobs immediately (see
                // Transition.JobFinished) - the strip auto-clears rather than lingering until
                // Msg.JobDismissed - so the completion signal to wait on here is the job list
                // going back to empty, not a Completed status that would never be observed.
                DrainUntil(loop, queue, () => loop.State.Jobs.Length == 0, TimeSpan.FromSeconds(10));

                var destFile = Path.Combine(destDir, "hello.txt");
                DrainUntil(loop, queue, () => loop.State.Columns[0].Load == LoadState.Loaded, TimeSpan.FromSeconds(10));

                Assert.True(File.Exists(destFile));
                Assert.True(File.Exists(sourceFile), "Copy must leave the source file in place.");
                Assert.Contains(loop.State.Columns[0].Entries, e => e.Name == "hello.txt");
            }
            finally
            {
                runtime.Dispose();
                jobEngine.Dispose();
            }
        }
        finally
        {
            TempDirectory.Delete(sourceDir);
            TempDirectory.Delete(destDir);
        }
    }

    [Fact]
    public void PasteRequested_move_completes_job_removes_source_and_reloads_both_columns()
    {
        var sourceDir = CreateTempDir("move-src");
        var destDir = CreateTempDir("move-dest");
        try
        {
            var sourceFile = Path.Combine(sourceDir, "movable.txt");
            File.WriteAllText(sourceFile, "move me");

            var initial = new AppState
            {
                Columns =
                [
                    new Column(new Location.RealDirectory(sourceDir), Entries: []),
                    new Column(new Location.RealDirectory(destDir), Entries: []),
                ],
                FocusedColumn = 1,
            };
            var (loop, queue, runtime, jobEngine) = BuildLoop(initial);
            try
            {
                loop.Dispatch(new Msg.PasteRequested(1, [sourceFile], IsMove: true));

                Assert.Single(loop.State.Jobs);

                // See the copy test's comment above: a clean Completed job is removed from
                // AppState.Jobs immediately, so wait on the job list emptying out.
                DrainUntil(loop, queue, () => loop.State.Jobs.Length == 0, TimeSpan.FromSeconds(10));

                DrainUntil(
                    loop,
                    queue,
                    () => loop.State.Columns[0].Load == LoadState.Loaded
                        && loop.State.Columns[1].Load == LoadState.Loaded,
                    TimeSpan.FromSeconds(10));

                var destFile = Path.Combine(destDir, "movable.txt");
                Assert.True(File.Exists(destFile));
                Assert.False(File.Exists(sourceFile), "Move must remove the source file.");
                Assert.DoesNotContain(loop.State.Columns[0].Entries, e => e.Name == "movable.txt");
                Assert.Contains(loop.State.Columns[1].Entries, e => e.Name == "movable.txt");
            }
            finally
            {
                runtime.Dispose();
                jobEngine.Dispose();
            }
        }
        finally
        {
            TempDirectory.Delete(sourceDir);
            TempDirectory.Delete(destDir);
        }
    }
}
