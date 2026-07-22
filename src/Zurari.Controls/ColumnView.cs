using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Zurari.Controls;

/// <summary>
/// Raw rubber-band release data for one column, before <see cref="ColumnBrowser"/> adds the
/// column index (which this view does not know about itself).
/// </summary>
internal readonly record struct MarkRangeRequestInfo(int FromIndex, int ToIndex, bool Additive);

/// <summary>
/// Raw pointer-press data for one entry row, before <see cref="ColumnBrowser"/> adds
/// the column index (which this view does not know about itself).
/// </summary>
internal readonly record struct EntryPointerPressInfo(
    int EntryIndex, ModifierKeys Modifiers, MouseButton Button, Point ScreenPosition);

/// <summary>
/// Raw file-drop data for a drop onto this view, before <see cref="ColumnBrowser"/> adds the
/// column index (which this view does not know about itself). <paramref name="TargetEntryIndex"/>
/// is the row the pointer was over at drop time (-1 for the column background); the host decides
/// what that means (row-granular transfer target vs. the column's own path).
/// </summary>
internal readonly record struct FileDropInfo(
    IReadOnlyList<string> Paths, int TargetEntryIndex, bool ShiftHeld, bool CtrlHeld);

/// <summary>
/// One column of a <see cref="ColumnBrowser"/>: title header, virtualized entry
/// list and a resize thumb on the right edge. Created and owned by the browser;
/// as dumb as its owner — it renders a <see cref="ColumnVm"/> and forwards input.
/// </summary>
[TemplatePart(Name = ListPartName, Type = typeof(ListBox))]
[TemplatePart(Name = ThumbPartName, Type = typeof(Thumb))]
[TemplatePart(Name = RubberBandPartName, Type = typeof(Rectangle))]
public sealed class ColumnView : Control
{
    internal const string ListPartName = "PART_List";
    internal const string ThumbPartName = "PART_ResizeThumb";
    internal const string RubberBandPartName = "PART_RubberBand";

    /// <summary>Identifies the <see cref="Column"/> dependency property.</summary>
    public static readonly DependencyProperty ColumnProperty = DependencyProperty.Register(
        nameof(Column),
        typeof(ColumnVm),
        typeof(ColumnView),
        new FrameworkPropertyMetadata(null, static (d, _) => ((ColumnView)d).SyncFromColumn()));

    /// <summary>
    /// Identifies the attached <c>IsColumnFocused</c> property. Set on the
    /// <see cref="ColumnView"/> itself (never on individual containers) and
    /// inherited down through the template into <see cref="List"/> and its
    /// item containers, so <c>ItemContainerStyle</c> triggers can render a
    /// stronger cursor-row highlight for the focused column and a weaker one
    /// for the rest — independent of WPF's own keyboard-focus-driven
    /// selection colors, which is not what "focused column" means here.
    /// Public so it can be read back (e.g. from tests) without exposing any
    /// other implementation detail of <see cref="ColumnView"/>.
    /// </summary>
    public static readonly DependencyProperty IsColumnFocusedProperty = DependencyProperty.RegisterAttached(
        "IsColumnFocused",
        typeof(bool),
        typeof(ColumnView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>
    /// Identifies the attached <c>IsDropTarget</c> property, set on the <see cref="ColumnView"/>
    /// itself while an Explorer (or other app) drag is hovering over it with file data, so
    /// <c>Generic.xaml</c> can render a border highlight. Mirrors <see cref="IsColumnFocusedProperty"/>.
    /// </summary>
    public static readonly DependencyProperty IsDropTargetProperty = DependencyProperty.RegisterAttached(
        "IsDropTarget",
        typeof(bool),
        typeof(ColumnView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>
    /// Identifies the attached <c>IsRubberBandHover</c> property: set directly on individual
    /// realized <see cref="ListBoxItem"/> containers (never inherited - unlike
    /// <see cref="IsColumnFocusedProperty"/>/<see cref="IsDropTargetProperty"/>) while a rubber-band
    /// drag's live range covers that row, so <c>Generic.xaml</c> can render a lighter preview
    /// highlight before the drag is released and marks are actually applied.
    /// </summary>
    public static readonly DependencyProperty IsRubberBandHoverProperty = DependencyProperty.RegisterAttached(
        "IsRubberBandHover",
        typeof(bool),
        typeof(ColumnView),
        new FrameworkPropertyMetadata(false));

    /// <summary>
    /// Identifies the attached <c>IsDropTargetRow</c> property: set directly on the single realized
    /// <see cref="ListBoxItem"/> container the pointer is currently hovering during an Explorer (or
    /// other app) file drag, but only while that row is a Directory or Drive (a file row is not a
    /// valid drop target and gets no row highlight - only the column-level <see cref="IsDropTargetProperty"/>
    /// border still applies). Mirrors <see cref="IsRubberBandHoverProperty"/>: set/cleared directly
    /// on containers, never inherited, cleared from every other container on each update.
    /// </summary>
    public static readonly DependencyProperty IsDropTargetRowProperty = DependencyProperty.RegisterAttached(
        "IsDropTargetRow",
        typeof(bool),
        typeof(ColumnView),
        new FrameworkPropertyMetadata(false));

    private bool suppressSelectionChanged;

    private readonly DragGestureTracker dragTracker = new();
    private readonly RubberBandTracker rubberBandTracker = new();
    private readonly DispatcherTimer autoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    /// <summary>
    /// Watches an open left-button gesture for a release the OS never delivered (some touchpad
    /// drivers occasionally drop the WM_LBUTTONUP of a physical-button click). Started on every
    /// left press, stopped by <see cref="CompleteLeftRelease"/>; each tick polls the live device
    /// state and synthesizes the release path when the button is up but no event arrived.
    /// </summary>
    private readonly DispatcherTimer releaseWatchdogTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    private readonly Stopwatch rowPressStopwatch = new();
    private Rectangle? rubberBandVisual;
    private ScrollViewer? listScrollViewer;
    private Point lastMovePositionInList;
    private Point rowPressPositionInList;

    /// <summary>
    /// True when the in-progress rubber-band gesture (if any) started on an entry row (armed
    /// retroactively from <see cref="OnListPreviewMouseMove"/>'s Finder-timing/Ctrl decision) rather
    /// than on empty space (armed immediately from <see cref="OnListPreviewMouseDown"/>). Captured
    /// by <see cref="PressRubberBand"/> and consumed at release by <see cref="OnListPreviewMouseUp"/>
    /// to tell an actual row-anchored rubber-band drag apart from a physical-button press that only
    /// wiggled the pointer past the system drag threshold without ever leaving the pressed row - see
    /// the wiggle-click fix there.
    /// </summary>
    private bool rubberBandStartedOnRow;

    /// <summary>
    /// Pixel offset between the exact press point that started the current rubber-band gesture and
    /// the anchor row's top edge at press time (or the viewport edge, for an anchor with no
    /// realized row). Captured once by <see cref="PressRubberBand"/> and reapplied by
    /// <see cref="ResolveAnchorPixelY"/> on every subsequent move, so the rectangle's fixed corner
    /// stays pinned to the actual press point - not the anchor row's top - even as the row's
    /// on-screen position shifts under auto-scroll.
    /// </summary>
    private double anchorPixelOffset;

    /// <summary>
    /// The exact press point (list-relative) that started the current rubber-band gesture - the
    /// same value passed to <see cref="PressRubberBand"/>. Fixed reference point for
    /// <see cref="rubberBandMaxDisplacement"/>.
    /// </summary>
    private Point rubberBandPressPositionInList;

    /// <summary>
    /// Max distance (DIPs) the pointer has travelled from <see cref="rubberBandPressPositionInList"/>
    /// at any point during the current rubber-band gesture, updated on every
    /// <see cref="UpdateDuringMove"/> call. Reset to 0 by <see cref="PressRubberBand"/>. Feeds
    /// <see cref="RubberBandTracker.ShouldConvertToClick"/>'s displacement-tolerance branch of the
    /// wiggle-click fix (Item C) - the row-escape rule alone misfires for a press near a row
    /// boundary, where even a few-px wiggle can cross into the neighbour row.
    /// </summary>
    private double rubberBandMaxDisplacement;

    /// <summary>
    /// List-relative press point recorded by <see cref="OnListPreviewMouseDown"/> (either branch),
    /// used only by the <see cref="ColumnBrowser.InputTraceEnabled"/> diagnostic tracing to compute
    /// the delta logged by the first <see cref="OnListPreviewMouseMove"/> of each gesture.
    /// </summary>
    private Point inputTracePressPositionInList;

    /// <summary>
    /// True once the first <see cref="OnListPreviewMouseMove"/> of the current gesture has logged
    /// its position delta (see <see cref="inputTracePressPositionInList"/>); reset by
    /// <see cref="OnListPreviewMouseDown"/>. Diagnostic tracing only.
    /// </summary>
    private bool inputTraceFirstMoveLogged;

    /// <summary>
    /// The <see cref="ColumnVm.CursorIndex"/> last passed to <see cref="List"/>.ScrollIntoView by
    /// <see cref="SyncFromColumn"/>, or <c>null</c> before the first sync. Defense-in-depth for
    /// Phase 5 bug B3 (scroll position snapping back during a running job): even with the App
    /// composition root now skipping its <c>Columns</c> reassignment when the underlying state's
    /// columns are unchanged, a snapshot swap that DOES carry real changes (e.g. a sibling column's
    /// directory reloading) still calls <see cref="SyncFromColumn"/> for every realized
    /// <see cref="ColumnView"/>, this one included - unconditionally re-scrolling to the cursor row
    /// would yank back any scrolling the user did in between, even though this column's cursor
    /// never moved. <see cref="List"/>.SelectedIndex is still resynced unconditionally every call
    /// (cheap, and it must never drift from the VM).
    /// </summary>
    private int? lastSyncedCursorIndex;

    static ColumnView()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ColumnView), new FrameworkPropertyMetadata(typeof(ColumnView)));
    }

    internal ColumnView()
    {
        AllowDrop = true;
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
        autoScrollTimer.Tick += OnAutoScrollTick;
        releaseWatchdogTimer.Tick += OnReleaseWatchdogTick;
    }

    /// <summary>
    /// Synthesizes the release path when the physical button is up but no
    /// <see cref="UIElement.PreviewMouseUp"/> ever arrived — see <see cref="releaseWatchdogTimer"/>.
    /// </summary>
    private void OnReleaseWatchdogTick(object? sender, EventArgs e)
    {
        if (Mouse.PrimaryDevice.LeftButton != MouseButtonState.Released)
        {
            return;
        }

        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine($"[input] col={Column?.Title} watchdog: button released without an up event - synthesizing release");
        }

        CompleteLeftRelease();
    }

    /// <summary>Snapshot of the column to display.</summary>
    public ColumnVm? Column
    {
        get => (ColumnVm?)GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    /// <summary>Raised while the resize thumb is dragged, with the horizontal delta in DIPs.</summary>
    internal event EventHandler<double>? ResizeDelta;

    /// <summary>Raised when a resize drag completes.</summary>
    internal event EventHandler? ResizeCompleted;

    /// <summary>
    /// Raised on a mouse-button press over an entry row. <see cref="ColumnBrowser"/>
    /// translates this into the public <see cref="EntryPointerPressedEventArgs"/>
    /// (adding the column index, which this view does not know about itself).
    /// </summary>
    internal event EventHandler<EntryPointerPressInfo>? EntryPointerPressed;

    /// <summary>Raised on a double-click over an entry row, carrying the entry index.</summary>
    internal event EventHandler<int>? EntryActivationRequested;

    /// <summary>
    /// Raised once per left-button press-then-release on an entry row that never crossed the
    /// system drag threshold (a true click, as opposed to the press that also starts a drag - see
    /// <see cref="EntryDragRequested"/>). Carries the entry index that was pressed.
    /// </summary>
    internal event EventHandler<int>? EntryClicked;

    /// <summary>
    /// Raised once per left-button drag gesture that starts on an entry row and crosses the system
    /// drag threshold (<see cref="SystemParameters.MinimumHorizontalDragDistance"/> /
    /// <see cref="SystemParameters.MinimumVerticalDragDistance"/>). Carries the entry index; reset
    /// on button-up so the next press starts a fresh gesture.
    /// </summary>
    internal event EventHandler<int>? EntryDragRequested;

    /// <summary>Raised when files are dropped from Explorer (or another app) onto this column.</summary>
    internal event EventHandler<FileDropInfo>? FileDropRequested;

    /// <summary>
    /// Raised when a rubber-band (rectangle) drag that started on empty space and covered at
    /// least one entry row is released. Never raised for a rubber-band that covered no rows
    /// (that release is silently dropped, not a click).
    /// </summary>
    internal event EventHandler<MarkRangeRequestInfo>? MarkRangeRequested;

    /// <summary>
    /// Raised at the moment a rubber-band gesture ACTIVATES (the pointer first crosses the drag
    /// threshold and the rectangle first appears) - for an empty-space start that is somewhere
    /// during <see cref="OnListPreviewMouseMove"/>/<see cref="UpdateDuringMove"/>; for a row start it
    /// is the same Finder-timing/Ctrl decision moment that arms <see cref="PressRubberBand"/>. Carries
    /// whether Ctrl was held at that instant (additive) so <see cref="ColumnBrowser"/>'s host can
    /// clear the column's existing marks the instant a REPLACE-mode band starts, instead of only at
    /// release - see the Item A fix this exists for. Never raised for a gesture that never crosses
    /// the threshold at all (a plain click).
    /// </summary>
    internal event EventHandler<bool>? RubberBandStarted;

    /// <summary>
    /// Test-only hook: raised immediately before <see cref="SyncFromColumn"/> actually calls
    /// <see cref="List"/>.ScrollIntoView - i.e. exactly when <see cref="lastSyncedCursorIndex"/>
    /// caused it to proceed rather than short-circuit. Lets a test assert the scroll call did or
    /// did not happen without depending on WPF's virtualization/binding timing to observe an actual
    /// scroll offset change (Phase 5 bug B3 regression coverage - see
    /// <c>CursorVisualTests.Snapshot_swap_with_unchanged_cursor_does_not_call_ScrollIntoView</c>).
    /// </summary>
    internal event EventHandler? ScrollIntoViewInvoked;

    internal ListBox? List { get; private set; }

    /// <summary>Gets the <see cref="IsColumnFocusedProperty"/> attached value.</summary>
    public static bool GetIsColumnFocused(DependencyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return (bool)obj.GetValue(IsColumnFocusedProperty);
    }

    /// <summary>Sets the <see cref="IsColumnFocusedProperty"/> attached value.</summary>
    public static void SetIsColumnFocused(DependencyObject obj, bool value)
    {
        ArgumentNullException.ThrowIfNull(obj);
        obj.SetValue(IsColumnFocusedProperty, value);
    }

    /// <summary>Gets the <see cref="IsDropTargetProperty"/> attached value.</summary>
    public static bool GetIsDropTarget(DependencyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return (bool)obj.GetValue(IsDropTargetProperty);
    }

    /// <summary>Sets the <see cref="IsDropTargetProperty"/> attached value.</summary>
    public static void SetIsDropTarget(DependencyObject obj, bool value)
    {
        ArgumentNullException.ThrowIfNull(obj);
        obj.SetValue(IsDropTargetProperty, value);
    }

    /// <summary>Gets the <see cref="IsRubberBandHoverProperty"/> attached value.</summary>
    public static bool GetIsRubberBandHover(DependencyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return (bool)obj.GetValue(IsRubberBandHoverProperty);
    }

    /// <summary>Sets the <see cref="IsRubberBandHoverProperty"/> attached value.</summary>
    public static void SetIsRubberBandHover(DependencyObject obj, bool value)
    {
        ArgumentNullException.ThrowIfNull(obj);
        obj.SetValue(IsRubberBandHoverProperty, value);
    }

    /// <summary>Gets the <see cref="IsDropTargetRowProperty"/> attached value.</summary>
    public static bool GetIsDropTargetRow(DependencyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return (bool)obj.GetValue(IsDropTargetRowProperty);
    }

    /// <summary>Sets the <see cref="IsDropTargetRowProperty"/> attached value.</summary>
    public static void SetIsDropTargetRow(DependencyObject obj, bool value)
    {
        ArgumentNullException.ThrowIfNull(obj);
        obj.SetValue(IsDropTargetRowProperty, value);
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // The old List (if any) is about to be discarded along with its containers: an
        // in-flight auto-scroll or cached ScrollViewer reference must not survive into the
        // new template.
        StopAutoScroll();
        listScrollViewer = null;

        if (List is not null)
        {
            List.SelectionChanged -= OnListSelectionChanged;
            List.PreviewMouseDown -= OnListPreviewMouseDown;
            List.PreviewMouseMove -= OnListPreviewMouseMove;
            List.PreviewMouseUp -= OnListPreviewMouseUp;
        }

        List = GetTemplateChild(ListPartName) as ListBox;
        if (List is not null)
        {
            List.SelectionChanged += OnListSelectionChanged;
            List.PreviewMouseDown += OnListPreviewMouseDown;
            List.PreviewMouseMove += OnListPreviewMouseMove;
            List.PreviewMouseUp += OnListPreviewMouseUp;
        }

        if (GetTemplateChild(ThumbPartName) is Thumb thumb)
        {
            thumb.DragDelta += (_, e) => ResizeDelta?.Invoke(this, e.HorizontalChange);
            thumb.DragCompleted += (_, _) => ResizeCompleted?.Invoke(this, EventArgs.Empty);
        }

        rubberBandVisual = GetTemplateChild(RubberBandPartName) as Rectangle;

        SyncFromColumn();
    }

    /// <summary>
    /// Applies the current <see cref="Column"/> snapshot to the list: the
    /// cursor row (<see cref="ColumnVm.CursorIndex"/>) becomes the selection
    /// (and is scrolled into view), and the focused-column marker is updated.
    /// Called whenever <see cref="Column"/> changes and once the template
    /// has been applied.
    /// </summary>
    private void SyncFromColumn()
    {
        var column = Column;
        SetIsColumnFocused(this, column?.IsFocused ?? false);

        if (List is null)
        {
            return;
        }

        var cursorIndex = column?.CursorIndex ?? -1;
        suppressSelectionChanged = true;
        try
        {
            List.SelectedIndex = cursorIndex;
        }
        finally
        {
            suppressSelectionChanged = false;
        }

        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine(
                $"[input] col={column?.Title} SyncFromColumn cursorIndex={cursorIndex} "
                + $"lastSyncedCursorIndex={lastSyncedCursorIndex} entries={column?.Entries.Count}");
        }

        if (lastSyncedCursorIndex == cursorIndex)
        {
            return;
        }

        lastSyncedCursorIndex = cursorIndex;

        if (column is not null && cursorIndex >= 0 && cursorIndex < column.Entries.Count)
        {
            if (ColumnBrowser.InputTraceEnabled)
            {
                Trace.WriteLine($"[input] col={column.Title} ScrollIntoView cursorIndex={cursorIndex}");
            }

            ScrollIntoViewInvoked?.Invoke(this, EventArgs.Empty);
            List.ScrollIntoView(column.Entries[cursorIndex]);
        }
    }

    /// <summary>
    /// The list's selection exists purely to render the cursor row; it must
    /// never become an independent source of truth. Any selection change not
    /// caused by <see cref="SyncFromColumn"/> (e.g. a stray click, before
    /// input wiring lands in a later task) is reverted back to the VM cursor.
    /// </summary>
    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelectionChanged || List is null)
        {
            return;
        }

        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine(
                $"[input] col={Column?.Title} selectionReverted from={List.SelectedIndex} "
                + $"to={Column?.CursorIndex ?? -1}");
        }

        suppressSelectionChanged = true;
        try
        {
            List.SelectedIndex = Column?.CursorIndex ?? -1;
        }
        finally
        {
            suppressSelectionChanged = false;
        }
    }

    /// <summary>
    /// Translates a raw mouse-button press on an entry row into
    /// <see cref="EntryPointerPressed"/> (and <see cref="EntryActivationRequested"/> on a
    /// double-click). Selection here is purely visual and reverted by
    /// <see cref="OnListSelectionChanged"/> — this view never decides where the cursor goes.
    /// Also records the press point/time for the pressed row (<see cref="rowPressPositionInList"/>,
    /// <see cref="rowPressStopwatch"/>) so <see cref="OnListPreviewMouseMove"/> can decide, once
    /// movement first crosses the system drag threshold, whether the gesture is a Finder-style
    /// rubber-band or a file drag - see <see cref="RubberBandTracker.ShouldStartRubberBand"/>.
    /// </summary>
    private void OnListPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var entryIndex = FindEntryIndex(e.OriginalSource as DependencyObject);
        inputTraceFirstMoveLogged = false;
        inputTracePressPositionInList = List is not null ? e.GetPosition(List) : default;

        if (entryIndex is null)
        {
            if (IsOnScrollBar(e.OriginalSource as DependencyObject))
            {
                if (ColumnBrowser.InputTraceEnabled)
                {
                    Trace.WriteLine(
                        $"[input] col={Column?.Title} down entry=null button={e.ChangedButton} clicks={e.ClickCount} "
                        + $"pos={inputTracePressPositionInList} branch=scrollbar");
                }

                // Left entirely to the ListBox's own ScrollViewer/ScrollBar handling - starting a
                // rubber-band drag and capturing the mouse (like the empty-space branch below does)
                // would prevent the user from dragging the scrollbar thumb to scroll.
                return;
            }

            if (ColumnBrowser.InputTraceEnabled)
            {
                Trace.WriteLine(
                    $"[input] col={Column?.Title} down entry=null button={e.ChangedButton} clicks={e.ClickCount} "
                    + $"pos={inputTracePressPositionInList} branch=empty-space");
            }

            // Empty space (below the rows, or the column background): start a rubber-band
            // instead of the entry-row drag/click handling below. No EntryPointerPressed,
            // no EntryClicked - this is not a row interaction. Always eligible, timing irrelevant -
            // there is nothing here to drag.
            if (e.ChangedButton == MouseButton.Left && List is not null)
            {
                var positionInList = e.GetPosition(List);
                PressRubberBand(positionInList, FindNearestRowIndex(positionInList), startedOnRow: false);
                List.CaptureMouse();
                releaseWatchdogTimer.Start();
            }

            return;
        }

        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine(
                $"[input] col={Column?.Title} down entry={entryIndex.Value} button={e.ChangedButton} "
                + $"clicks={e.ClickCount} pos={inputTracePressPositionInList} branch=row");
        }

        // Reset any stale rubber-band gesture (e.g. one whose Release never fired because
        // capture was lost) so it cannot bleed into this entry-row press.
        rubberBandTracker.Press(default, onEmptySpace: false);

        var screenPosition = PointToScreen(e.GetPosition(this));
        EntryPointerPressed?.Invoke(
            this, new EntryPointerPressInfo(entryIndex.Value, Keyboard.Modifiers, e.ChangedButton, screenPosition));

        if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
        {
            EntryActivationRequested?.Invoke(this, entryIndex.Value);
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            dragTracker.Press(e.GetPosition(this), entryIndex.Value);
            rowPressPositionInList = List is not null ? e.GetPosition(List) : default;
            rowPressStopwatch.Restart();
            releaseWatchdogTimer.Start();
        }
    }

    /// <summary>
    /// Tracks a left-button drag past the system threshold. The state machine in
    /// <see cref="DragGestureTracker"/> fires exactly once per gesture, at the moment the pointer
    /// first travels past the system drag threshold - that single firing is also the Finder-timing
    /// decision point. Net behavior (see also <see cref="OnEntryClicked"/> at
    /// <c>MainWindow.OnEntryClicked</c> for the click/toggle-mark half of this table):
    /// <list type="bullet">
    /// <item>plain quick drag (&lt; <see cref="RubberBandTracker.RubberBandVsDragThresholdMs"/>) =
    /// replace-selection rubber band</item>
    /// <item>Ctrl+drag = additive rubber band, timing irrelevant - Ctrl wins outright</item>
    /// <item>plain hold-then-drag = file drag (the marked SET, if the pressed row is marked)</item>
    /// <item>Ctrl+hold-then-drag = additive rubber band (Ctrl still wins)</item>
    /// </list>
    /// Ctrl held at the moment the threshold crosses always starts a rubber band - additivity itself
    /// is decided again at release, from Ctrl held at THAT moment (<see cref="OnListPreviewMouseUp"/>),
    /// which is what lets the user let go of Ctrl mid-drag without losing the existing marks or hold
    /// it down through release to add to them. Without Ctrl, <see cref="RubberBandTracker.ShouldStartRubberBand"/>
    /// applies the plain timing rule uniformly - including on a marked row (no more "marked row is
    /// always a drag" exception). Either way the gesture is armed retroactively via
    /// <see cref="PressRubberBand"/> - the drag tracker having already fired is what suppresses the
    /// click at button-up, exactly as a real drag would (see the wiggle-click fix in
    /// <see cref="OnListPreviewMouseUp"/> for the case where it turns out not to have moved anywhere).
    /// Empty-space presses never reach here - they are decided immediately at press time in
    /// <see cref="OnListPreviewMouseDown"/>.
    /// </summary>
    private void OnListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (ColumnBrowser.InputTraceEnabled && !inputTraceFirstMoveLogged && List is not null)
        {
            inputTraceFirstMoveLogged = true;
            var delta = e.GetPosition(List) - inputTracePressPositionInList;
            Trace.WriteLine($"[input] col={Column?.Title} firstmove delta={delta}");
        }

        // A move reporting the button as up while a left gesture is still open means the OS
        // never delivered the release event (some touchpad drivers drop the WM_LBUTTONUP of a
        // physical-button click): this move IS our first sight of the release. Complete the
        // gesture now, exactly as the up handler would - previously DragGestureTracker.Move's
        // stale-gesture cancellation silently discarded the press here, swallowing the click.
        // (The release watchdog stays as the fallback for when not even a move arrives.)
        if (releaseWatchdogTimer.IsEnabled && e.LeftButton == MouseButtonState.Released)
        {
            if (ColumnBrowser.InputTraceEnabled)
            {
                Trace.WriteLine($"[input] col={Column?.Title} move observed button released - synthesizing release");
            }

            CompleteLeftRelease();
            return;
        }

        if (dragTracker.Move(e.GetPosition(this), e.LeftButton == MouseButtonState.Pressed) is { } entryIndex)
        {
            var ctrlHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var startsRubberBand = ctrlHeld || RubberBandTracker.ShouldStartRubberBand(
                onEmptySpace: false, rowPressStopwatch.ElapsedMilliseconds);

            if (ColumnBrowser.InputTraceEnabled)
            {
                Trace.WriteLine(
                    $"[input] col={Column?.Title} movefire elapsedMs={rowPressStopwatch.ElapsedMilliseconds} "
                    + $"ctrl={ctrlHeld} branch={(startsRubberBand ? "rubberband" : "drag")}");
            }

            if (startsRubberBand)
            {
                PressRubberBand(rowPressPositionInList, entryIndex, startedOnRow: true);
                List?.CaptureMouse();
            }
            else
            {
                EntryDragRequested?.Invoke(this, entryIndex);
            }
        }

        if (List is null)
        {
            return;
        }

        UpdateDuringMove(e.GetPosition(List));
    }

    /// <summary>
    /// Advances the rubber-band gesture for the pointer at <paramref name="positionInList"/>
    /// (list-relative coordinates): updates the rectangle overlay, the live row-index range and
    /// its hover highlight, and starts/stops auto-scroll depending on whether the pointer is
    /// outside the viewport. Shared by <see cref="OnListPreviewMouseMove"/> and
    /// <see cref="OnAutoScrollTick"/> (which replays the last known pointer position after each
    /// scroll step, since a real pointer move is not what drove that tick).
    /// </summary>
    private void UpdateDuringMove(Point positionInList)
    {
        lastMovePositionInList = positionInList;

        var displacement = (positionInList - rubberBandPressPositionInList).Length;
        if (displacement > rubberBandMaxDisplacement)
        {
            rubberBandMaxDisplacement = displacement;
        }

        var wasActive = rubberBandTracker.IsActive;
        var rect = rubberBandTracker.Move(positionInList);
        if (rect is not null && !wasActive)
        {
            RubberBandStarted?.Invoke(this, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        }

        UpdateRubberBandVisual(rect, positionInList);

        if (rect is null)
        {
            UpdateRubberBandHoverHighlight(null);
            StopAutoScroll();
            return;
        }

        var currentIndex = FindNearestRowIndex(positionInList);
        var range = rubberBandTracker.UpdateCurrentIndex(currentIndex);
        UpdateRubberBandHoverHighlight(range);
        UpdateAutoScroll(positionInList);
    }

    /// <summary>
    /// Draws (or hides) the rubber-band feedback rectangle. The vertical span is NOT taken
    /// directly from <paramref name="rect"/> (which reflects the fixed press-time origin) but
    /// recomputed from the anchor row's current pixel position - see
    /// <see cref="ResolveAnchorPixelY"/> - so the rectangle stays correct as auto-scroll moves the
    /// anchor row off-screen; the horizontal span is unaffected (no horizontal auto-scroll).
    /// </summary>
    private void UpdateRubberBandVisual(Rect? rect, Point currentPositionInList)
    {
        if (rubberBandVisual is null)
        {
            return;
        }

        if (rect is not { } r || List is null)
        {
            rubberBandVisual.Visibility = Visibility.Collapsed;
            return;
        }

        var anchorY = ResolveAnchorPixelY(rubberBandTracker.AnchorIndex);
        var currentY = Math.Clamp(currentPositionInList.Y, 0, Math.Max(0, List.ActualHeight));
        var top = Math.Min(anchorY, currentY);
        var bottom = Math.Max(anchorY, currentY);

        Canvas.SetLeft(rubberBandVisual, r.X);
        Canvas.SetTop(rubberBandVisual, top);
        rubberBandVisual.Width = r.Width;
        rubberBandVisual.Height = bottom - top;
        rubberBandVisual.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Sets/clears <see cref="IsRubberBandHoverProperty"/> on every currently realized row
    /// container, matching <paramref name="range"/> (or clearing all of them when <c>null</c>).
    /// Iterating every realized container each call - not just the ones inside
    /// <paramref name="range"/> - is what clears stale flags left on recycled containers whose
    /// index moved outside the range since the last update.
    /// </summary>
    private void UpdateRubberBandHoverHighlight((int From, int To)? range)
    {
        if (List is null)
        {
            return;
        }

        for (var i = 0; i < List.Items.Count; i++)
        {
            if (List.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item)
            {
                continue;
            }

            SetIsRubberBandHover(item, range is { } r && i >= r.From && i <= r.To);
        }
    }

    /// <summary>
    /// Arms <see cref="rubberBandTracker"/> for a gesture anchored at <paramref name="positionInList"/>
    /// (list-relative) / <paramref name="anchorIndex"/>, and records <see cref="anchorPixelOffset"/>:
    /// the pixel distance between that exact press point and the anchor row's edge at this instant
    /// (computed via <see cref="ResolveAnchorPixelY"/> itself, with the offset momentarily zeroed,
    /// so this is the only place that needs to know how the edge is resolved). Every later call to
    /// <see cref="ResolveAnchorPixelY"/> reapplies this fixed offset, which is what keeps the
    /// rendered rectangle's corner pinned to the actual press point - not the row's top - per
    /// <see cref="UpdateRubberBandVisual"/>'s contract, including for a press on empty space below
    /// the last row (whose "row" for anchoring purposes is that last row itself). Also records
    /// <paramref name="startedOnRow"/> into <see cref="rubberBandStartedOnRow"/> - <c>true</c> when
    /// called from <see cref="OnListPreviewMouseMove"/>'s Finder-timing/Ctrl decision (an entry row
    /// was pressed first), <c>false</c> when called from <see cref="OnListPreviewMouseDown"/>'s
    /// empty-space handling - so <see cref="OnListPreviewMouseUp"/> can later tell a genuine
    /// row-anchored rubber-band apart from a click that merely wiggled past the drag threshold.
    /// </summary>
    private void PressRubberBand(Point positionInList, int anchorIndex, bool startedOnRow)
    {
        anchorPixelOffset = 0;
        anchorPixelOffset = positionInList.Y - ResolveAnchorPixelY(anchorIndex);
        rubberBandStartedOnRow = startedOnRow;
        rubberBandPressPositionInList = positionInList;
        rubberBandMaxDisplacement = 0;
        rubberBandTracker.Press(positionInList, onEmptySpace: true, anchorIndex);
    }

    /// <summary>
    /// Resolves the current pixel Y (in <see cref="List"/>-relative, i.e. viewport-relative,
    /// coordinates) of the rubber-band's fixed corner for <paramref name="anchorIndex"/>: while its
    /// row is still realized (in view), the exact press point is reconstructed as that row's
    /// current top edge plus <see cref="anchorPixelOffset"/> (the fixed distance from the row's top
    /// to the actual press point, captured once by <see cref="PressRubberBand"/>) - this is what
    /// keeps the corner glued to the press point rather than snapping to the row's top, and is what
    /// makes a press below the last row (which anchors on that row - see
    /// <see cref="FindNearestRowIndex"/>) draw correctly instead of excluding it. Once the row has
    /// scrolled out of the realized range, the exact point can no longer be reconstructed relative
    /// to it, so this clamps to whichever viewport edge it scrolled past instead (top edge if the
    /// anchor is above the lowest realized index, bottom edge otherwise - realized indices are
    /// contiguous under virtualization, so the first realized row tells us which direction).
    /// </summary>
    private double ResolveAnchorPixelY(int anchorIndex)
    {
        if (List is null)
        {
            return 0;
        }

        if (anchorIndex >= 0 && List.ItemContainerGenerator.ContainerFromIndex(anchorIndex) is ListBoxItem anchorItem)
        {
            var rowTop = anchorItem.TransformToAncestor(List).TransformBounds(new Rect(anchorItem.RenderSize)).Top;
            return Math.Clamp(rowTop + anchorPixelOffset, 0, Math.Max(0, List.ActualHeight));
        }

        for (var i = 0; i < List.Items.Count; i++)
        {
            if (List.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem)
            {
                return anchorIndex < i ? 0 : List.ActualHeight;
            }
        }

        return 0;
    }

    /// <summary>
    /// Starts (or stops) <see cref="autoScrollTimer"/> depending on whether
    /// <paramref name="positionInList"/> is currently outside <see cref="List"/>'s viewport.
    /// </summary>
    private void UpdateAutoScroll(Point positionInList)
    {
        if (List is null)
        {
            StopAutoScroll();
            return;
        }

        var step = ComputeAutoScrollStep(positionInList.Y, 0, List.ActualHeight);
        if (step == 0)
        {
            StopAutoScroll();
            return;
        }

        if (!autoScrollTimer.IsEnabled)
        {
            autoScrollTimer.Start();
        }
    }

    private void StopAutoScroll()
    {
        if (autoScrollTimer.IsEnabled)
        {
            autoScrollTimer.Stop();
        }
    }

    /// <summary>
    /// One auto-scroll tick: scrolls <see cref="List"/> by the step <see cref="ComputeAutoScrollStep"/>
    /// reports for the last known pointer position, then replays that position through
    /// <see cref="UpdateDuringMove"/> so the rectangle/live-range/hover highlight catch up with the
    /// new scroll offset. Stops itself once the gesture is no longer active or the pointer has
    /// moved back inside the viewport (the next real mouse move already does that too, but the
    /// timer must not spin forever if it somehow misses that).
    /// </summary>
    private void OnAutoScrollTick(object? sender, EventArgs e)
    {
        if (List is null || !rubberBandTracker.IsActive)
        {
            StopAutoScroll();
            return;
        }

        if (GetListScrollViewer() is not { } scrollViewer)
        {
            StopAutoScroll();
            return;
        }

        var step = ComputeAutoScrollStep(lastMovePositionInList.Y, 0, List.ActualHeight);
        if (step == 0)
        {
            StopAutoScroll();
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + step);
        UpdateDuringMove(lastMovePositionInList);
    }

    /// <summary>
    /// Pure auto-scroll step function: how many items (negative = up, positive = down) to scroll
    /// per tick for a pointer at <paramref name="pointerY"/> relative to the viewport
    /// [<paramref name="viewportTop"/>, <paramref name="viewportBottom"/>] - 0 while the pointer is
    /// inside the viewport, 1..3 (proportional to how far outside) otherwise. Kept as a standalone
    /// static method (rather than inline in <see cref="OnAutoScrollTick"/>) specifically so it is
    /// unit-testable without a live pointer/STA window - see the class remarks on why the timer
    /// loop itself is not.
    /// </summary>
    internal static int ComputeAutoScrollStep(double pointerY, double viewportTop, double viewportBottom)
    {
        if (pointerY < viewportTop)
        {
            return -StepsForOvershoot(viewportTop - pointerY);
        }

        if (pointerY > viewportBottom)
        {
            return StepsForOvershoot(pointerY - viewportBottom);
        }

        return 0;
    }

    private static int StepsForOvershoot(double overshoot)
    {
        if (overshoot > 120)
        {
            return 3;
        }

        return overshoot > 50 ? 2 : 1;
    }

    private ScrollViewer? GetListScrollViewer()
    {
        if (listScrollViewer is not null)
        {
            return listScrollViewer;
        }

        if (List is null)
        {
            return null;
        }

        listScrollViewer = FindVisualChild<ScrollViewer>(List);
        return listScrollViewer;
    }

    /// <summary>
    /// Ends the current gesture and raises exactly one of <see cref="EntryClicked"/> or
    /// <see cref="MarkRangeRequested"/> (or neither, for an in-progress file drag - already reported
    /// via <see cref="EntryDragRequested"/>). The decision itself is
    /// <see cref="RubberBandTracker.DecideRelease"/>, a pure function of state read up front here -
    /// before either tracker's <c>Release()</c> resets its gesture state, so which branch fires
    /// cannot depend on read order. See that method's remarks for the empty-space bug
    /// (<see cref="RubberBandTracker.DecideRelease"/>'s <c>dragFired</c>/<c>rowWasPressed</c>
    /// parameters) and the wiggle-click fix (<see cref="RubberBandTracker.ShouldConvertToClick"/>)
    /// it encodes.
    /// </summary>
    private void OnListPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            if (ColumnBrowser.InputTraceEnabled)
            {
                Trace.WriteLine($"[input] col={Column?.Title} up ignored button={e.ChangedButton}");
            }

            return;
        }

        CompleteLeftRelease();
    }

    /// <summary>
    /// Completes the current left-button gesture: decides click vs mark-range vs nothing and
    /// resets every tracker. Called from <see cref="OnListPreviewMouseUp"/> for a normally
    /// delivered release, and from <see cref="OnReleaseWatchdogTick"/> when the OS never delivered
    /// one (observed in the wild: some touchpad drivers occasionally drop the WM_LBUTTONUP for a
    /// physical-button click — the press arrives, the release never does, and the click was
    /// silently swallowed). Idempotent: with all trackers already released it decides None.
    /// </summary>
    private void CompleteLeftRelease()
    {
        releaseWatchdogTimer.Stop();

        var pressedEntryIndex = dragTracker.PressedEntryIndex;
        var dragFired = dragTracker.FiredThisGesture;
        var startedOnRow = rubberBandStartedOnRow;
        var escapedAnchor = rubberBandTracker.HasEscapedAnchor;
        var maxDisplacement = rubberBandMaxDisplacement;
        var capturedAtUp = List?.IsMouseCaptured ?? false;

        // LiveRange must be read before Release() - Release() resets the anchor/current
        // indices along with the rest of the gesture state.
        var finalRange = rubberBandTracker.LiveRange;
        var rubberBandRect = rubberBandTracker.Release();
        dragTracker.Release();
        StopAutoScroll();
        UpdateRubberBandHoverHighlight(null);

        if (List is not null && List.IsMouseCaptured)
        {
            List.ReleaseMouseCapture();
        }

        UpdateRubberBandVisual(null, default);

        var bandProducedRange = rubberBandRect is not null && finalRange is not null;
        var action = RubberBandTracker.DecideRelease(
            dragFired,
            rowWasPressed: pressedEntryIndex is not null,
            bandProducedRange,
            startedOnRow,
            escapedAnchor,
            maxDisplacement);

        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine(
                $"[input] col={Column?.Title} up dragFired={dragFired} "
                + $"rowWasPressed={pressedEntryIndex is not null} "
                + $"pressedEntryIndex={(pressedEntryIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")} "
                + $"bandProducedRange={bandProducedRange} startedOnRow={startedOnRow} "
                + $"escapedAnchor={escapedAnchor} maxDisplacement={maxDisplacement:F1} "
                + $"capturedAtUp={capturedAtUp} action={action}");
        }

        switch (action)
        {
            case RubberBandTracker.GestureReleaseAction.Click when pressedEntryIndex is { } clickedIndex:
                if (ColumnBrowser.InputTraceEnabled)
                {
                    Trace.WriteLine($"[input] col={Column?.Title} raised EntryClicked idx={clickedIndex}");
                }

                EntryClicked?.Invoke(this, clickedIndex);
                break;

            case RubberBandTracker.GestureReleaseAction.MarkRange when finalRange is { } range:
                if (ColumnBrowser.InputTraceEnabled)
                {
                    Trace.WriteLine(
                        $"[input] col={Column?.Title} raised MarkRangeRequested from={range.From} to={range.To}");
                }

                MarkRangeRequested?.Invoke(
                    this,
                    new MarkRangeRequestInfo(
                        range.From, range.To, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)));
                break;
        }
    }

    /// <summary>
    /// Finds the row index at <paramref name="positionInList"/> (list-relative coordinates) among
    /// currently REALIZED containers (<see cref="ItemContainerGenerator"/>) - with
    /// <c>CanContentScroll</c> item virtualization, rows outside the viewport simply have no
    /// container, so this doubles as "nearest visible row": an exact hit returns that row; a
    /// position above all realized rows clamps to the first, below clamps to the last (covers
    /// both the rubber-band anchor - "nearest row to the press, including empty space below the
    /// last row" - and the live current-index during a drag - "first/last visible index when the
    /// pointer is outside the viewport"). -1 when the column has no realized rows at all (an
    /// empty column).
    /// </summary>
    private int FindNearestRowIndex(Point positionInList)
    {
        if (List is null)
        {
            return -1;
        }

        int? firstIndex = null;
        int? lastIndex = null;
        double firstTop = 0;
        double lastBottom = 0;

        for (var i = 0; i < List.Items.Count; i++)
        {
            if (List.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item)
            {
                continue;
            }

            var bounds = item.TransformToAncestor(List).TransformBounds(new Rect(item.RenderSize));
            if (positionInList.Y >= bounds.Top && positionInList.Y < bounds.Bottom)
            {
                return i;
            }

            if (firstIndex is null || bounds.Top < firstTop)
            {
                firstIndex = i;
                firstTop = bounds.Top;
            }

            if (lastIndex is null || bounds.Bottom > lastBottom)
            {
                lastIndex = i;
                lastBottom = bounds.Bottom;
            }
        }

        if (firstIndex is null || lastIndex is null)
        {
            return -1;
        }

        return positionInList.Y < firstTop ? firstIndex.Value : lastIndex.Value;
    }

    /// <summary>
    /// Explorer drag hovering over this column with file data: accept it (Copy by default, Move
    /// with Shift held), highlight the column via <see cref="IsDropTargetProperty"/>, and - if the
    /// pointer is over a Directory or Drive row - highlight that single row via
    /// <see cref="IsDropTargetRowProperty"/> so the receiving directory reads exactly like a
    /// selected row (a file row or the column background gets no row highlight, only the
    /// column-level border). Anything else (no file data) is rejected.
    /// </summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            UpdateDropTargetRowHighlight(null);
            return;
        }

        e.Effects = (e.KeyStates & DragDropKeyStates.ShiftKey) != 0
            ? DragDropEffects.Move
            : DragDropEffects.Copy;
        SetIsDropTarget(this, true);

        var hoveredIndex = FindEntryIndex(e.OriginalSource as DependencyObject);
        var isDirectoryOrDrive = hoveredIndex is { } index && Column is { } column
            && index >= 0 && index < column.Entries.Count
            && column.Entries[index].Kind is EntryKind.Directory or EntryKind.Drive;
        UpdateDropTargetRowHighlight(isDirectoryOrDrive ? hoveredIndex : null);

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        SetIsDropTarget(this, false);
        UpdateDropTargetRowHighlight(null);
    }

    /// <summary>
    /// Extracts the dropped file paths, hit-tests which row (if any) the pointer was over, and
    /// raises <see cref="FileDropRequested"/>. Modifier state is reported raw (Shift/Ctrl held) -
    /// the host, not this view, decides what that means for copy vs. move.
    /// </summary>
    private void OnDrop(object sender, DragEventArgs e)
    {
        SetIsDropTarget(this, false);
        UpdateDropTargetRowHighlight(null);

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var targetEntryIndex = FindEntryIndex(e.OriginalSource as DependencyObject) ?? -1;
        var shiftHeld = (e.KeyStates & DragDropKeyStates.ShiftKey) != 0;
        var ctrlHeld = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
        FileDropRequested?.Invoke(this, new FileDropInfo(paths, targetEntryIndex, shiftHeld, ctrlHeld));
        e.Handled = true;
    }

    /// <summary>
    /// Sets/clears <see cref="IsDropTargetRowProperty"/> on every currently realized row container so
    /// only <paramref name="targetRowIndex"/> (or none, when <c>null</c>) is highlighted. Iterating
    /// every realized container - not just the target - is what clears a stale flag left on a
    /// container the pointer has moved off of, mirroring <see cref="UpdateRubberBandHoverHighlight"/>.
    /// </summary>
    private void UpdateDropTargetRowHighlight(int? targetRowIndex)
    {
        if (List is null)
        {
            return;
        }

        for (var i = 0; i < List.Items.Count; i++)
        {
            if (List.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item)
            {
                continue;
            }

            SetIsDropTargetRow(item, targetRowIndex == i);
        }
    }

    /// <summary>
    /// True when <paramref name="source"/> (typically a mouse event's <c>OriginalSource</c>) is a
    /// <see cref="ScrollBar"/> itself or one of its descendants (Thumb, RepeatButtons, Track),
    /// walking up the visual/logical tree only as far as <see cref="List"/>. Used to keep a press on
    /// the vertical scrollbar out of the empty-space rubber-band branch of
    /// <see cref="OnListPreviewMouseDown"/> - see its remarks.
    /// </summary>
    private bool IsOnScrollBar(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, List))
        {
            if (source is ScrollBar)
            {
                return true;
            }

            source = source is Visual visual
                ? VisualTreeHelper.GetParent(visual)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    /// <summary>Walks up from a click's <c>OriginalSource</c> to the containing <see cref="ListBoxItem"/>.</summary>
    private int? FindEntryIndex(DependencyObject? source)
    {
        if (List is null)
        {
            return null;
        }

        while (source is not null && source is not ListBoxItem)
        {
            source = source is Visual visual
                ? VisualTreeHelper.GetParent(visual)
                : LogicalTreeHelper.GetParent(source);
        }

        if (source is not ListBoxItem item)
        {
            return null;
        }

        var index = List.ItemContainerGenerator.IndexFromContainer(item);
        return index >= 0 ? index : null;
    }

    /// <summary>
    /// Estimates how many rows currently fit in the list's viewport, for the
    /// PageUp/PageDown hint in <see cref="CursorMoveRequestedEventArgs"/>. Returns 0 when
    /// it cannot be determined (no items realized yet, or the viewport has not been measured).
    /// </summary>
    internal int GetVisibleRowCount()
    {
        if (List is null || List.Items.Count == 0)
        {
            return 0;
        }

        if (FindVisualChild<ScrollViewer>(List) is not { ViewportHeight: > 0 } scrollViewer)
        {
            return 0;
        }

        if (List.ItemContainerGenerator.ContainerFromIndex(0) is not FrameworkElement { ActualHeight: > 0 } container)
        {
            return 0;
        }

        return Math.Max(1, (int)(scrollViewer.ViewportHeight / container.ActualHeight));
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                return typed;
            }

            if (FindVisualChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
