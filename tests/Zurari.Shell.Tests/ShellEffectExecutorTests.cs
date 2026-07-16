using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using Zurari.Core;

namespace Zurari.Shell.Tests;

public class ShellEffectExecutorTests
{
    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "zurari-shell-tests-" + Guid.NewGuid());
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

    [StaFact]
    public void DeleteToRecycleBin_of_temp_file_reports_completed_and_removes_file()
    {
        var dir = CreateTempDir();
        try
        {
            var filePath = Path.Combine(dir, "victim.txt");
            File.WriteAllText(filePath, "bye");

            var queue = new ConcurrentQueue<Msg>();
            using var executor = new ShellEffectExecutor(queue.Enqueue);
            executor.Submit(new Effect.DeleteToRecycleBin(0, dir, [filePath]));

            var msg = WaitForMsg(queue, TimeSpan.FromSeconds(15));
            var completed = Assert.IsType<Msg.ShellOpCompleted>(msg);
            Assert.Equal(0, completed.ColumnIndex);
            Assert.Equal(dir, completed.Path);
            Assert.Equal([dir], completed.AffectedDirs);
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [StaFact]
    public void DeleteToRecycleBin_of_temp_directory_with_content_reports_completed_and_removes_it()
    {
        var dir = CreateTempDir();
        try
        {
            var subDir = Path.Combine(dir, "victim-dir");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "inner.txt"), "content");

            var queue = new ConcurrentQueue<Msg>();
            using var executor = new ShellEffectExecutor(queue.Enqueue);
            executor.Submit(new Effect.DeleteToRecycleBin(0, dir, [subDir]));

            var msg = WaitForMsg(queue, TimeSpan.FromSeconds(15));
            var completed = Assert.IsType<Msg.ShellOpCompleted>(msg);
            Assert.Equal(0, completed.ColumnIndex);
            Assert.Equal(dir, completed.Path);
            Assert.Equal([dir], completed.AffectedDirs);
            Assert.False(Directory.Exists(subDir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [StaFact]
    public void DeleteToRecycleBin_of_nonexistent_path_reports_failure()
    {
        var dir = CreateTempDir();
        try
        {
            var missing = Path.Combine(dir, "does-not-exist-" + Guid.NewGuid() + ".txt");

            var queue = new ConcurrentQueue<Msg>();
            using var executor = new ShellEffectExecutor(queue.Enqueue);
            executor.Submit(new Effect.DeleteToRecycleBin(2, dir, [missing]));

            var msg = WaitForMsg(queue, TimeSpan.FromSeconds(15));
            var failed = Assert.IsType<Msg.ShellOpFailed>(msg);
            Assert.Equal(2, failed.ColumnIndex);
            Assert.Equal(dir, failed.Path);
            Assert.False(string.IsNullOrEmpty(failed.Error));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [StaFact]
    public void DeleteToRecycleBin_of_two_temp_files_reports_completed_and_removes_both()
    {
        var dir = CreateTempDir();
        try
        {
            var filePath1 = Path.Combine(dir, "victim1.txt");
            var filePath2 = Path.Combine(dir, "victim2.txt");
            File.WriteAllText(filePath1, "bye1");
            File.WriteAllText(filePath2, "bye2");

            var queue = new ConcurrentQueue<Msg>();
            using var executor = new ShellEffectExecutor(queue.Enqueue);
            executor.Submit(new Effect.DeleteToRecycleBin(0, dir, [filePath1, filePath2]));

            var msg = WaitForMsg(queue, TimeSpan.FromSeconds(15));
            var completed = Assert.IsType<Msg.ShellOpCompleted>(msg);
            Assert.Equal(0, completed.ColumnIndex);
            Assert.Equal(dir, completed.Path);
            Assert.Equal([dir], completed.AffectedDirs);
            Assert.False(File.Exists(filePath1));
            Assert.False(File.Exists(filePath2));
            Assert.Empty(queue);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [StaFact]
    public void Dispose_returns_promptly_even_with_queued_work()
    {
        var dir = CreateTempDir();
        try
        {
            var queue = new ConcurrentQueue<Msg>();
            var executor = new ShellEffectExecutor(queue.Enqueue);
            for (var i = 0; i < 50; i++)
            {
                executor.Submit(new Effect.DeleteToRecycleBin(i, dir, [Path.Combine(dir, $"missing-{i}.txt")]));
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            executor.Dispose();
            sw.Stop();

            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Dispose took {sw.Elapsed}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [StaFact]
    public void ShellCopyOrMove_copy_of_temp_file_reports_completed_and_leaves_source_intact()
    {
        var srcDir = CreateTempDir();
        var destDir = CreateTempDir();
        try
        {
            var srcFile = Path.Combine(srcDir, "source.txt");
            File.WriteAllText(srcFile, "hello");

            var queue = new ConcurrentQueue<Msg>();
            using var executor = new ShellEffectExecutor(queue.Enqueue, suppressUi: true);
            executor.Submit(new Effect.ShellCopyOrMove(0, destDir, destDir, [srcFile], IsMove: false));

            var msg = WaitForMsg(queue, TimeSpan.FromSeconds(15));
            var completed = Assert.IsType<Msg.ShellOpCompleted>(msg);
            Assert.Equal(0, completed.ColumnIndex);
            Assert.Equal(destDir, completed.Path);
            // A copy leaves the source untouched, so only the destination is affected.
            Assert.Equal([destDir], completed.AffectedDirs);
            Assert.True(File.Exists(srcFile));
            Assert.True(File.Exists(Path.Combine(destDir, "source.txt")));
        }
        finally
        {
            Directory.Delete(srcDir, recursive: true);
            Directory.Delete(destDir, recursive: true);
        }
    }

    [StaFact]
    public void ShellCopyOrMove_into_a_subdirectory_row_reports_completed_against_the_column_path()
    {
        var srcDir = CreateTempDir();
        var columnDir = CreateTempDir();
        try
        {
            var srcFile = Path.Combine(srcDir, "source.txt");
            File.WriteAllText(srcFile, "hello");

            var subDir = Path.Combine(columnDir, "row-target");
            Directory.CreateDirectory(subDir);

            var queue = new ConcurrentQueue<Msg>();
            using var executor = new ShellEffectExecutor(queue.Enqueue, suppressUi: true);
            // ColumnPath (columnDir) differs from DestPath (subDir): a row-granular drop onto a
            // directory row within the column, not the column's own background.
            executor.Submit(new Effect.ShellCopyOrMove(0, columnDir, subDir, [srcFile], IsMove: false));

            var msg = WaitForMsg(queue, TimeSpan.FromSeconds(15));
            var completed = Assert.IsType<Msg.ShellOpCompleted>(msg);
            Assert.Equal(0, completed.ColumnIndex);
            Assert.Equal(columnDir, completed.Path);
            // AffectedDirs is keyed by DestPath (the row that was actually dropped onto), not
            // ColumnPath - the row-target subdirectory is what changed, not the column background.
            Assert.Equal([subDir], completed.AffectedDirs);
            Assert.True(File.Exists(srcFile));
            Assert.True(File.Exists(Path.Combine(subDir, "source.txt")));
        }
        finally
        {
            Directory.Delete(srcDir, recursive: true);
            Directory.Delete(columnDir, recursive: true);
        }
    }

    [StaFact]
    public void ShellCopyOrMove_move_of_temp_file_reports_completed_and_removes_source()
    {
        var srcDir = CreateTempDir();
        var destDir = CreateTempDir();
        try
        {
            var srcFile = Path.Combine(srcDir, "source.txt");
            File.WriteAllText(srcFile, "hello");

            var queue = new ConcurrentQueue<Msg>();
            using var executor = new ShellEffectExecutor(queue.Enqueue, suppressUi: true);
            executor.Submit(new Effect.ShellCopyOrMove(0, destDir, destDir, [srcFile], IsMove: true));

            var msg = WaitForMsg(queue, TimeSpan.FromSeconds(15));
            var completed = Assert.IsType<Msg.ShellOpCompleted>(msg);
            Assert.Equal(destDir, completed.Path);
            // A move also changes the source directory, so its parent is affected too.
            Assert.Equal([destDir, srcDir], completed.AffectedDirs);
            Assert.False(File.Exists(srcFile));
            Assert.True(File.Exists(Path.Combine(destDir, "source.txt")));
        }
        finally
        {
            Directory.Delete(srcDir, recursive: true);
            Directory.Delete(destDir, recursive: true);
        }
    }

    [StaFact]
    public void ShellCopyOrMove_of_nonexistent_source_reports_failure()
    {
        var destDir = CreateTempDir();
        try
        {
            var missing = Path.Combine(destDir, "does-not-exist-" + Guid.NewGuid() + ".txt");

            var queue = new ConcurrentQueue<Msg>();
            using var executor = new ShellEffectExecutor(queue.Enqueue, suppressUi: true);
            executor.Submit(new Effect.ShellCopyOrMove(3, destDir, destDir, [missing], IsMove: false));

            var msg = WaitForMsg(queue, TimeSpan.FromSeconds(15));
            var failed = Assert.IsType<Msg.ShellOpFailed>(msg);
            Assert.Equal(3, failed.ColumnIndex);
            Assert.Equal(destDir, failed.Path);
            Assert.False(string.IsNullOrEmpty(failed.Error));
        }
        finally
        {
            Directory.Delete(destDir, recursive: true);
        }
    }
}
