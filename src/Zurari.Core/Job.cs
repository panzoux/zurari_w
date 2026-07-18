using System.Collections.Immutable;

namespace Zurari.Core;

/// <summary>What kind of file transfer a <see cref="Job"/> performs.</summary>
public enum JobKind
{
    /// <summary>Sources are copied into the destination; originals are left in place.</summary>
    Copy,

    /// <summary>Sources are moved into the destination; originals are removed.</summary>
    Move,
}

/// <summary>Lifecycle state of a <see cref="Job"/>.</summary>
public enum JobStatus
{
    /// <summary>Submitted to the engine but not yet picked up by the worker.</summary>
    Queued,

    /// <summary>The worker is actively transferring files for this job.</summary>
    Running,

    /// <summary>Finished successfully (possibly with some files skipped due to conflicts).</summary>
    Completed,

    /// <summary>Finished because of an unexpected error; see <see cref="Job.Error"/>.</summary>
    Failed,

    /// <summary>Finished because <see cref="Msg.JobCancelRequested"/> was honored.</summary>
    Cancelled,
}

/// <summary>
/// A background copy/move operation and its live progress, as tracked in <see cref="AppState.Jobs"/>.
/// Contains no I/O types (BannedSymbols enforces this) - everything the Runtime learns while
/// executing the job is reduced to this shape via <see cref="Msg.JobProgress"/> and friends.
/// </summary>
/// <param name="JobId">Stable identifier, unique within a single <see cref="AppState"/>.</param>
/// <param name="Kind">Whether this job copies or moves its sources.</param>
/// <param name="Sources">Full paths of the top-level files/directories being transferred.</param>
/// <param name="DestDir">Full path of the directory the sources are transferred into.</param>
/// <param name="Status">Current lifecycle state.</param>
/// <param name="DoneFiles">Number of files fully processed so far (including skipped ones).</param>
/// <param name="TotalFiles">Total number of files the pre-scan found, or 0 before the first progress report.</param>
/// <param name="DoneBytes">Bytes transferred so far (skipped files count their full size as done).</param>
/// <param name="TotalBytes">Total bytes the pre-scan found, or 0 before the first progress report.</param>
/// <param name="CurrentFile">Name of the file currently being transferred, or <c>null</c> when idle/finished.</param>
/// <param name="Error">Set only when <see cref="Status"/> is <see cref="JobStatus.Failed"/>.</param>
/// <param name="SkippedFiles">Number of files skipped because a same-named entry already existed at the destination.</param>
public sealed record Job(
    int JobId,
    JobKind Kind,
    ImmutableArray<string> Sources,
    string DestDir,
    JobStatus Status = JobStatus.Queued,
    int DoneFiles = 0,
    int TotalFiles = 0,
    long DoneBytes = 0,
    long TotalBytes = 0,
    string? CurrentFile = null,
    string? Error = null,
    int SkippedFiles = 0);
