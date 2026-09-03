using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zurari.Controls;
using Zurari.Core;
using Zurari.Runtime;
using Zurari.Shell;

namespace Zurari.App;

/// <summary>
/// Composition root: owns the <see cref="MessageLoop"/> and <see cref="WorkerRuntime"/>, wires
/// <see cref="ColumnBrowser"/> events to <see cref="Msg"/>s 1:1, and re-renders on every state
/// change via <see cref="StateProjection"/>. No decisions live here — only construction and
/// straight-line event-to-message translation.
/// </summary>
public sealed partial class MainWindow : Window, IDisposable
{
    private readonly WorkerRuntime runtime;
    private readonly ShellEffectExecutor shellExecutor;
    private readonly JobEngine jobEngine;
    private readonly DirectoryWatcher directoryWatcher;
    private readonly MessageLoop loop;
    private readonly ShellIconCache iconCache = new();

    /// <summary>Persisted preferences, at the default per-user location.</summary>
    private readonly UserSettingsStore settingsStore = new();

    /// <summary>Lets a render reuse the row arrays it built last time - see <see cref="ProjectionCache"/>.</summary>
    private readonly ProjectionCache projectionCache = new();

    /// <summary>
    /// The job strip's actual <c>ItemsSource</c>, assigned once in the constructor and never
    /// replaced - <see cref="RenderJobs"/> reconciles it in place (update/insert/remove/move) on
    /// every render instead, which is what keeps a running job's row (and its "キャンセル" button
    /// container) alive across the frequent <see cref="Msg.JobProgress"/>-driven renders. See
    /// <see cref="JobRowVm"/>'s remarks for why that matters (Phase 5 bug B1).
    /// </summary>
    private readonly ObservableCollection<JobRowVm> jobRows = new();

    /// <summary>
    /// The <see cref="AppState.Columns"/> most recently pushed into <see cref="Browser"/>.
    /// <see cref="Render"/> only reassigns <see cref="Controls.ColumnBrowser.Columns"/> when this
    /// differs (by the underlying array reference - see its remarks) from the incoming state's,
    /// so unrelated renders (job progress, preview loads, ...) do not force
    /// <see cref="Controls.ColumnBrowser"/> to rebuild/re-sync every column, which otherwise yanks
    /// back any in-progress user scrolling (Phase 5 bug B3).
    /// </summary>
    private ImmutableArray<Column> lastRenderedColumns;

    /// <summary>
    /// The <see cref="AppState.FocusedColumn"/> most recently pushed into <see cref="Browser"/>.
    /// Focus-only changes (<see cref="Msg.GoToParent"/>, <see cref="Msg.FocusColumn"/>) leave
    /// <see cref="AppState.Columns"/>'s array reference untouched, so the guard above alone would
    /// never re-project them - the strong-cursor highlight silently stopped following focus (Left/
    /// Backspace looked like a no-op) until this was tracked alongside it.
    /// </summary>
    private int lastRenderedFocusedColumn = -1;

    /// <summary>
    /// The <see cref="StateProjection.PreviewVm.Generation"/> of the image most recently decoded
    /// into <see cref="PreviewImage"/>'s source - lets <see cref="RenderPreview"/> skip re-decoding
    /// the same <c>BitmapImage</c> on every unrelated re-render (e.g. a job progress tick) and only
    /// pay the decode cost when the previewed file actually changed.
    /// </summary>
    private int lastDecodedImageGeneration = -1;

    /// <summary>
    /// How many items <see cref="TrashPlaceRow"/> last saw in the recycle bin. Only
    /// <see cref="ResolveIcon"/>'s cache key, so emptying the bin re-asks the shell for its icon.
    /// Written from a worker thread.
    /// </summary>
    private long trashItemCount;

    /// <summary>The preview header's two lines, kept so a pane resize can re-fit them - see <see cref="FitPreviewHeader"/>.</summary>
    private string? previewFileName;

    private string? previewOriginalDirectory;

    /// <summary>
    /// How long <see cref="RunEffect"/> holds the latest <see cref="Effect.LoadPreview"/> before
    /// submitting it (Phase 5 fix P1). Every newer <c>LoadPreview</c> replaces the held one and
    /// restarts <see cref="previewDebounceTimer"/>, so a fast cursor never queues more than one
    /// outstanding preview load - the fix that stops preview I/O from making cursor movement feel
    /// sluggish. Non-preview effects are unaffected; they still flow to their executor immediately.
    /// </summary>
    private static readonly TimeSpan PreviewDebounceInterval = TimeSpan.FromMilliseconds(150);

    private readonly PendingEffectGate<Effect.LoadPreview> previewGate = new();
    private DispatcherTimer? previewDebounceTimer;

    /// <summary>
    /// The same coalescing as <see cref="previewGate"/>, for the column that fills in beside the
    /// cursor. Held for the same reason and with the same interval: a held-down arrow key would
    /// otherwise put one directory listing on the worker pool per keystroke, and on a network share
    /// that is exactly the stall the preview slot was separated out to avoid.
    /// </summary>
    private readonly PendingEffectGate<Effect.ReadDirectory> speculativeReadGate = new();
    private DispatcherTimer? speculativeReadTimer;

    public MainWindow()
    {
        InitializeComponent();

        JobStrip.ItemsSource = jobRows;

        runtime = new WorkerRuntime(
            post: PostToLoop, places: RootPlaces, trash: TrashPlaceRow, settings: settingsStore);
        shellExecutor = new ShellEffectExecutor(post: PostToLoop);
        jobEngine = new JobEngine(post: PostToLoop);
        directoryWatcher = new DirectoryWatcher(post: PostToLoop);
        loop = new MessageLoop(AppState.Initial, RunEffect, Render);

        Browser.CursorMoveRequested += (_, e) => Dispatch(ToCursorMsg(e));
        Browser.ColumnFocusRequested += (_, e) => Dispatch(new Msg.FocusColumn(e.ColumnIndex));
        Browser.EntryActivated += (_, e) => Dispatch(new Msg.EnterDirectory(e.ColumnIndex, e.EntryIndex));
        Browser.NavigateUpRequested += (_, e) => Dispatch(new Msg.GoToParent(e.ColumnIndex));
        Browser.EntryPointerPressed += OnEntryPointerPressed;
        Browser.EntryClicked += OnEntryClicked;
        Browser.EntryDragRequested += OnEntryDragRequested;
        Browser.FileDropRequested += (_, e) => Dispatch(
            new Msg.DropFiles(e.ColumnIndex, e.TargetEntryIndex, [.. e.Paths], e.ShiftHeld, e.CtrlHeld));
        Browser.MarkRangeRequested += (_, e) => Dispatch(
            new Msg.MarkRange(e.ColumnIndex, e.FromIndex, e.ToIndex, e.Additive));
        Browser.RubberBandStarted += OnRubberBandStarted;
        Browser.SectionToggleRequested += (_, e) => Dispatch(new Msg.ToggleSection(e.ColumnIndex, e.EntryIndex));

        // Restore the persisted preview-pane width (clamped: the XAML Min/star sizing keeps it
        // on-screen even if the saved value is larger than the current window).
        if (settingsStore.Load().PreviewWidth is { } previewWidth && previewWidth >= 150)
        {
            PreviewColumn.Width = new GridLength(Math.Min(previewWidth, 2000));
        }

        Closed += (_, _) =>
        {
            // Update, not Save: a bare Save writes the whole file, so saving just the width threw
            // away every pinned place and collapsed section on the way out of the app.
            settingsStore.Update(s => s with { PreviewWidth = PreviewColumn.ActualWidth });
            Dispose();
        };
        Loaded += (_, _) => Browser.Focus();

        // The header's two lines are shortened to fit, so a resize has to re-shorten them - the
        // splitter drag is the whole reason the preview pane's width is not a constant.
        PreviewPane.SizeChanged += (_, _) => FitPreviewHeader();
        KeyDown += OnWindowKeyDown;

        if (ColumnBrowser.InputTraceEnabled)
        {
            // Window-level tunneling handlers see EVERY press/release before any child does,
            // regardless of capture or which element the event targets - closes the diagnostic
            // blind spot where a release never reaches a column's list (and therefore never
            // shows up in the [input] log at all).
            PreviewMouseDown += (_, e) => TraceWindowMouse("winDown", e);
            PreviewMouseUp += (_, e) => TraceWindowMouse("winUp", e);
        }

        Dispatch(new Msg.Refresh());
    }

    /// <summary>
    /// The places the drive pane lists above the drives. Resolved here because it needs both layers:
    /// <see cref="Zurari.Shell.KnownFolders"/> to find them (Downloads has no
    /// <c>Environment.SpecialFolder</c> member, so the shell API is the only route) and
    /// <see cref="WorkerRuntime"/> to consume them — and Runtime is not allowed to depend on Shell.
    /// </summary>
    private static IReadOnlyList<RootPlace> RootPlaces() =>
        [.. Zurari.Shell.KnownFolders.UserPlaces().Select(f => new RootPlace(f.Path, f.Label))];

    /// <summary>
    /// The recycle bin row, labelled with what is in it. Resolved here because the totals come from
    /// <c>SHQueryRecycleBin</c> and Runtime may not depend on Shell.
    /// </summary>
    private TrashPlace TrashPlaceRow()
    {
        var summary = Zurari.Shell.RecycleBinFolder.Query();

        // Runs on a worker thread; ResolveIcon reads it on the UI thread. Volatile rather than a
        // lock: a stale read costs a full/empty bin icon for one render, and the next read fixes it.
        Volatile.Write(ref trashItemCount, summary.ItemCount);

        return new TrashPlace(
            summary.ItemCount <= 0
                ? "ゴミ箱"
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"ゴミ箱 ({summary.ItemCount})"),
            summary.ItemCount,
            summary.TotalBytes);
    }

    /// <summary>
    /// The icon for one row. Everything the shell can identify from a file name goes through
    /// <see cref="ShellIconCache.GetIcon"/>; the ゴミ箱 row has no path at all, so its icon comes from
    /// looking the bin itself up in the shell namespace - which draws it full or empty to match.
    /// </summary>
    /// <remarks>
    /// The item count is the icon's cache variant, not what chooses it: the shell decides how a full
    /// bin looks, and a changed count is simply the signal to ask again.
    /// </remarks>
    private ImageSource? ResolveIcon(Entry entry) =>
        entry.Target is Location.RecycleBin
            ? iconCache.GetShellNamespaceIcon(
                Location.RecycleBin.ParsingName,
                Volatile.Read(ref trashItemCount) > 0 ? "full" : "empty")
            : iconCache.GetIcon(entry.Kind, entry.Name);

    /// <summary>Diagnostic-only: logs a window-level mouse transition (see ctor wiring).</summary>
    private void TraceWindowMouse(string kind, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(this);
        Trace.WriteLine(
            $"[win] {kind} button={e.ChangedButton} state={e.ButtonState} clicks={e.ClickCount} " +
            $"pos={position.X:F0},{position.Y:F0} src={e.OriginalSource?.GetType().Name} " +
            $"captured={Mouse.Captured?.GetType().Name ?? "none"}");
    }

    /// <summary>
    /// Disposes the <see cref="WorkerRuntime"/>, <see cref="ShellEffectExecutor"/>,
    /// <see cref="JobEngine"/>, and <see cref="DirectoryWatcher"/> owned by this window.
    /// </summary>
    public void Dispose()
    {
        previewDebounceTimer?.Stop();
        speculativeReadTimer?.Stop();
        runtime.Dispose();
        shellExecutor.Dispose();
        jobEngine.Dispose();
        directoryWatcher.Dispose();
    }

    /// <summary>
    /// Routes an <see cref="Effect"/> to the executor that can perform it: filesystem effects go
    /// to <see cref="WorkerRuntime"/>, shell effects go to <see cref="ShellEffectExecutor"/>. Pure
    /// dispatch by effect type — no decisions beyond "which executor", with one exception:
    /// <see cref="Effect.LoadPreview"/> is debounced (see <see cref="SchedulePreviewLoad"/>) rather
    /// than submitted immediately, since it is the one effect a fast cursor can otherwise flood
    /// the worker pool with (Phase 5 fix P1). Every other effect still flows straight through.
    /// </summary>
    /// <summary>
    /// Hands each effect to its executor. The routing decision lives in
    /// <see cref="EffectRouting"/> so it can be tested - see its remarks for what a silently
    /// unrouted effect cost.
    /// </summary>
    private void RunEffect(Effect effect)
    {
        switch (EffectRouting.For(effect))
        {
            case EffectTarget.Runtime:
                if (effect is Effect.ReadDirectory { Speculative: true } speculativeRead)
                {
                    ScheduleSpeculativeRead(speculativeRead);
                }
                else
                {
                    runtime.Submit(effect);
                }

                break;
            case EffectTarget.Preview:
                if (effect is Effect.LoadPreview loadPreview)
                {
                    SchedulePreviewLoad(loadPreview);
                }
                else
                {
                    // A cancel must not be held by the debounce gate - it is what releases whatever
                    // the gate is holding.
                    runtime.Submit(effect);
                }

                break;
            case EffectTarget.Shell:
                shellExecutor.Submit(effect);
                break;
            case EffectTarget.Jobs:
                jobEngine.Submit(effect);
                break;
        }
    }

    /// <summary>
    /// Holds <paramref name="effect"/> in <see cref="previewGate"/> (replacing whatever was
    /// pending) and (re)starts <see cref="previewDebounceTimer"/> at <see cref="PreviewDebounceInterval"/>.
    /// Only the timer tick (<see cref="OnPreviewDebounceTick"/>) ever actually submits a
    /// <see cref="Effect.LoadPreview"/> to <see cref="runtime"/> - this method never does, which is
    /// the coalescing behavior itself (Phase 5 fix P1). The timer is created lazily on first use and
    /// reused afterward.
    /// </summary>
    private void SchedulePreviewLoad(Effect.LoadPreview effect)
    {
        previewGate.Hold(effect);

        if (previewDebounceTimer is null)
        {
            previewDebounceTimer = new DispatcherTimer { Interval = PreviewDebounceInterval };
            previewDebounceTimer.Tick += OnPreviewDebounceTick;
        }

        previewDebounceTimer.Stop();
        previewDebounceTimer.Start();
    }

    /// <summary>
    /// Holds the newest speculative listing and (re)starts its timer, exactly as
    /// <see cref="SchedulePreviewLoad"/> does for previews.
    /// </summary>
    private void ScheduleSpeculativeRead(Effect.ReadDirectory effect)
    {
        speculativeReadGate.Hold(effect);

        if (speculativeReadTimer is null)
        {
            speculativeReadTimer = new DispatcherTimer { Interval = PreviewDebounceInterval };
            speculativeReadTimer.Tick += OnSpeculativeReadTick;
        }

        speculativeReadTimer.Stop();
        speculativeReadTimer.Start();
    }

    private void OnSpeculativeReadTick(object? sender, EventArgs e)
    {
        speculativeReadTimer?.Stop();
        var effect = speculativeReadGate.Take();
        if (effect is not null)
        {
            runtime.Submit(effect);
        }
    }

    private void OnPreviewDebounceTick(object? sender, EventArgs e)
    {
        previewDebounceTimer?.Stop();
        var effect = previewGate.Take();
        if (effect is not null)
        {
            runtime.Submit(effect);
        }
    }

    private void PostToLoop(Msg msg) => Dispatcher.BeginInvoke(() => Dispatch(msg));

    /// <summary>
    /// The single choke point every <see cref="Msg"/> passes through on its way to
    /// <see cref="loop"/> - every wiring lambda and handler in this class calls this instead of
    /// <c>loop.Dispatch</c> directly, purely so the input-pipeline diagnostic tracing (see
    /// <see cref="ColumnBrowser.InputTraceEnabled"/> / <c>--debug-input</c>) can log the type of
    /// every Msg dispatched without needing a log line at each of the twenty-odd call sites.
    /// </summary>
    private void Dispatch(Msg msg)
    {
        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine($"[app] dispatch {msg.GetType().Name}");
        }

        loop.Dispatch(msg);
    }

    private static Msg ToCursorMsg(CursorMoveRequestedEventArgs e)
    {
        var pageSize = Math.Max(1, e.VisibleRowCount);
        return e.Move switch
        {
            CursorMove.Up => new Msg.CursorUp(e.ColumnIndex),
            CursorMove.Down => new Msg.CursorDown(e.ColumnIndex),
            CursorMove.PageUp => new Msg.CursorPageUp(e.ColumnIndex, pageSize),
            CursorMove.PageDown => new Msg.CursorPageDown(e.ColumnIndex, pageSize),
            CursorMove.Home => new Msg.CursorHome(e.ColumnIndex),
            CursorMove.End => new Msg.CursorEnd(e.ColumnIndex),
            _ => new Msg.Noop(),
        };
    }

    /// <summary>
    /// Every plain press just moves the cursor there - directory activation happens on release-
    /// without-drag instead (see <see cref="Zurari.Controls.EntryClickedEventArgs"/>), so a press
    /// that turns into a drag never enters a directory out from under column 3, which would
    /// otherwise collapse the very column the drag needs as a drop target. Ctrl+left does NOTHING
    /// at press time - not even <see cref="Msg.CursorTo"/> - because a Ctrl press might still turn
    /// into an additive rubber-band drag (see <see cref="Zurari.Controls.ColumnView.OnListPreviewMouseMove"/>)
    /// and must not disturb cursor/mark state before that is decided; the toggle itself happens at
    /// release instead, in <see cref="OnEntryClicked"/>. A right click additionally shows the shell
    /// context menu - for every marked entry in the column when the pressed row is marked, otherwise
    /// just that entry - blocking synchronously until the user picks a command or dismisses it, and
    /// - if a command was invoked - dispatches <see cref="Msg.Refresh"/> since the shell may have
    /// renamed/deleted/pasted something that this pane needs to reflect.
    /// </summary>
    private void OnEntryPointerPressed(object? sender, EntryPointerPressedEventArgs e)
    {
        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine(
                $"[app] OnEntryPointerPressed col={e.ColumnIndex} idx={e.EntryIndex} "
                + $"button={e.Button} modifiers={e.Modifiers}");
        }

        if (!(e.Button == MouseButton.Left && e.Modifiers == ModifierKeys.Control))
        {
            Dispatch(new Msg.CursorTo(e.ColumnIndex, e.EntryIndex));
        }

        if (e.Button != MouseButton.Right)
        {
            return;
        }

        ShowContextMenu(e.ColumnIndex, e.EntryIndex, e.ScreenPosition);
    }

    /// <summary>
    /// Opens the shell context menu for one row at <paramref name="screenPosition"/>, on the row's
    /// whole marked set when it is part of one. Does nothing for a row the shell cannot name.
    /// </summary>
    /// <remarks>
    /// Reached both by right-click and by the menu key (see <c>OnBrowserPreviewKeyDown</c>), so the
    /// two cannot drift apart. Names come from <see cref="ResolveShellName"/> rather than
    /// <see cref="ResolveFullPath"/>: the recycle-bin row has no filesystem path at all, and it is
    /// the row whose menu - "ゴミ箱を空にする" - there is no other way to reach.
    /// </remarks>
    private void ShowContextMenu(int columnIndex, int entryIndex, Point screenPosition)
    {
        if (ResolveShellName(columnIndex, entryIndex) is not { } shellName)
        {
            return;
        }

        var paths = IsEntryMarked(columnIndex, entryIndex) ? ResolveMarkedFullPaths(columnIndex) : [shellName];
        if (paths.Count == 0)
        {
            paths = [shellName];
        }

        var ownerHwnd = new WindowInteropHelper(this).Handle;
        if (ShellContextMenu.Show(ownerHwnd, paths, (int)screenPosition.X, (int)screenPosition.Y))
        {
            Dispatch(new Msg.Refresh());
        }
    }

    /// <summary>
    /// A true click (release-without-drag - see <see cref="EntryClickedEventArgs"/>; this also
    /// covers the wiggle-click case where <see cref="Zurari.Controls.ColumnView"/> converted a
    /// stillborn rubber-band back into a click - see its <c>OnListPreviewMouseUp</c>). Ctrl held at
    /// RELEASE toggles just that entry's mark and does nothing else - no <see cref="Msg.ClearMarks"/>,
    /// no <see cref="Msg.EnterDirectory"/> - so Ctrl+click only ever builds/shrinks a multi-selection
    /// without navigating. Without Ctrl, collapses any existing mark set in the column, Explorer/
    /// Finder style, then activates the clicked entry. Net behavior table (see also
    /// <see cref="Zurari.Controls.ColumnView.OnListPreviewMouseMove"/> for the drag/rubber-band half):
    /// <list type="bullet">
    /// <item>plain click = select-only-this + enter, but <see cref="Msg.EnterDirectory.FocusChild"/>
    /// false - the child pane opens without stealing focus/cursor-highlight from the clicked column
    /// (that only happens via Enter/→/double-click, which dispatch the FocusChild-true default via
    /// <see cref="Zurari.Controls.ColumnBrowser.EntryActivated"/>)</item>
    /// <item>Ctrl+click = toggle mark (no navigation, no clearing)</item>
    /// <item>plain quick drag = replace-selection rubber band</item>
    /// <item>Ctrl+drag = additive rubber band</item>
    /// <item>plain hold-then-drag = file drag (the marked SET, if the pressed row was marked)</item>
    /// <item>Ctrl+hold-then-drag = additive rubber band (Ctrl wins over the hold timing)</item>
    /// </list>
    /// A press that turns into a real drag never reaches here at all (no <see cref="EntryClicked"/>
    /// is raised for it), which is what lets dragging a marked set keep every mark.
    /// </summary>
    private void OnEntryClicked(object? sender, EntryClickedEventArgs e)
    {
        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine(
                $"[app] OnEntryClicked col={e.ColumnIndex} idx={e.EntryIndex} modifiers={Keyboard.Modifiers}");
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Dispatch(new Msg.ToggleMark(e.ColumnIndex, e.EntryIndex));
            return;
        }

        Dispatch(new Msg.ClearMarks(e.ColumnIndex));
        Dispatch(new Msg.EnterDirectory(e.ColumnIndex, e.EntryIndex, FocusChild: false));
    }

    /// <summary>
    /// A rubber-band drag just activated in <paramref name="e"/>.ColumnIndex - see
    /// <see cref="Zurari.Controls.RubberBandStartedEventArgs"/>. A REPLACE-mode band (Ctrl not held
    /// at activation) clears the column's existing marks immediately, so stale marks never render
    /// alongside the live drag highlight; an additive band leaves them untouched. Note a band that
    /// later converts back to a click (the wiggle-click fix in <see cref="Zurari.Controls.ColumnView"/>)
    /// has already dispatched this <see cref="Msg.ClearMarks"/> by the time that happens - harmless,
    /// since the click's own <see cref="OnEntryClicked"/> dispatches an equivalent
    /// <see cref="Msg.ClearMarks"/> right before its <see cref="Msg.EnterDirectory"/> anyway.
    /// </summary>
    private void OnRubberBandStarted(object? sender, RubberBandStartedEventArgs e)
    {
        if (!e.Additive)
        {
            Dispatch(new Msg.ClearMarks(e.ColumnIndex));
        }
    }

    /// <summary>
    /// Drag-out gesture reported by <see cref="ColumnBrowser"/>: resolves the entry's full path
    /// (drive entries at the virtual root are skipped - dragging a drive letter out means nothing)
    /// and starts the actual OLE drag via <see cref="DragDrop.DoDragDrop"/>, which the control
    /// itself never touches. When the dragged entry is marked, every marked entry's full path in
    /// that column rides along instead of just the one dragged. If the drag ends as a move, the
    /// source location may no longer contain the entry, so a <see cref="Msg.Refresh"/> is
    /// dispatched afterward.
    /// </summary>
    private void OnEntryDragRequested(object? sender, EntryDragRequestedEventArgs e)
    {
        var state = loop.State;
        if (e.ColumnIndex < 0 || e.ColumnIndex >= state.Columns.Length)
        {
            return;
        }

        if (state.Columns[e.ColumnIndex].Location.FilesystemPath is null)
        {
            return;
        }

        var fullPath = ResolveFullPath(e.ColumnIndex, e.EntryIndex);
        if (fullPath is null)
        {
            return;
        }

        var paths = IsEntryMarked(e.ColumnIndex, e.EntryIndex) ? ResolveMarkedFullPaths(e.ColumnIndex) : [fullPath];
        if (paths.Count == 0)
        {
            paths = [fullPath];
        }

        var data = new DataObject(DataFormats.FileDrop, paths.ToArray());
        var result = DragDrop.DoDragDrop(Browser, data, DragDropEffects.Copy | DragDropEffects.Move);
        if (result == DragDropEffects.Move)
        {
            Dispatch(new Msg.Refresh());
        }
    }

    /// <summary>
    /// Resolves the full filesystem path of an entry the same way <c>Transition</c> does
    /// (<c>Path.Combine(column.Path, entry.Name)</c>, with the root column's empty
    /// <see cref="Column.Path"/> meaning entries are drives already named like <c>"C:\\"</c>).
    /// Returns <c>null</c> if the indices no longer match the current state (e.g. a race with a
    /// directory reload).
    /// </summary>
    private string? ResolveFullPath(int columnIndex, int entryIndex)
    {
        var state = loop.State;
        if (columnIndex < 0 || columnIndex >= state.Columns.Length)
        {
            return null;
        }

        var column = state.Columns[columnIndex];
        if (entryIndex < 0 || entryIndex >= column.Entries.Length)
        {
            return null;
        }

        var entry = column.Entries[entryIndex];
        return entry.Target is { } target ? target.FilesystemPath : column.Location.ChildPath(entry.Name);
    }

    /// <summary>
    /// What the shell namespace calls the row at these indices - a filesystem path for almost
    /// everything, the bin's CLSID for the ゴミ箱 row. <c>null</c> when the indices no longer match
    /// the current state, or the row is not something the shell can name at all (a section header).
    /// </summary>
    private string? ResolveShellName(int columnIndex, int entryIndex)
    {
        var state = loop.State;
        if (columnIndex < 0 || columnIndex >= state.Columns.Length)
        {
            return null;
        }

        var column = state.Columns[columnIndex];
        if (entryIndex < 0 || entryIndex >= column.Entries.Length)
        {
            return null;
        }

        var entry = column.Entries[entryIndex];
        if (entry.Kind == Core.EntryKind.Header)
        {
            return null;
        }

        return entry.Target is { } target ? target.ShellParsingName : column.Location.ChildPath(entry.Name);
    }

    private bool IsEntryMarked(int columnIndex, int entryIndex)
    {
        var state = loop.State;
        if (columnIndex < 0 || columnIndex >= state.Columns.Length)
        {
            return false;
        }

        var column = state.Columns[columnIndex];
        return entryIndex >= 0 && entryIndex < column.Entries.Length && column.Entries[entryIndex].IsMarked;
    }

    /// <summary>Full paths of every marked entry in <paramref name="columnIndex"/>, in entry order.</summary>
    private List<string> ResolveMarkedFullPaths(int columnIndex)
    {
        var state = loop.State;
        if (columnIndex < 0 || columnIndex >= state.Columns.Length)
        {
            return [];
        }

        var column = state.Columns[columnIndex];
        var result = new List<string>();
        foreach (var entry in column.Entries)
        {
            if (entry.IsMarked)
            {
                var path = entry.Target is { } target
                    ? target.FilesystemPath
                    : column.Location.ChildPath(entry.Name);
                if (path is not null)
                {
                    result.Add(path);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Opens the context menu for the focused column's cursor row, anchored under that row's
    /// bottom-left corner the way Windows places a menu-key menu.
    /// </summary>
    /// <remarks>
    /// The keyboard route to the shell menu. It is the only route for the ゴミ箱 row on a machine
    /// driven from the keyboard, and 空にする lives nowhere else.
    /// </remarks>
    private void ShowContextMenuAtCursor()
    {
        var state = loop.State;
        var columnIndex = state.FocusedColumn;
        if (columnIndex < 0 || columnIndex >= state.Columns.Length)
        {
            return;
        }

        var cursor = state.Columns[columnIndex].Cursor;
        if (cursor < 0 || Browser.TryGetRowScreenRect(columnIndex, cursor) is not { } rowRect)
        {
            return;
        }

        ShowContextMenu(columnIndex, cursor, new Point(rowRect.Left, rowRect.Bottom));
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 || (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control))
        {
            // F5 is the primary refresh key (already wired); Ctrl+R is a discoverability alias -
            // some users look for that instead, especially coming from a browser-style filer.
            Dispatch(new Msg.Refresh());
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            // Shift+Delete bypasses the recycle bin entirely (Windows Explorer convention);
            // plain Delete is the normal recycle-bin path.
            TryDelete(permanent: Keyboard.Modifiers == ModifierKeys.Shift);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
        {
            Dispatch(new Msg.ToggleMarkAtCursor(loop.State.FocusedColumn));
            e.Handled = true;
        }
        else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+B adds the folder the cursor is pointing at, without having to enter it. The
            // gesture that matters once several columns are open and the drive pane is off-screen.
            Dispatch(new Msg.PinEntryAtCursor(loop.State.FocusedColumn));
            e.Handled = true;
        }
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+D pins the location this column is showing into the drive pane, the way a browser
            // bookmarks the current page. Delete on the pinned row removes it again.
            Dispatch(new Msg.PinFocusedLocation(loop.State.FocusedColumn));
            e.Handled = true;
        }
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+E ejects whatever is in the drive under the cursor - a disc, a card, or an image
            // that was mounted by opening it. It is also the only way to unmount one.
            Dispatch(new Msg.EjectAtCursor(loop.State.FocusedColumn));
            e.Handled = true;
        }
        else if (e.Key is Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
        {
            ShowContextMenuAtCursor();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Dispatch(new Msg.ClearMarks(loop.State.FocusedColumn));
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            TryCopyToClipboard(isMove: false);
            e.Handled = true;
        }
        else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
        {
            TryCopyToClipboard(isMove: true);
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            TryPasteFromClipboard();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Ctrl+C/Ctrl+X: places the focused column's marked full paths (or, absent any marks, just
    /// the cursor entry) on the OS clipboard as a <see cref="DataFormats.FileDrop"/> payload plus
    /// the "Preferred DropEffect" marker Explorer reads/writes, so the two interoperate in both
    /// directions. No-op when the focused column has nothing to offer (empty/no cursor) or is the
    /// virtual root (empty <see cref="Column.Path"/> - drives cannot be copied). Clipboard access
    /// can transiently fail with CLIPBRD_E_CANT_OPEN when another process holds the clipboard open;
    /// that failure is swallowed - the user simply sees nothing happen and can retry. Also dispatches
    /// <see cref="Msg.SetCutPending"/>: <paramref name="isMove"/> (Ctrl+X) sets it to the same paths
    /// just placed on the clipboard, so <see cref="Controls.ColumnBrowser"/> dims those rows
    /// Explorer-style; a plain Ctrl+C clears it (a copy is not a pending cut).
    /// </summary>
    private void TryCopyToClipboard(bool isMove)
    {
        var state = loop.State;
        var columnIndex = state.FocusedColumn;
        if (columnIndex < 0 || columnIndex >= state.Columns.Length)
        {
            return;
        }

        var column = state.Columns[columnIndex];
        if (column.Location.FilesystemPath is null)
        {
            return;
        }

        var paths = ResolveMarkedFullPaths(columnIndex);
        if (paths.Count == 0)
        {
            var cursorPath = ResolveFullPath(columnIndex, column.Cursor);
            if (cursorPath is null)
            {
                return;
            }

            paths = [cursorPath];
        }

        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, paths.ToArray());
            var effect = isMove ? DragDropEffects.Move : DragDropEffects.Copy;
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)effect)));
            Clipboard.SetDataObject(data, copy: true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // CLIPBRD_E_CANT_OPEN or similar transient clipboard-ownership failure - nothing to do,
            // including the cut-pending dispatch below (dimming rows for a cut that never actually
            // reached the clipboard would be misleading).
            return;
        }

        Dispatch(new Msg.SetCutPending(isMove ? [.. paths] : []));
    }

    /// <summary>
    /// Ctrl+V: reads a <see cref="DataFormats.FileDrop"/> payload back off the clipboard (via
    /// <see cref="ClipboardFileDropParser.TryParse"/>, shared with tests) and dispatches
    /// <see cref="Msg.PasteRequested"/> for the focused column 1:1. No-op if the clipboard holds
    /// nothing usable. Swallows the same transient clipboard-access failures as
    /// <see cref="TryCopyToClipboard"/>.
    /// </summary>
    private void TryPasteFromClipboard()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null || !ClipboardFileDropParser.TryParse(data, out var paths, out var isMove))
            {
                return;
            }

            Dispatch(new Msg.PasteRequested(loop.State.FocusedColumn, [.. paths], isMove));
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // CLIPBRD_E_CANT_OPEN or similar transient clipboard-ownership failure - nothing to do.
        }
    }

    /// <summary>
    /// Deletes the marked entries of the focused column when it has any (Drives are never
    /// eligible, so a column with only a marked drive falls through to the single-entry path
    /// below); otherwise falls back to deleting just the cursor entry, as before marks existed.
    /// <paramref name="permanent"/> (Shift+Delete) bypasses the recycle bin entirely; the
    /// confirmation text warns about that explicitly rather than reusing the recycle-bin wording.
    /// </summary>
    private void TryDelete(bool permanent)
    {
        var state = loop.State;
        var columnIndex = state.FocusedColumn;
        var column = state.Columns[columnIndex];

        var markedCount = 0;
        foreach (var entry in column.Entries)
        {
            if (entry.IsMarked && entry.Kind != Zurari.Core.EntryKind.Drive)
            {
                markedCount++;
            }
        }

        if (markedCount > 0)
        {
            var markedResult = MessageBox.Show(
                this,
                permanent
                    ? $"選択した {markedCount} 件を完全に削除しますか?(ゴミ箱に入りません)"
                    : $"選択した {markedCount} 件をゴミ箱に移動しますか?",
                "削除の確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (markedResult == MessageBoxResult.Yes)
            {
                Dispatch(new Msg.DeleteMarked(columnIndex, permanent));
            }

            return;
        }

        TryDeleteFocusedEntry(permanent);
    }

    private void TryDeleteFocusedEntry(bool permanent)
    {
        var state = loop.State;
        var columnIndex = state.FocusedColumn;
        var column = state.Columns[columnIndex];
        if (column.Cursor < 0 || column.Cursor >= column.Entries.Length)
        {
            return;
        }

        var entry = column.Entries[column.Cursor];
        if (entry.Kind == Zurari.Core.EntryKind.Drive)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            permanent
                ? $"{entry.Name} を完全に削除しますか?(ゴミ箱に入りません)"
                : $"{entry.Name} をゴミ箱に移動しますか?",
            "削除の確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            Dispatch(new Msg.DeleteEntry(columnIndex, column.Cursor, permanent));
        }
    }

    /// <summary>Job strip "キャンセル" button: dispatches <see cref="Msg.JobCancelRequested"/> for the row's job.</summary>
    private void OnJobCancelClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is JobRowVm vm)
        {
            Dispatch(new Msg.JobCancelRequested(vm.JobId));
        }
    }

    /// <summary>Job strip "×" button: dispatches <see cref="Msg.JobDismissed"/> for the row's job.</summary>
    private void OnJobDismissClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is JobRowVm vm)
        {
            Dispatch(new Msg.JobDismissed(vm.JobId));
        }
    }

    /// <summary>Job strip "上書き" button (conflict prompt): resolves with <see cref="ConflictDecision.Overwrite"/>.</summary>
    private void OnJobOverwriteClicked(object sender, RoutedEventArgs e) =>
        DispatchConflictDecision(sender, ConflictDecision.Overwrite);

    /// <summary>Job strip "スキップ" button (conflict prompt): resolves with <see cref="ConflictDecision.Skip"/>.</summary>
    private void OnJobSkipConflictClicked(object sender, RoutedEventArgs e) =>
        DispatchConflictDecision(sender, ConflictDecision.Skip);

    /// <summary>Job strip "中止" button (conflict prompt): resolves with <see cref="ConflictDecision.Cancel"/>.</summary>
    private void OnJobCancelConflictClicked(object sender, RoutedEventArgs e) =>
        DispatchConflictDecision(sender, ConflictDecision.Cancel);

    private void DispatchConflictDecision(object sender, ConflictDecision decision)
    {
        if (((FrameworkElement)sender).DataContext is JobRowVm vm)
        {
            Dispatch(new Msg.JobConflictResolved(vm.JobId, decision));
        }
    }

    private void Render(AppState state)
    {
        // Reference comparison, not value comparison: Transition returns the very same
        // ImmutableArray<Column> instance (same underlying array) whenever a Msg does not touch
        // AppState.Columns at all (JobProgress/JobFailed/JobCancelRequested/JobDismissed, and the
        // untouched columns inside ShellOpCompleted/JobFinished's per-column loop). Skipping the
        // reassignment in that case is what stops Controls.ColumnBrowser.RebuildColumns and
        // ColumnView.SyncFromColumn from running - and yanking back user scroll/hover state - on
        // renders that have nothing to do with the browsed columns (Phase 5 bug B3).
        if (state.Columns != lastRenderedColumns || state.FocusedColumn != lastRenderedFocusedColumn)
        {
            Browser.Columns = StateProjection.Project(state, ResolveIcon, projectionCache);
            lastRenderedColumns = state.Columns;
            lastRenderedFocusedColumn = state.FocusedColumn;

            // Cheap reconcile (see DirectoryWatcher.SetWatchedPaths' remarks) - only worth doing
            // when the columns actually changed, same guard as the Browser.Columns reassignment
            // above, so an unrelated render (job progress, preview loads, ...) does not churn
            // watchers.
            // Only real directories can be watched. A virtual location (the drive list, and later
            // the recycle bin or the inside of an archive) has no path for FileSystemWatcher to
            // point at, so it is filtered out here rather than relying on the watcher to reject it.
            directoryWatcher.SetWatchedPaths(
                state.Columns
                    .Select(c => c.Location)
                    .OfType<Location.RealDirectory>()
                    .Select(d => d.Path)
                    .Distinct()
                    .ToList());
        }

        RenderJobs(state);
        RenderPreview(state);

        var focused = state.Columns[state.FocusedColumn];
        var focusedPath = focused.Location.FilesystemPath ?? "ドライブ";
        Title = "zurari — " + focusedPath;

        var markedCount = 0;
        foreach (var entry in focused.Entries)
        {
            if (entry.IsMarked)
            {
                markedCount++;
            }
        }

        var statusText = focusedPath + $" ({focused.Entries.Length} 件)";
        if (markedCount > 0)
        {
            statusText += $" | マーク: {markedCount}";
        }

        // The reply to something the user just asked for, when it has no other visible outcome -
        // an eject that the drive refused. Cleared by Transition as soon as the cursor moves on.
        if (state.Notice is { } notice)
        {
            statusText += " | " + notice;
        }

        StatusText.Text = statusText;
    }

    /// <summary>
    /// Reconciles <see cref="jobRows"/> (the job strip's stable <c>ItemsSource</c>) against the
    /// freshly-projected job list: existing rows are updated in place via
    /// <see cref="JobRowVm.UpdateFrom"/> (raising only property-level <c>PropertyChanged</c>, never
    /// touching the collection), a job no longer tracked in <see cref="AppState.Jobs"/> (dismissed,
    /// or auto-removed on a clean completion - see <c>Transition.JobFinished</c>) has its row
    /// removed, and a brand-new job gets a brand-new row inserted/moved into position. This is what
    /// keeps a running job's row container - including its "キャンセル" button - alive across the
    /// ~10/second <see cref="Msg.JobProgress"/>-driven renders instead of the whole list being
    /// discarded and rebuilt every tick (Phase 5 bug B1 - see <see cref="JobRowVm"/>'s remarks).
    /// </summary>
    private void RenderJobs(AppState state)
    {
        var jobs = StateProjection.ProjectJobs(state);

        for (var i = jobRows.Count - 1; i >= 0; i--)
        {
            var jobId = jobRows[i].JobId;
            var stillTracked = false;
            for (var j = 0; j < jobs.Count; j++)
            {
                if (jobs[j].JobId == jobId)
                {
                    stillTracked = true;
                    break;
                }
            }

            if (!stillTracked)
            {
                jobRows.RemoveAt(i);
            }
        }

        for (var i = 0; i < jobs.Count; i++)
        {
            var vm = jobs[i];
            var existingIndex = FindJobRowIndex(vm.JobId);
            if (existingIndex < 0)
            {
                jobRows.Insert(i, new JobRowVm(vm));
                continue;
            }

            if (existingIndex != i)
            {
                jobRows.Move(existingIndex, i);
            }

            jobRows[i].UpdateFrom(vm);
        }

        JobStrip.Visibility = jobRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private int FindJobRowIndex(int jobId)
    {
        for (var i = 0; i < jobRows.Count; i++)
        {
            if (jobRows[i].JobId == jobId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Renders the preview header - the folder a deleted file came from, then its name - shortening
    /// each from the middle to fit the pane's current width.
    /// </summary>
    /// <remarks>
    /// Called both when the preview changes and when the pane is resized, so it reads its text from
    /// <see cref="previewFileName"/>/<see cref="previewOriginalDirectory"/> rather than taking it as
    /// an argument: a resize has no new state to project from.
    /// </remarks>
    private void FitPreviewHeader()
    {
        var width = PreviewPane.ActualWidth;
        TextFitter.Fit(PreviewFileName, previewFileName, width, TextTrim.Middle);
        TextFitter.Fit(PreviewOriginalPath, previewOriginalDirectory, width, TextTrim.Path);
        PreviewOriginalPath.Visibility =
            string.IsNullOrEmpty(previewOriginalDirectory) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Wires <see cref="StateProjection.ProjectPreview"/> onto the preview pane's four
    /// mutually-exclusive content views (image / text-or-hex / capacity bar / status message), toggling
    /// visibility by <see cref="PreviewKind"/>, plus the Finder-style metadata block underneath
    /// (<see cref="PreviewMetadataBlock"/>, shown whenever <c>vm.MetadataText</c> is non-empty -
    /// independent of <see cref="PreviewKind"/>, since it applies to every kind alike).
    /// <see cref="PreviewKind.Binary"/> shares the same monospace text box as
    /// <see cref="PreviewKind.Text"/> - its <c>vm.Text</c> is already the label header plus hex
    /// dump, composed in the projection. The only decision made here rather than in the projection
    /// is the <c>ImageBytes</c> -&gt; <c>BitmapImage</c> decode, which is wiring (WPF-specific, not
    /// a display-formatting choice) - cached by <see cref="lastDecodedImageGeneration"/> so it only
    /// runs once per distinct preview, not on every unrelated re-render.
    /// </summary>
    /// <summary>
    /// Draws how full a volume is: a summary line, a bar, and what is left.
    /// </summary>
    /// <remarks>
    /// The bar is two star-sized grid columns rather than a width computed here, so it follows the
    /// splitter without this method being told about it. The recycle bin has a size but no capacity,
    /// so it gets the figures and no bar - a bar with nothing to be full of would be a lie.
    /// </remarks>
    private void RenderCapacity(StateProjection.CapacityVm capacity)
    {
        PreviewCapacitySummary.Text = capacity.Summary;

        if (capacity.UsedFraction is { } fraction)
        {
            PreviewCapacityUsed.Width = new GridLength(fraction, GridUnitType.Star);
            PreviewCapacityFree.Width = new GridLength(1 - fraction, GridUnitType.Star);
            PreviewCapacityBar.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewCapacityBar.Visibility = Visibility.Collapsed;
        }

        PreviewCapacityFreeText.Text = capacity.Free is { } free ? $"空き {free}" : string.Empty;
        PreviewCapacityFreeText.Visibility =
            capacity.Free is null ? Visibility.Collapsed : Visibility.Visible;
        PreviewCapacityPanel.Visibility = Visibility.Visible;
    }

    private void RenderPreview(AppState state)
    {
        var vm = StateProjection.ProjectPreview(state);
        previewFileName = vm.FileName;
        previewOriginalDirectory = vm.OriginalDirectory;
        FitPreviewHeader();

        PreviewMetadataBlock.Text = vm.MetadataText;
        PreviewMetadataBlock.Visibility =
            string.IsNullOrEmpty(vm.MetadataText) ? Visibility.Collapsed : Visibility.Visible;

        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Collapsed;
        PreviewMeta.Visibility = Visibility.Collapsed;
        PreviewCapacityPanel.Visibility = Visibility.Collapsed;

        switch (vm.Kind)
        {
            case PreviewKind.Image:
                if (lastDecodedImageGeneration != vm.Generation)
                {
                    PreviewImage.Source = DecodeImage(vm.ImageBytes);
                    lastDecodedImageGeneration = vm.Generation;
                }

                PreviewImage.Visibility = Visibility.Visible;
                break;

            case PreviewKind.Text:
            case PreviewKind.Binary:
                // Binary's vm.Text is already the label header + hex dump (see
                // StateProjection.ProjectPreview) - the same monospace box as Text needs no
                // special-casing to show it.
                PreviewTextBox.Text = vm.Text ?? string.Empty;
                PreviewTextBox.Visibility = Visibility.Visible;
                break;

            case PreviewKind.Capacity when vm.Capacity is { } capacity:
                RenderCapacity(capacity);
                break;

            case PreviewKind.Loading:
                PreviewMeta.Text = "読み込み中…";
                PreviewMeta.Visibility = Visibility.Visible;
                break;

            case PreviewKind.None:
            default:
                if (vm.Error is not null)
                {
                    PreviewMeta.Text = vm.Error;
                    PreviewMeta.Visibility = Visibility.Visible;
                }

                break;
        }
    }

    /// <summary>
    /// Caps decode resolution (Phase 5 fix P1) so a huge photo cannot make the UI thread's decode
    /// pass itself sluggish - roughly the preview pane's width; <see cref="BitmapImage.DecodePixelWidth"/>
    /// alone (leaving <see cref="BitmapImage.DecodePixelHeight"/> at its default 0) preserves aspect
    /// ratio automatically.
    /// </summary>
    private const int PreviewDecodePixelWidth = 1024;

    /// <summary>
    /// Decodes already-loaded image bytes (from <see cref="Msg.PreviewLoaded"/>, via
    /// <see cref="Effect.LoadPreview"/> - Runtime's job, not this method's) into a frozen
    /// <see cref="BitmapImage"/> so it is safe to hand to the UI thread's <see cref="Image"/>
    /// control from here regardless of which thread posted the underlying <see cref="Msg"/>.
    /// </summary>
    private static BitmapImage DecodeImage(System.Collections.Immutable.ImmutableArray<byte> bytes)
    {
        using var stream = new MemoryStream([.. bytes]);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = PreviewDecodePixelWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
