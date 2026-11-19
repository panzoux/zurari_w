using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.App;

/// <summary>
/// The parts of the split window that can be decided without a window: what to dispatch at startup
/// to bring back the last session, and what share of the height the splitter stands at. Static and
/// pure, so tests exercise them directly instead of through a <c>MainWindow</c>.
/// </summary>
public static class SplitLayout
{
    /// <summary>The smallest a ratio may be: the top pane keeps at least this share of the height.</summary>
    public const double MinRatio = 0.15;

    /// <summary>The largest a ratio may be.</summary>
    public const double MaxRatio = 0.85;

    /// <summary>Where the splitter starts when nothing has been stored: the top pane's share of the height.</summary>
    public const double DefaultRatio = 0.5;

    /// <summary>
    /// The Msgs that bring back the last session, in dispatch order: the top pane's stored view,
    /// then a refresh (which reads the top pane's column), then - only when the split was open -
    /// <see cref="Msg.ToggleSplit"/> carrying the bottom pane's stored view (or <c>null</c> when only
    /// one was stored, so the new pane copies the top one's). Opening the split after the refresh
    /// reads only the new pane's own column, so nothing is read twice.
    /// </summary>
    public static IReadOnlyList<Msg> StartupMsgs(UserSettings settings, PaneId top)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var views = settings.EffectivePaneViews();
        var msgs = new List<Msg>
        {
            new Msg.ViewRestored(top, WorkerRuntime.ReadViewOptions(views[0])),
            new Msg.Refresh(),
        };

        if (settings.SplitOpen == true)
        {
            msgs.Add(new Msg.ToggleSplit(views.Count > 1 ? WorkerRuntime.ReadViewOptions(views[1]) : null));
        }

        return msgs;
    }

    /// <summary>A stored or measured ratio brought into <see cref="MinRatio"/>..<see cref="MaxRatio"/>; a non-finite one becomes <see cref="DefaultRatio"/>.</summary>
    public static double ClampRatio(double ratio) =>
        double.IsFinite(ratio) ? Math.Clamp(ratio, MinRatio, MaxRatio) : DefaultRatio;

    /// <summary>
    /// The top pane's share of the height from the two rows' laid-out heights, or <c>null</c> when
    /// there is no usable layout (the split is closed - no bottom row - or the window is too small
    /// for both rows to keep <paramref name="minPaneHeight"/>). Measured from heights because after
    /// a drag <c>GridSplitter</c> leaves pixel-sized star values, not normalised ones. Clamped so
    /// both rows' minimum heights fit.
    /// </summary>
    public static double? MeasuredRatio(double topHeight, double bottomHeight, double minPaneHeight)
    {
        var total = topHeight + bottomHeight;
        if (bottomHeight <= 0 || total <= 2 * minPaneHeight)
        {
            return null;
        }

        var floor = Math.Max(MinRatio, minPaneHeight / total);
        var ceiling = Math.Min(MaxRatio, 1 - (minPaneHeight / total));
        return Math.Clamp(topHeight / total, floor, ceiling);
    }
}
