using System.Windows;
using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// The two-row layout of <see cref="MainWindow"/>: the splitter ratio, opening and closing the
/// second row, and forgetting what belonged to a pane that went away. Mirrors
/// <c>Transition.Split.cs</c> in Core.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Whether <see cref="ApplyLayout"/> currently shows the second row.</summary>
    private bool splitShown;

    /// <summary>The smallest a pane may be dragged to, so both always have room for their columns.</summary>
    private const double PaneMinHeight = 120;

    /// <summary>
    /// The top pane's share of the height the split opens with: restored from settings, then kept
    /// current by every drag.
    /// </summary>
    private double splitRatio = SplitLayout.DefaultRatio;

    /// <summary>
    /// The top pane's share as the rows are laid out now, or <c>null</c> when there is no usable
    /// layout (see <see cref="SplitLayout.MeasuredRatio"/>). Computed from <c>ActualHeight</c>:
    /// after a drag <c>GridSplitter</c> leaves pixel-sized star values (<c>312*</c> and
    /// <c>188*</c>), not normalised ones.
    /// </summary>
    private double? MeasuredSplitRatio() =>
        SplitLayout.MeasuredRatio(TopRow.ActualHeight, BottomRow.ActualHeight, PaneMinHeight);

    private void SaveSplitRatio()
    {
        if (MeasuredSplitRatio() is not { } ratio)
        {
            return;
        }

        splitRatio = ratio;
        settingsStore.Update(s => s with { SplitRatio = ratio });
    }

    /// <summary>
    /// Opens or closes the second row when the state's pane count says so. Acts only on a change,
    /// not on every render: sizing the rows each time would undo the user's splitter drag.
    /// </summary>
    private void ApplyLayout(AppState state)
    {
        var split = state.Panes.Length == 2;
        if (split == splitShown)
        {
            return;
        }

        splitShown = split;
        if (split)
        {
            TopRow.Height = new GridLength(splitRatio, GridUnitType.Star);
            SplitterRow.Height = new GridLength(6);
            BottomRow.MinHeight = PaneMinHeight;
            BottomRow.Height = new GridLength(1 - splitRatio, GridUnitType.Star);
            PaneSplitter.Visibility = Visibility.Visible;
            BottomPane.Visibility = Visibility.Visible;
        }
        else
        {
            // The rows are about to be zeroed, and with them the only record of where the user left
            // the splitter this session.
            if (MeasuredSplitRatio() is { } leftAt)
            {
                splitRatio = leftAt;
            }

            PaneSplitter.Visibility = Visibility.Collapsed;
            BottomPane.Visibility = Visibility.Collapsed;
            BottomRow.MinHeight = 0;
            BottomRow.Height = new GridLength(0);
            SplitterRow.Height = new GridLength(0);
            TopRow.Height = new GridLength(1, GridUnitType.Star);
        }
    }

    /// <summary>
    /// Forgets the per-pane gates and caches of panes that no longer exist, and the last-rendered
    /// record of rows that no longer show a pane, so closing the split does not pin their row arrays
    /// or leave a stale preview request to be submitted - and a pane that opens in the row later is
    /// always drawn. Returns whether any row had been rendered and is now empty.
    /// </summary>
    private bool DropGonePanes(AppState state)
    {
        DropUnknownKeys(state, previewGates, gate => gate.Stop());
        DropUnknownKeys(state, speculativeReadGates, gate => gate.Stop());
        DropUnknownKeys(state, projectionCaches, null);

        var rowEmptied = false;
        for (var row = state.Panes.Length; row < lastRendered.Length; row++)
        {
            if (lastRendered[row] is not null)
            {
                lastRendered[row] = null;
                rowEmptied = true;

                // The row is hidden, but its columns and decoded image are still alive.
                rows[row].ColumnBrowser.Columns = [];
                rows[row].ClearPreview();
            }
        }

        return rowEmptied;
    }

    private static void DropUnknownKeys<T>(AppState state, Dictionary<PaneId, T> map, Action<T>? release)
    {
        var gone = new List<PaneId>();
        foreach (var id in map.Keys)
        {
            if (!HasPane(state, id))
            {
                gone.Add(id);
            }
        }

        foreach (var id in gone)
        {
            release?.Invoke(map[id]);
            map.Remove(id);
        }
    }

    private static bool HasPane(AppState state, PaneId id)
    {
        foreach (var pane in state.Panes)
        {
            if (pane.Id == id)
            {
                return true;
            }
        }

        return false;
    }
}
