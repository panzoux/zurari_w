using Zurari.Core;

namespace Zurari.TestSupport;

/// <summary>Addresses in the single pane every pre-split test works with.</summary>
public static class TestPanes
{
    /// <summary>The id <see cref="AppState.Initial"/> and <see cref="AppState.SinglePane"/> give their pane.</summary>
    public static readonly PaneId First = new(0);

    /// <summary>Column <paramref name="index"/> of <see cref="First"/>.</summary>
    public static ColumnRef Col(int index) => new(First, index);

    /// <summary>
    /// Two panes, top (id 0) and bottom (id 1), each showing one column. Tests that use this
    /// address pane 1 - the unfocused one - to prove a Msg reaches the pane it names rather than
    /// always landing on the focused pane. <paramref name="focused"/> picks which pane is focused.
    /// </summary>
    public static AppState TwoPanes(Column top, Column bottom, int focused = 0) =>
        AppState.Initial with
        {
            Panes = [new Pane(new PaneId(0), [top], 0), new Pane(new PaneId(1), [bottom], 0)],
            FocusedPane = focused,
            NextPaneId = 2,
        };
}
