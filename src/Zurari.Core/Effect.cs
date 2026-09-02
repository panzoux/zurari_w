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
    /// Enumerate <paramref name="Location"/> and report it back as
    /// <see cref="Msg.DirectoryLoaded"/> or <see cref="Msg.DirectoryLoadFailed"/> for
    /// <paramref name="ColumnIndex"/>. How it is enumerated is the Runtime's business: a
    /// <see cref="Zurari.Core.Location.RealDirectory"/> is a filesystem listing, while
    /// <see cref="Zurari.Core.Location.Drives"/> is synthesized from the machine's drives.
    /// </summary>
    public sealed record ReadDirectory(int ColumnIndex, Location Location) : Effect;

    /// <summary>
    /// Move every path in <paramref name="Targets"/> to the recycle bin as a single batch
    /// operation - or, when <paramref name="Permanent"/> is <c>true</c>, delete them outright,
    /// bypassing the recycle bin (Shift+Delete). Reports the outcome back as
    /// <see cref="Msg.ShellOpCompleted"/> or <see cref="Msg.ShellOpFailed"/> for
    /// <paramref name="ColumnIndex"/>/<paramref name="ColumnLocation"/> (the containing column, so a
    /// successful delete can trigger a re-read of that column).
    /// </summary>
    public sealed record DeleteToRecycleBin(
        int ColumnIndex, Location ColumnLocation, ImmutableArray<string> Targets, bool Permanent = false) : Effect;

    /// <summary>
    /// Copy or move <paramref name="Paths"/> into <paramref name="DestPath"/> via the shell's
    /// <c>IFileOperation</c>, which owns its own progress/overwrite UI. <paramref name="DestPath"/>
    /// may be a subdirectory row within the column rather than the column's own path (a
    /// row-granular drop), so the outcome is reported back against <paramref name="ColumnLocation"/> -
    /// the location shown by <paramref name="ColumnIndex"/> at the time the drop was issued - as
    /// <see cref="Msg.ShellOpCompleted"/> or <see cref="Msg.ShellOpFailed"/>, matching the
    /// staleness contract every other column-keyed result follows (ignored unless the column still
    /// shows that location) and triggering a re-read of the column, not the row that was dropped onto.
    /// </summary>
    public sealed record ShellCopyOrMove(
        int ColumnIndex, Location ColumnLocation, string DestPath, ImmutableArray<string> Paths, bool IsMove) : Effect;

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
    /// Wakes the job engine worker blocked waiting on <paramref name="JobId"/>'s conflict prompt
    /// (see <see cref="Msg.JobConflictsFound"/>) with the user's <paramref name="Decision"/>.
    /// Handled synchronously by the engine, the same way as <see cref="CancelJob"/> - it never
    /// itself produces a Msg; the job's own progress/completion Msgs resume once the worker wakes.
    /// </summary>
    public sealed record ResolveJobConflict(int JobId, ConflictDecision Decision) : Effect;

    /// <summary>
    /// Loads a preview of whatever <paramref name="Target"/> says <paramref name="Path"/> is. For a
    /// file: image bytes (whole file, capped),
    /// decoded text (head only), or a <see cref="FileTypeDetector"/> label for anything else.
    /// Reports back as <see cref="Msg.PreviewLoaded"/> or <see cref="Msg.PreviewFailed"/>, both
    /// carrying <paramref name="Generation"/> unchanged so a superseded request's result can be
    /// told apart from the current one and discarded.
    /// </summary>
    public sealed record LoadPreview(
        int Generation, string Path, PreviewTarget Target = PreviewTarget.File) : Effect;

    /// <summary>
    /// Abandons the in-flight <see cref="LoadPreview"/> for <paramref name="Generation"/>: the
    /// cursor has moved off that file, so its result is already destined to be discarded by the
    /// generation check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Discarding the result was never the expensive part. A preview can be arbitrarily slow -
    /// opening a handle on a cloud placeholder hydrates it, and a video thumbnail shells out to
    /// ffmpeg for up to twenty seconds - and without this the work ran to completion regardless,
    /// holding a worker the whole time. Fast cursor movement through a directory of videos could
    /// therefore queue up minutes of work whose results were all thrown away.
    /// </para>
    /// <para>
    /// Like <see cref="CancelJob"/>, this produces no <see cref="Msg"/> of its own: the abandoned
    /// load simply stops, and the request that superseded it reports normally.
    /// </para>
    /// </remarks>
    public sealed record CancelPreview(int Generation) : Effect;

    /// <summary>
    /// Adds <paramref name="Path"/> to the drive pane's pinned places, or removes it when
    /// <paramref name="Pin"/> is <c>false</c>. Persisted, so it survives a restart.
    /// </summary>
    /// <remarks>
    /// Reports back as <see cref="Msg.PlacesChanged"/> whether or not anything actually changed -
    /// pinning something already pinned is a no-op the user should still see settle.
    /// </remarks>
    public sealed record SetPinned(string Path, bool Pin) : Effect;

    /// <summary>
    /// Remembers which sections of the drive pane are collapsed, so they come back that way.
    /// </summary>
    /// <remarks>
    /// Carries the whole set rather than the one section that just changed: the set is what gets
    /// written, and sending it whole means a lost or reordered effect cannot leave the stored value
    /// disagreeing with what is on screen. Reports nothing back - the collapse has already been
    /// applied to the state that emitted this, so there is nothing to wait for.
    /// </remarks>
    public sealed record SetCollapsedGroups(ImmutableArray<string> Groups) : Effect;
}
