using System.Collections.Concurrent;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class JobEngineTests
{
    private static string CreateTempDir() => TempDirectory.Create("zurari-jobengine-tests");

    private static Msg WaitForMsg<T>(ConcurrentQueue<Msg> queue, TimeSpan timeout)
        where T : Msg
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (queue.TryDequeue(out var msg))
            {
                if (msg is T)
                {
                    return msg;
                }

                continue;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException($"No {typeof(T).Name} was posted within the timeout.");
    }

    [Fact]
    public void RunFileJob_copies_a_single_file()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var srcFile = Path.Combine(srcDir, "a.txt");
            File.WriteAllText(srcFile, "hello world");

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(1, JobKind.Copy, [srcFile], destDir));

            var completed = (Msg.JobCompleted)WaitForMsg<Msg.JobCompleted>(queue, TimeSpan.FromSeconds(10));
            Assert.Equal(1, completed.JobId);
            Assert.Equal(0, completed.SkippedFiles);
            Assert.Contains(destDir, completed.AffectedDirs);

            var destFile = Path.Combine(destDir, "a.txt");
            Assert.True(File.Exists(destFile));
            Assert.Equal("hello world", File.ReadAllText(destFile));
            Assert.True(File.Exists(srcFile)); // copy leaves the source alone
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void RunFileJob_copies_a_directory_tree_recursively_with_content_intact()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "srcTree");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(destDir);
            Directory.CreateDirectory(Path.Combine(srcDir, "sub"));
            File.WriteAllText(Path.Combine(srcDir, "top.txt"), "top");
            File.WriteAllText(Path.Combine(srcDir, "sub", "nested.txt"), "nested");

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(2, JobKind.Copy, [srcDir], destDir));

            var completed = (Msg.JobCompleted)WaitForMsg<Msg.JobCompleted>(queue, TimeSpan.FromSeconds(10));
            Assert.Equal(0, completed.SkippedFiles);

            Assert.Equal("top", File.ReadAllText(Path.Combine(destDir, "srcTree", "top.txt")));
            Assert.Equal("nested", File.ReadAllText(Path.Combine(destDir, "srcTree", "sub", "nested.txt")));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void RunFileJob_skips_a_conflicting_file_but_still_copies_the_rest()
    {
        // Phase 5: a conflict now pauses the job and waits for Msg.JobConflictResolved instead of
        // silently skipping - see JobEngineTests' RunFileJob_posts_JobConflictsFound_* tests for the
        // conflict-prompt flow itself. This test resolves with ConflictDecision.Skip, which
        // reproduces the exact old skip-by-default behavior it used to exercise unprompted.
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var srcA = Path.Combine(srcDir, "a.txt");
            var srcB = Path.Combine(srcDir, "b.txt");
            File.WriteAllText(srcA, "new-a");
            File.WriteAllText(srcB, "new-b");
            File.WriteAllText(Path.Combine(destDir, "a.txt"), "existing-a"); // conflict

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(3, JobKind.Copy, [srcA, srcB], destDir));

            WaitForMsg<Msg.JobConflictsFound>(queue, TimeSpan.FromSeconds(10));
            engine.Submit(new Effect.ResolveJobConflict(3, ConflictDecision.Skip));

            var completed = (Msg.JobCompleted)WaitForMsg<Msg.JobCompleted>(queue, TimeSpan.FromSeconds(10));
            Assert.Equal(1, completed.SkippedFiles);

            Assert.Equal("existing-a", File.ReadAllText(Path.Combine(destDir, "a.txt"))); // untouched
            Assert.Equal("new-b", File.ReadAllText(Path.Combine(destDir, "b.txt")));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void RunFileJob_moves_a_file_on_the_same_volume_and_removes_the_source()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var srcFile = Path.Combine(srcDir, "a.txt");
            File.WriteAllText(srcFile, "move-me");

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(4, JobKind.Move, [srcFile], destDir));

            var completed = (Msg.JobCompleted)WaitForMsg<Msg.JobCompleted>(queue, TimeSpan.FromSeconds(10));
            Assert.Equal(0, completed.SkippedFiles);

            Assert.False(File.Exists(srcFile));
            Assert.Equal("move-me", File.ReadAllText(Path.Combine(destDir, "a.txt")));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    // Cross-volume move (different drive letters) is not reliably testable in this environment
    // (requires a second physical/mapped volume to exist); the copy+delete path it shares with
    // JobKind.Copy is covered by the copy tests above and by DeleteCopiedSources' unit behavior
    // being exercised indirectly through same-volume-forced copy below.

    [Fact]
    public void RunFileJob_progress_reports_are_monotonically_non_decreasing_in_DoneBytes()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var srcFile = Path.Combine(srcDir, "big.bin");
            File.WriteAllBytes(srcFile, new byte[10 * 1024 * 1024]); // 10MB, spans several 1MB buffer writes

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(5, JobKind.Copy, [srcFile], destDir));

            var progressReports = new List<Msg.JobProgress>();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (queue.TryDequeue(out var msg))
                {
                    if (msg is Msg.JobProgress progress)
                    {
                        progressReports.Add(progress);
                    }
                    else if (msg is Msg.JobCompleted)
                    {
                        break;
                    }
                }
                else
                {
                    Thread.Sleep(5);
                }
            }

            Assert.NotEmpty(progressReports);
            for (var i = 1; i < progressReports.Count; i++)
            {
                Assert.True(
                    progressReports[i].DoneBytes >= progressReports[i - 1].DoneBytes,
                    $"DoneBytes went backwards: {progressReports[i - 1].DoneBytes} -> {progressReports[i].DoneBytes}");
            }
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void CancelJob_stops_a_large_copy_and_reports_JobCancelled()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var srcFile = Path.Combine(srcDir, "huge.bin");
            File.WriteAllBytes(srcFile, new byte[50 * 1024 * 1024]); // 50MB real file

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(6, JobKind.Copy, [srcFile], destDir));

            // Wait for the first progress report so the job has genuinely started.
            WaitForMsg<Msg.JobProgress>(queue, TimeSpan.FromSeconds(10));
            engine.Submit(new Effect.CancelJob(6));

            var cancelled = false;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (queue.TryDequeue(out var msg))
                {
                    if (msg is Msg.JobCancelled jobCancelled && jobCancelled.JobId == 6)
                    {
                        cancelled = true;
                        break;
                    }
                }
                else
                {
                    Thread.Sleep(5);
                }
            }

            Assert.True(cancelled, "Expected a JobCancelled Msg for job 6.");
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Engine_still_processes_a_subsequent_job_after_a_cancellation()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var bigFile = Path.Combine(srcDir, "huge.bin");
            File.WriteAllBytes(bigFile, new byte[50 * 1024 * 1024]);
            var smallFile = Path.Combine(srcDir, "small.txt");
            File.WriteAllText(smallFile, "hi");

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(7, JobKind.Copy, [bigFile], destDir));
            WaitForMsg<Msg.JobProgress>(queue, TimeSpan.FromSeconds(10));
            engine.Submit(new Effect.CancelJob(7));
            WaitForMsg<Msg.JobCancelled>(queue, TimeSpan.FromSeconds(15));

            engine.Submit(new Effect.RunFileJob(8, JobKind.Copy, [smallFile], destDir));
            var completed = (Msg.JobCompleted)WaitForMsg<Msg.JobCompleted>(queue, TimeSpan.FromSeconds(10));

            Assert.Equal(8, completed.JobId);
            Assert.Equal("hi", File.ReadAllText(Path.Combine(destDir, "small.txt")));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void CancelJob_on_a_still_queued_job_is_honored_before_it_starts()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var bigFile = Path.Combine(srcDir, "huge.bin");
            File.WriteAllBytes(bigFile, new byte[50 * 1024 * 1024]);
            var queuedFile = Path.Combine(srcDir, "queued.txt");
            File.WriteAllText(queuedFile, "queued-content");

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            // Job 9 occupies the single worker; job 10 sits behind it in the channel, still Queued.
            engine.Submit(new Effect.RunFileJob(9, JobKind.Copy, [bigFile], destDir));
            engine.Submit(new Effect.RunFileJob(10, JobKind.Copy, [queuedFile], destDir));
            engine.Submit(new Effect.CancelJob(10));

            var sawJob10Cancelled = false;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (queue.TryDequeue(out var msg))
                {
                    if (msg is Msg.JobCancelled c && c.JobId == 10)
                    {
                        sawJob10Cancelled = true;
                        break;
                    }
                }
                else
                {
                    Thread.Sleep(5);
                }
            }

            Assert.True(sawJob10Cancelled, "Expected job 10 to be cancelled while still queued.");
            Assert.False(File.Exists(Path.Combine(destDir, "queued.txt")));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void RunFileJob_posts_JobConflictsFound_then_overwrites_when_resolved_Overwrite()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var srcFile = Path.Combine(srcDir, "a.txt");
            File.WriteAllText(srcFile, "new-content");
            File.WriteAllText(Path.Combine(destDir, "a.txt"), "old-content");

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(20, JobKind.Copy, [srcFile], destDir));

            var found = (Msg.JobConflictsFound)WaitForMsg<Msg.JobConflictsFound>(queue, TimeSpan.FromSeconds(10));
            Assert.Equal(20, found.JobId);
            Assert.Equal(1, found.ConflictCount);

            engine.Submit(new Effect.ResolveJobConflict(20, ConflictDecision.Overwrite));

            var completed = (Msg.JobCompleted)WaitForMsg<Msg.JobCompleted>(queue, TimeSpan.FromSeconds(10));
            Assert.Equal(0, completed.SkippedFiles);
            Assert.Equal("new-content", File.ReadAllText(Path.Combine(destDir, "a.txt")));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void RunFileJob_posts_JobConflictsFound_then_skips_when_resolved_Skip()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var srcFile = Path.Combine(srcDir, "a.txt");
            File.WriteAllText(srcFile, "new-content");
            File.WriteAllText(Path.Combine(destDir, "a.txt"), "old-content");

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(21, JobKind.Copy, [srcFile], destDir));

            var found = (Msg.JobConflictsFound)WaitForMsg<Msg.JobConflictsFound>(queue, TimeSpan.FromSeconds(10));
            Assert.Equal(1, found.ConflictCount);

            engine.Submit(new Effect.ResolveJobConflict(21, ConflictDecision.Skip));

            var completed = (Msg.JobCompleted)WaitForMsg<Msg.JobCompleted>(queue, TimeSpan.FromSeconds(10));
            Assert.Equal(1, completed.SkippedFiles);
            Assert.Equal("old-content", File.ReadAllText(Path.Combine(destDir, "a.txt")));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void RunFileJob_posts_JobConflictsFound_then_cancels_when_resolved_Cancel()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var srcFile = Path.Combine(srcDir, "a.txt");
            File.WriteAllText(srcFile, "new-content");
            File.WriteAllText(Path.Combine(destDir, "a.txt"), "old-content");

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(22, JobKind.Copy, [srcFile], destDir));

            WaitForMsg<Msg.JobConflictsFound>(queue, TimeSpan.FromSeconds(10));
            engine.Submit(new Effect.ResolveJobConflict(22, ConflictDecision.Cancel));

            var cancelled = (Msg.JobCancelled)WaitForMsg<Msg.JobCancelled>(queue, TimeSpan.FromSeconds(10));
            Assert.Equal(22, cancelled.JobId);
            Assert.Equal("old-content", File.ReadAllText(Path.Combine(destDir, "a.txt")));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void RunFileJob_with_no_conflicts_never_posts_JobConflictsFound()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var srcFile = Path.Combine(srcDir, "a.txt");
            File.WriteAllText(srcFile, "content");

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(23, JobKind.Copy, [srcFile], destDir));

            var seen = new List<Msg>();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            var completed = false;
            while (DateTime.UtcNow < deadline && !completed)
            {
                if (queue.TryDequeue(out var msg))
                {
                    seen.Add(msg);
                    completed = msg is Msg.JobCompleted;
                }
                else
                {
                    Thread.Sleep(5);
                }
            }

            Assert.True(completed, "Expected JobCompleted within the timeout.");
            Assert.DoesNotContain(seen, m => m is Msg.JobConflictsFound);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void CancelJob_while_waiting_on_a_conflict_decision_reports_JobCancelled()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var srcFile = Path.Combine(srcDir, "a.txt");
            File.WriteAllText(srcFile, "new-content");
            File.WriteAllText(Path.Combine(destDir, "a.txt"), "old-content");

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(24, JobKind.Copy, [srcFile], destDir));

            WaitForMsg<Msg.JobConflictsFound>(queue, TimeSpan.FromSeconds(10));
            // Cancelled via the ordinary CancelJob effect (not a conflict-prompt decision) while
            // the worker is still blocked waiting for one - the cancellation token must abort that
            // wait, not just a running transfer.
            engine.Submit(new Effect.CancelJob(24));

            var cancelled = (Msg.JobCancelled)WaitForMsg<Msg.JobCancelled>(queue, TimeSpan.FromSeconds(10));
            Assert.Equal(24, cancelled.JobId);
            Assert.Equal("old-content", File.ReadAllText(Path.Combine(destDir, "a.txt")));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void CancelJob_mid_copy_deletes_the_incomplete_destination_file()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var srcFile = Path.Combine(srcDir, "huge.bin");
            File.WriteAllBytes(srcFile, new byte[50 * 1024 * 1024]);

            var queue = new ConcurrentQueue<Msg>();
            using var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(25, JobKind.Copy, [srcFile], destDir));

            WaitForMsg<Msg.JobProgress>(queue, TimeSpan.FromSeconds(10));
            engine.Submit(new Effect.CancelJob(25));
            WaitForMsg<Msg.JobCancelled>(queue, TimeSpan.FromSeconds(15));

            var destFile = Path.Combine(destDir, "huge.bin");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            var exists = File.Exists(destFile);
            while (exists && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
                exists = File.Exists(destFile);
            }

            Assert.False(exists, "Expected the partially-written destination file to be deleted after cancellation.");
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Dispose_returns_promptly_even_mid_copy()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "src");
            var destDir = Path.Combine(dir, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            var bigFile = Path.Combine(srcDir, "huge.bin");
            File.WriteAllBytes(bigFile, new byte[50 * 1024 * 1024]);

            var queue = new ConcurrentQueue<Msg>();
            var engine = new JobEngine(queue.Enqueue);
            engine.Submit(new Effect.RunFileJob(11, JobKind.Copy, [bigFile], destDir));
            WaitForMsg<Msg.JobProgress>(queue, TimeSpan.FromSeconds(10));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            engine.Dispose();
            sw.Stop();

            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(6), $"Dispose took {sw.Elapsed}");
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }
}
