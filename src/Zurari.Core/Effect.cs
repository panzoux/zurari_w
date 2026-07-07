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
}
