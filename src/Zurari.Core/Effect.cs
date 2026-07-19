using System.Collections.Immutable;

namespace Zurari.Core;

/// <summary>
/// A description of I/O the Runtime must perform on behalf of Core (read a
/// directory, copy files, load a preview...). Core never performs I/O itself;
/// it returns effects from <see cref="Transition.Apply"/> and later receives
/// the outcome as a <see cref="Msg"/>.
/// </summary>
public abstract record Effect
{
    private protected Effect()
    {
    }

    /// <summary>
    /// Enumerate the directory at <paramref name="Path"/> and report it back as
    /// <see cref="Msg.DirectoryLoaded"/> or <see cref="Msg.DirectoryLoadFailed"/> for
    /// <paramref name="ColumnIndex"/>. An empty <paramref name="Path"/> means the virtual root:
    /// the Runtime enumerates drives instead of a filesystem directory.
    /// </summary>
    public sealed record ReadDirectory(int ColumnIndex, string Path) : Effect;

    /// <summary>
    /// Move every path in <paramref name="Targets"/> to the recycle bin as a single batch
    /// operation - or, when <paramref name="Permanent"/> is <c>true</c>, delete them outright,
    /// bypassing the recycle bin (Shift+Delete). Reports the outcome back as
    /// <see cref="Msg.ShellOpCompleted"/> or <see cref="Msg.ShellOpFailed"/> for
    /// <paramref name="ColumnIndex"/>/<paramref name="Path"/> (the containing column, so a
    /// successful delete can trigger a re-read of that column).
    /// </summary>
    public sealed record DeleteToRecycleBin(
        int ColumnIndex, string Path, ImmutableArray<string> Targets, bool Permanent = false) : Effect;

    /// <summary>
    /// Copy or move <paramref name="Paths"/> into <paramref name="DestPath"/> via the shell's
    /// <c>IFileOperation</c>, which owns its own progress/overwrite UI. <paramref name="DestPath"/>
    /// may be a subdirectory row within the column rather than the column's own path (a
    /// row-granular drop), so the outcome is reported back against <paramref name="ColumnPath"/> -
    /// the path shown by <paramref name="ColumnIndex"/> at the time the drop was issued - as
    /// <see cref="Msg.ShellOpCompleted"/> or <see cref="Msg.ShellOpFailed"/>, matching the
    /// staleness contract every other column-keyed result follows (ignored unless the column still
    /// shows that path) and triggering a re-read of the column, not the row that was dropped onto.
    /// </summary>
    public sealed record ShellCopyOrMove(
        int ColumnIndex, string ColumnPath, string DestPath, ImmutableArray<string> Paths, bool IsMove) : Effect;

    /// <summary>
    /// Runs the internal job engine's copy or move of <paramref name="Sources"/> into
    /// <paramref name="DestDir"/> for <paramref name="JobId"/>. Reports progress via
    /// <see cref="Msg.JobProgress"/> and finishes with <see cref="Msg.JobCompleted"/>,
    /// <see cref="Msg.JobFailed"/>, or <see cref="Msg.JobCancelled"/>.
    /// </summary>
    public sealed record RunFileJob(
        int JobId, JobKind Kind, ImmutableArray<string> Sources, string DestDir) : Effect;

    /// <summary>
    /// Requests cooperative cancellation of the job engine's <paramref name="JobId"/>, whether it
    /// is still queued or already running. The engine confirms via <see cref="Msg.JobCancelled"/>;
    /// this effect itself never produces a Msg.
    /// </summary>
    public sealed record CancelJob(int JobId) : Effect;

    /// <summary>
    /// Loads a preview of the file at <paramref name="Path"/>: image bytes (whole file, capped),
    /// decoded text (head only), or a <see cref="FileTypeDetector"/> label for anything else.
    /// Reports back as <see cref="Msg.PreviewLoaded"/> or <see cref="Msg.PreviewFailed"/>, both
    /// carrying <paramref name="Generation"/> unchanged so a superseded request's result can be
    /// told apart from the current one and discarded.
    /// </summary>
    public sealed record LoadPreview(int Generation, string Path) : Effect;
}
