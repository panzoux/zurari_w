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
    /// Choosing how listings are ordered. Stays open while fields are chosen, so choosing the same
    /// field again flips its direction without reopening the mode.
    /// </summary>
    Sort,
}
