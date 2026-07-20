using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Channels;
using Zurari.Core;

namespace Zurari.Runtime;

/// <summary>
/// Executes <see cref="Effect.RunFileJob"/> and <see cref="Effect.CancelJob"/>: a single dedicated
/// worker runs jobs one at a time (serial, so a big copy does not fight a later paste for disk
/// bandwidth), reporting progress and outcome back as <see cref="Msg"/>s. This is the only layer
/// allowed to copy/move files for pasted jobs; Core stays pure and describes the work as data.
/// </summary>
public sealed class JobEngine : IDisposable
{
    private const int BufferSize = 1024 * 1024;
    private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(100);

    private readonly Channel<Effect.RunFileJob> _channel;
    private readonly Action<Msg> _post;
    private readonly CancellationTokenSource _engineCts;
    private readonly Task _worker;
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _jobCancellations = new();

    /// <summary>
    /// One pending conflict decision per job currently blocked in <see cref="ExecuteRunFileJob"/>
    /// waiting on <see cref="Msg.JobConflictsFound"/>'s prompt. <see cref="Submit"/> completes the
    /// entry synchronously for <see cref="Effect.ResolveJobConflict"/> - same pattern as
    /// <see cref="_jobCancellations"/>/<see cref="Effect.CancelJob"/>.
    /// </summary>
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ConflictDecision>> _pendingConflicts = new();

    /// <summary>
    /// Starts the dedicated job worker. <paramref name="post"/> may be invoked from the worker
    /// thread; the caller is responsible for marshalling results onto whatever thread owns
    /// application state (the UI thread in the real app).
    /// </summary>
    public JobEngine(Action<Msg> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        _post = post;
        _channel = Channel.CreateUnbounded<Effect.RunFileJob>();
        _engineCts = new CancellationTokenSource();
        _worker = Task.Run(() => RunWorkerAsync(_engineCts.Token));
    }

    /// <summary>
    /// Enqueues <see cref="Effect.RunFileJob"/> for serial execution, or requests cancellation of
    /// an already-submitted job for <see cref="Effect.CancelJob"/> (whether it is still queued or
    /// already running). Never blocks. Any other effect type is ignored.
    /// </summary>
    public void Submit(Effect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        switch (effect)
        {
            case Effect.RunFileJob runFileJob:
                // Registered before the job is queued so a CancelJob racing in immediately after
                // still finds it, whether the job has started running yet or not.
                _jobCancellations[runFileJob.JobId] = new CancellationTokenSource();
                _channel.Writer.TryWrite(runFileJob);
                break;

            case Effect.CancelJob cancelJob:
                if (_jobCancellations.TryGetValue(cancelJob.JobId, out var cts))
                {
                    TryCancel(cts);
                }

                break;

            case Effect.ResolveJobConflict resolveJobConflict:
                // Handled synchronously, like CancelJob above: the worker thread is blocked inside
                // ExecuteRunFileJob waiting on this exact TaskCompletionSource, so completing it
                // here (whatever thread Submit is called from) is what wakes it back up.
                if (_pendingConflicts.TryGetValue(resolveJobConflict.JobId, out var tcs))
                {
                    tcs.TrySetResult(resolveJobConflict.Decision);
                }

                break;
        }
    }

    private static void TryCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The job already finished and disposed its token; nothing left to cancel.
        }
    }

    private async Task RunWorkerAsync(CancellationToken engineToken)
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(engineToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var effect))
                {
                    if (engineToken.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        ExecuteRunFileJob(effect);
                    }
                    catch (Exception)
                    {
                        // ExecuteRunFileJob already reports failures via JobFailed; this is a
                        // last-resort guard so the worker never dies and stops processing later jobs.
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose() requested cancellation; exit quietly.
        }
        catch (ChannelClosedException)
        {
            // Writer completed while we were waiting; exit quietly.
        }
    }

    private void ExecuteRunFileJob(Effect.RunFileJob effect)
    {
        _jobCancellations.TryGetValue(effect.JobId, out var jobCts);
        jobCts ??= new CancellationTokenSource();
        var token = jobCts.Token;
        var affectedDirs = ComputeAffectedDirs(effect);

        try
        {
            if (token.IsCancellationRequested)
            {
                _post(new Msg.JobCancelled(effect.JobId, affectedDirs));
                return;
            }

            var (files, totalBytes, scanSkipped) = ScanSources(effect.Sources);
            var sameVolumeMove = effect.Kind == JobKind.Move && AllSameVolume(effect.Sources, effect.DestDir);
            var conflicts = ComputeConflicts(effect, files, sameVolumeMove);

            var decision = ConflictDecision.Skip;
            if (conflicts.Count > 0)
            {
                decision = AwaitConflictDecision(effect.JobId, conflicts.Count, token);
                if (decision == ConflictDecision.Cancel)
                {
                    _post(new Msg.JobCancelled(effect.JobId, affectedDirs));
                    return;
                }
            }

            var overwrite = decision == ConflictDecision.Overwrite;
            var progress = new JobProgressState(_post, effect.JobId)
            {
                TotalFiles = files.Count,
                TotalBytes = totalBytes,
                Skipped = scanSkipped,
            };
            progress.PostInitial();

            if (sameVolumeMove)
            {
                var topLevelStats = ComputeTopLevelStats(files);
                MoveSameVolume(effect.Sources, effect.DestDir, progress, topLevelStats, overwrite, token);
            }
            else
            {
                CopyFiles(files, effect.DestDir, progress, overwrite, token);
                if (effect.Kind == JobKind.Move)
                {
                    DeleteCopiedSources(files, progress.CopiedSourcePaths, effect.Sources);
                }
            }

            progress.MaybePost(force: true);
            _post(new Msg.JobCompleted(effect.JobId, progress.Skipped, affectedDirs));
        }
        catch (OperationCanceledException)
        {
            _post(new Msg.JobCancelled(effect.JobId, affectedDirs));
        }
        catch (Exception ex)
        {
            _post(new Msg.JobFailed(effect.JobId, ex.Message));
        }
        finally
        {
            _jobCancellations.TryRemove(effect.JobId, out _);
            jobCts.Dispose();
        }
    }

    /// <summary>
    /// <see cref="Effect.RunFileJob.DestDir"/> plus, for a move only, the distinct parents of
    /// <see cref="Effect.RunFileJob.Sources"/> - same convention as <c>ShellOpCompleted</c>'s
    /// <c>AffectedDirs</c>.
    /// </summary>
    private static ImmutableArray<string> ComputeAffectedDirs(Effect.RunFileJob effect)
    {
        var dirs = new List<string> { effect.DestDir };
        if (effect.Kind == JobKind.Move)
        {
            foreach (var source in effect.Sources)
            {
                var parent = Path.GetDirectoryName(source.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(parent)
                    && !dirs.Contains(parent, StringComparer.OrdinalIgnoreCase))
                {
                    dirs.Add(parent);
                }
            }
        }

        return [.. dirs];
    }

    /// <summary>
    /// Destination paths that already exist and would collide with a source of the same name -
    /// computed the same way <see cref="MoveSameVolume"/> or <see cref="CopyFiles"/> checks for a
    /// conflict, without performing any transfer. Its count is what <see cref="Msg.JobConflictsFound"/>
    /// reports and what drives the prompt in <see cref="AwaitConflictDecision"/>.
    /// </summary>
    private static HashSet<string> ComputeConflicts(
        Effect.RunFileJob effect, List<ScannedFile> files, bool sameVolumeMove)
    {
        var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (sameVolumeMove)
        {
            foreach (var source in effect.Sources)
            {
                var trimmed = source.TrimEnd('\\', '/');
                var name = Path.GetFileName(trimmed);
                var dest = Path.Combine(effect.DestDir, name);
                if (Directory.Exists(dest) || File.Exists(dest))
                {
                    conflicts.Add(dest);
                }
            }
        }
        else
        {
            foreach (var file in files)
            {
                var destPath = Path.Combine(effect.DestDir, file.RelativePath);
                if (File.Exists(destPath))
                {
                    conflicts.Add(destPath);
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Posts <see cref="Msg.JobConflictsFound"/> for <paramref name="jobId"/>/<paramref name="conflictCount"/>
    /// and blocks the calling (worker) thread until <see cref="Effect.ResolveJobConflict"/> completes
    /// the matching entry in <see cref="_pendingConflicts"/> via <see cref="Submit"/>, or
    /// <paramref name="token"/> is cancelled - which throws <see cref="OperationCanceledException"/>,
    /// left to the caller's existing cancellation handling (reported as <see cref="Msg.JobCancelled"/>
    /// just like any other cancellation during the job).
    /// </summary>
    private ConflictDecision AwaitConflictDecision(int jobId, int conflictCount, CancellationToken token)
    {
        _post(new Msg.JobConflictsFound(jobId, conflictCount));

        var tcs = new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingConflicts[jobId] = tcs;
        try
        {
            tcs.Task.Wait(token);
            return tcs.Task.Result;
        }
        finally
        {
            _pendingConflicts.TryRemove(jobId, out _);
        }
    }

    private static bool AllSameVolume(ImmutableArray<string> sources, string destDir)
    {
        var destRoot = Path.GetPathRoot(destDir);
        foreach (var source in sources)
        {
            if (!string.Equals(Path.GetPathRoot(source), destRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static void MoveSameVolume(
        ImmutableArray<string> sources,
        string destDir,
        JobProgressState progress,
        Dictionary<string, (long Size, int Count)> topLevelStats,
        bool overwrite,
        CancellationToken token)
    {
        foreach (var source in sources)
        {
            token.ThrowIfCancellationRequested();

            var trimmed = source.TrimEnd('\\', '/');
            var name = Path.GetFileName(trimmed);
            topLevelStats.TryGetValue(name, out var stat);
            progress.CurrentFile = name;

            var dest = Path.Combine(destDir, name);
            var destExists = Directory.Exists(dest) || File.Exists(dest);
            if (destExists && !overwrite)
            {
                progress.Skipped += stat.Count;
            }
            else
            {
                if (destExists)
                {
                    // Overwrite: clear out whatever is already there so Directory.Move/File.Move
                    // (neither of which can replace an existing entry) can land the source in its
                    // place.
                    if (Directory.Exists(dest))
                    {
                        Directory.Delete(dest, recursive: true);
                    }
                    else
                    {
                        File.Delete(dest);
                    }
                }

                if (Directory.Exists(trimmed))
                {
                    Directory.Move(trimmed, dest);
                }
                else if (File.Exists(trimmed))
                {
                    File.Move(trimmed, dest);
                }
            }

            progress.CompleteTopLevel(stat.Count, stat.Size);
            progress.MaybePost(force: false);
        }
    }

    private static Dictionary<string, (long Size, int Count)> ComputeTopLevelStats(List<ScannedFile> files)
    {
        var stats = new Dictionary<string, (long Size, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var separatorIndex = file.RelativePath.IndexOf(Path.DirectorySeparatorChar, StringComparison.Ordinal);
            var topName = separatorIndex < 0 ? file.RelativePath : file.RelativePath[..separatorIndex];
            stats.TryGetValue(topName, out var current);
            stats[topName] = (current.Size + file.Size, current.Count + 1);
        }

        return stats;
    }

    private static void CopyFiles(
        List<ScannedFile> files, string destDir, JobProgressState progress, bool overwrite, CancellationToken token)
    {
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            var destPath = Path.Combine(destDir, file.RelativePath);
            var destParent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destParent))
            {
                Directory.CreateDirectory(destParent);
            }

            if (File.Exists(destPath) && !overwrite)
            {
                progress.Skipped++;
                progress.AddBytes(file.Size);
                progress.CompleteFile();
                progress.MaybePost(force: false);
                continue;
            }

            progress.CurrentFile = Path.GetFileName(file.RelativePath);
            CopyOneFile(file.FullPath, destPath, progress, token);
            progress.CopiedSourcePaths.Add(file.FullPath);
            progress.CompleteFile();
            progress.MaybePost(force: false);
        }
    }

    /// <summary>
    /// Copies <paramref name="sourcePath"/> to <paramref name="destPath"/> (overwriting whatever is
    /// there, via <see cref="FileMode.Create"/>). If the copy is aborted - by cancellation or any
    /// other failure mid-write - the destination is left with only a partial file; rather than leave
    /// that half-written file behind, it is deleted (best-effort - see <see cref="TryDeleteIncompleteFile"/>)
    /// before the exception propagates. Completed files are never touched by this cleanup, since it
    /// only runs when this call itself did not finish.
    /// </summary>
    private static void CopyOneFile(
        string sourcePath, string destPath, JobProgressState progress, CancellationToken token)
    {
        using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);

        try
        {
            using (var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    dest.Write(buffer, 0, read);
                    progress.AddBytes(read);
                    progress.MaybePost(force: false);
                }
            }
        }
        catch (Exception)
        {
            // The inner `using` above has already closed the handle by the time we get here (its
            // Dispose runs during unwind, before this catch), so the delete below is not blocked by
            // FileShare.None.
            TryDeleteIncompleteFile(destPath);
            throw;
        }
    }

    private static void TryDeleteIncompleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: leave the partial file rather than let cleanup itself fail the job.
        }
    }

    /// <summary>
    /// Deletes every source file that was actually copied (<paramref name="copiedSourcePaths"/>) -
    /// a conflict-skipped file's source is left untouched - then removes directories under
    /// <paramref name="topLevelSources"/> that are left empty, deepest first.
    /// </summary>
    private static void DeleteCopiedSources(
        List<ScannedFile> files, HashSet<string> copiedSourcePaths, ImmutableArray<string> topLevelSources)
    {
        foreach (var file in files)
        {
            if (!copiedSourcePaths.Contains(file.FullPath))
            {
                continue;
            }

            try
            {
                File.Delete(file.FullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort: the destination already has the data; leave the stray source file.
            }
        }

        foreach (var source in topLevelSources)
        {
            var trimmed = source.TrimEnd('\\', '/');
            if (Directory.Exists(trimmed))
            {
                TryDeleteIfEmptyRecursively(trimmed);
            }
        }
    }

    private static void TryDeleteIfEmptyRecursively(string dir)
    {
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                TryDeleteIfEmptyRecursively(sub);
            }

            if (Directory.GetFileSystemEntries(dir).Length == 0)
            {
                Directory.Delete(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it - partial cleanup is fine, the file data itself was already handled.
        }
    }

    private static (List<ScannedFile> Files, long TotalBytes, int ScanSkipped) ScanSources(
        ImmutableArray<string> sources)
    {
        var files = new List<ScannedFile>();
        long totalBytes = 0;
        var skipped = 0;

        foreach (var source in sources)
        {
            var trimmed = source.TrimEnd('\\', '/');
            var topName = Path.GetFileName(trimmed);
            try
            {
                if (Directory.Exists(trimmed))
                {
                    ScanDirectory(trimmed, topName, files, ref totalBytes, ref skipped);
                }
                else if (File.Exists(trimmed))
                {
                    var info = new FileInfo(trimmed);
                    files.Add(new ScannedFile(trimmed, topName, info.Length));
                    totalBytes += info.Length;
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        return (files, totalBytes, skipped);
    }

    private static void ScanDirectory(
        string dirPath, string relBase, List<ScannedFile> files, ref long totalBytes, ref int skipped)
    {
        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(dirPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped++;
            return;
        }

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            var rel = Path.Combine(relBase, name);
            try
            {
                if (Directory.Exists(entry))
                {
                    ScanDirectory(entry, rel, files, ref totalBytes, ref skipped);
                }
                else
                {
                    var info = new FileInfo(entry);
                    files.Add(new ScannedFile(entry, rel, info.Length));
                    totalBytes += info.Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }
    }

    /// <summary>
    /// Stops accepting new work, cancels every in-flight/queued job cooperatively, and waits
    /// (bounded to a few seconds) for the worker to exit.
    /// </summary>
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _engineCts.Cancel();
        foreach (var cts in _jobCancellations.Values)
        {
            TryCancel(cts);
        }

        try
        {
            _worker.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // A job faulted while unwinding from cancellation; nothing more to do on Dispose.
        }

        _engineCts.Dispose();
    }

    private sealed record ScannedFile(string FullPath, string RelativePath, long Size);

    /// <summary>Mutable progress accumulator for a single job's execution, including throttling.</summary>
    private sealed class JobProgressState
    {
        private readonly Action<Msg> _post;
        private readonly int _jobId;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private TimeSpan? _lastPost;

        public JobProgressState(Action<Msg> post, int jobId)
        {
            _post = post;
            _jobId = jobId;
        }

        public int TotalFiles { get; init; }

        public long TotalBytes { get; init; }

        public int DoneFiles { get; private set; }

        public long DoneBytes { get; private set; }

        public int Skipped { get; set; }

        public string? CurrentFile { get; set; }

        public HashSet<string> CopiedSourcePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void PostInitial() => _post(new Msg.JobProgress(_jobId, 0, TotalFiles, 0, TotalBytes, null));

        public void AddBytes(long delta) => DoneBytes += delta;

        public void CompleteFile()
        {
            DoneFiles++;
            CurrentFile = null;
        }

        public void CompleteTopLevel(int fileCount, long size)
        {
            DoneFiles += fileCount;
            DoneBytes += size;
            CurrentFile = null;
        }

        public void MaybePost(bool force)
        {
            var elapsed = _stopwatch.Elapsed;
            if (!force && _lastPost.HasValue && elapsed - _lastPost.Value < ProgressThrottle)
            {
                return;
            }

            _lastPost = elapsed;
            _post(new Msg.JobProgress(_jobId, DoneFiles, TotalFiles, DoneBytes, TotalBytes, CurrentFile));
        }
    }
}
