namespace Zurari.Core;

/// <summary>
/// What the next input means. Most inputs act at once; a mode changes how the ones after it are read
/// until it is left again. Which physical keys open and drive a mode is the App's business - Core
/// only knows that the mode is open.
/// </summary>
public enum InputMode
{
    /// <summary>Inputs act directly.</summary>
    Normal,

    /// <summary>
    /// Choosing how listings are ordered. One decision long: choosing a field, choosing one
    /// descending or toggling folders first closes it again, as does any key it does not own.
    /// </summary>
    Sort,
}
