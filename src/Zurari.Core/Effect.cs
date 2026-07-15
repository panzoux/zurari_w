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
}
