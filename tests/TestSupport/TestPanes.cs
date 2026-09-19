using Zurari.Core;

namespace Zurari.TestSupport;

/// <summary>Addresses in the single pane every pre-split test works with.</summary>
public static class TestPanes
{
    /// <summary>The id <see cref="AppState.Initial"/> and <see cref="AppState.SinglePane"/> give their pane.</summary>
    public static readonly PaneId First = new(0);

    /// <summary>Column <paramref name="index"/> of <see cref="First"/>.</summary>
    public static ColumnRef Col(int index) => new(First, index);
}
