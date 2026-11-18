using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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

    /// <summary>
    /// Lets a render reuse the row arrays it built last time - see <see cref="ProjectionCache"/>.
    /// One per pane: the cache is keyed by column index, so two panes sharing one would evict each
    /// other's rows on every render.
    /// </summary>
    private readonly Dictionary<PaneId, ProjectionCache> projectionCaches = [];

    /// <summary>
    /// The job strip's rows, bound once in the constructor and reconciled in place on every render -
    /// see <see cref="JobStripRows"/>.
    /// </summary>
    private readonly JobStripRows jobStrip;

    /// <summary>The panes on screen, top row first. A row is a place on screen; which pane it shows changes (swap, close).</summary>
    private readonly PaneView[] rows;

    /// <summary>
    /// What each ROW's <see cref="Controls.ColumnBrowser"/> was most recently handed, by row.
    /// <see cref="Render"/> only reassigns <see cref="Controls.ColumnBrowser.Columns"/> when this
    /// differs from the incoming state's, so unrelated renders (job progress, preview loads, ...)
    /// do not force <see cref="Controls.ColumnBrowser"/> to rebuild/re-sync every column, which
    /// otherwise yanks back any in-progress user scrolling (Phase 5 bug B3).
    /// </summary>
    /// <remarks>
    /// Keyed by row because what changes on screen is the row: a swap moves pane 1 into row 0 with
    /// its columns untouched. The key names the pane (<see cref="StateProjection.RenderKey"/>), so
    /// that counts as a change. A row with no pane holds <c>null</c>, so a pane that opens in it
    /// later is always drawn.
    /// </remarks>
    private readonly StateProjection.RenderKeyValue?[] lastRendered = new StateProjection.RenderKeyValue?[2];

    /// <summary>
    /// How many items <see cref="TrashPlaceRow"/> last saw in the recycle bin. Only
    /// <see cref="ResolveIcon"/>'s cache key, so emptying the bin re-asks the shell for its icon.
    /// Written from a worker thread.
    /// </summary>
    private long trashItemCount;

    /// <summary>
    /// How long <see cref="RunEffect"/> holds the latest <see cref="Effect.LoadPreview"/> before
    /// submitting it (Phase 5 fix P1). Every newer <c>LoadPreview</c> replaces the held one and
    /// restarts its pane's timer (see <see cref="DebouncedGate{T}"/>), so a fast cursor never queues
    /// more than one outstanding preview load - the fix that stops preview I/O from making cursor
    /// movement feel sluggish. Non-preview effects are unaffected; they still flow to their executor
    /// immediately.
    /// </summary>
    private static readonly TimeSpan PreviewDebounceInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// How long to wait after a drive change before re-reading the pane. Longer than the preview
    /// debounce because a volume that has just announced itself is not ready to be enumerated the
    /// instant it does, and because these arrive in bursts rather than one per keystroke.
    /// </summary>
    private static readonly TimeSpan PlacesRefreshDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// One gate (and timer) per pane: a gate holds only the newest effect, so a single shared one
    /// would let a preview request for pane B replace pane A's inside the debounce window, and A
    /// would stay "loading" forever; a shared timer would let steady cursor movement in A keep
    /// restarting it and delay B's held request for as long as A stays busy.
    /// </summary>
    private readonly Dictionary<PaneId, DebouncedGate<Effect.LoadPreview>> previewGates = [];

    /// <summary>
    /// The same coalescing as <see cref="previewGates"/>, for the column that fills in beside the
    /// cursor, per pane (keyed by the column's pane). Held for the same reason and with the same
    /// interval: a held-down arrow key would otherwise put one directory listing on the worker pool
    /// per keystroke, and on a network share that is exactly the stall the preview slot was
    /// separated out to avoid.
    /// </summary>
    private readonly Dictionary<PaneId, DebouncedGate<Effect.ReadDirectory>> speculativeReadGates = [];

    /// <summary>
    /// The pane's gate in <paramref name="map"/>, created on first use. Submits straight to the
    /// <see cref="runtime"/> once its quiet period ends.
    /// </summary>
    private DebouncedGate<T> GateFor<T>(Dictionary<PaneId, DebouncedGate<T>> map, PaneId pane)
        where T : Effect
    {
        if (!map.TryGetValue(pane, out var gate))
        {
            gate = new DebouncedGate<T>(PreviewDebounceInterval, effect => runtime.Submit(effect));
            map.Add(pane, gate);
        }

        return gate;
    }

    private static T For<T>(Dictionary<PaneId, T> map, PaneId pane)
        where T : new()
    {
        if (!map.TryGetValue(pane, out var value))
        {
            value = new T();
            map.Add(pane, value);
        }

        return value;
    }

    /// <summary>
    /// Watches for drives arriving and leaving, so the pane does not need an F5 to notice. Created
    /// once the window has a handle, which is what the shell registers against.
    /// </summary>
    private ShellChangeWatcher? shellWatcher;

    /// <summary>
    /// Coalesces drive notifications. Inserting a disc produces several events in a row, and each
    /// one would otherwise re-read every drive, query the bin and ask the shell for a display name
    /// per drive.
    /// </summary>
    private DispatcherTimer? placesRefreshTimer;

    public MainWindow()
    {
        InitializeComponent();

        jobStrip = new JobStripRows(JobStrip);
        rows = [TopPane];

        runtime = new WorkerRuntime(
            post: PostToLoop, places: RootPlaces, trash: TrashPlaceRow, settings: settingsStore,
            displayName: Zurari.Shell.ShellDisplayName.For,
            shellThumbnail: Zurari.Shell.ShellThumbnailer.TryGetPng);
        shellExecutor = new ShellEffectExecutor(post: PostToLoop);
        jobEngine = new JobEngine(post: PostToLoop);
        directoryWatcher = new DirectoryWatcher(post: PostToLoop);
        loop = new MessageLoop(AppState.Initial, RunEffect, Render);

        for (var row = 0; row < rows.Length; row++)
        {
            WirePane(rows[row], row);
        }

        // Restore the persisted preview-pane width (clamped: the XAML Min/star sizing keeps it
        // on-screen even if the saved value is larger than the current window). Every row gets it,
        // including one that is collapsed right now.
        if (settingsStore.Load().PreviewWidth is { } previewWidth && previewWidth >= 150)
        {
            foreach (var view in rows)
            {
                view.PreviewWidth = Math.Min(previewWidth, 2000);
            }
        }

        Closed += (_, _) => Dispose();
        Loaded += (_, _) => TopPane.ColumnBrowser.Focus();

        // The handle does not exist until here, and the shell registers against a handle.
        SourceInitialized += OnSourceInitialized;

        // The title names the app and its version, nothing more. Where you are is the status bar's
        // job, and having both say it meant the most valuable line in the window was a duplicate.
        //
        // The full name, not "zurari": that is the TUI one, and both can be open at once.
        Title = "zurari for Windows " + AppVersion();

        PreviewKeyDown += OnWindowPreviewKeyDown;
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
    /// Subscribes to everything <paramref name="view"/> raises, with the row it sits in. Every
    /// handler turns that row into a pane at the moment of the event (<see cref="PaneInRow"/>), never
    /// at wiring time: a swap moves panes between rows.
    /// </summary>
    private void WirePane(PaneView view, int row)
    {
        var browser = view.ColumnBrowser;
        browser.CursorMoveRequested += (_, e) => Dispatch(ToCursorMsg(e, RefIn(row, e.ColumnIndex)));
        browser.ColumnFocusRequested += (_, e) => Dispatch(new Msg.FocusColumn(RefIn(row, e.ColumnIndex)));
        browser.EntryActivated += (_, e) => Dispatch(new Msg.EnterDirectory(RefIn(row, e.ColumnIndex), e.EntryIndex));
        browser.NavigateUpRequested += (_, e) => Dispatch(new Msg.GoToParent(RefIn(row, e.ColumnIndex)));
        browser.EntryPointerPressed += (_, e) => OnEntryPointerPressed(row, e);
        browser.EntryClicked += (_, e) => OnEntryClicked(row, e);
        browser.EntryDragRequested += (s, e) => OnEntryDragRequested(row, s, e);
        browser.FileDropRequested += (_, e) => Dispatch(
            new Msg.DropFiles(
                RefIn(row, e.ColumnIndex), e.TargetEntryIndex, [.. e.Paths], e.ShiftHeld, e.CtrlHeld));
        browser.MarkRangeRequested += (_, e) => Dispatch(
            new Msg.MarkRange(RefIn(row, e.ColumnIndex), e.FromIndex, e.ToIndex, e.Additive));
        browser.RubberBandStarted += (_, e) => OnRubberBandStarted(row, e);
        browser.SectionToggleRequested += (_, e) => Dispatch(
            new Msg.ToggleSection(RefIn(row, e.ColumnIndex), e.EntryIndex));
        browser.RenameSubmitted += (_, e) => Dispatch(new Msg.RenameSubmitted(e.NewName));
        browser.RenameCancelled += (_, _) => Dispatch(new Msg.RenameCancelled());

        view.PreviewWidthChanged += OnPreviewWidthChanged;
        view.RetryRequested += (_, _) => Dispatch(new Msg.RetryPreview(PaneInRow(row)));
    }

    /// <summary>
    /// The pane shown in <paramref name="row"/> right now. By position at the moment of the event,
    /// not captured at wiring time: a swap moves panes between rows.
    /// </summary>
    private PaneId PaneInRow(int row) =>
        row < loop.State.Panes.Length ? loop.State.Panes[row].Id : loop.State.Focused.Id;

    /// <summary>Column <paramref name="columnIndex"/> of the pane shown in <paramref name="row"/>. Addresses a pane by its id, never by focus.</summary>
    private ColumnRef RefIn(int row, int columnIndex) => new(PaneInRow(row), columnIndex);

    /// <summary>
    /// The places the drive pane lists above the drives. Resolved here because it needs both layers:
    /// <see cref="Zurari.Shell.KnownFolders"/> to find them (Downloads has no
    /// <c>Environment.SpecialFolder</c> member, so the shell API is the only route) and
    /// <see cref="WorkerRuntime"/> to consume them — and Runtime is not allowed to depend on Shell.
    /// </summary>
    private static List<RootPlace> RootPlaces()
    {
        var places = new List<RootPlace>();
        foreach (var folder in Zurari.Shell.KnownFolders.UserPlaces())
        {
            places.Add(new RootPlace(folder.Path, folder.Label));
        }

        return places;
    }

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

    /// <summary>
    /// Starts watching for drive changes, and routes the shell's notification message to the watcher.
    /// </summary>
    /// <remarks>
    /// If the shell declines to register, nothing here fails - the pane simply keeps needing F5, the
    /// way it did before. That is why the hook is only added when the registration took.
    /// </remarks>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        shellWatcher = new ShellChangeWatcher(source.Handle, ScheduleDrivePaneRefresh);
        if (shellWatcher.IsRegistered)
        {
            source.AddHook(OnWindowMessage);
        }
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (shellWatcher?.HandleMessage(message, wParam, lParam) == true)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Re-reads the drive pane shortly after a drive change, coalescing a burst into one read.
    /// </summary>
    private void ScheduleDrivePaneRefresh()
    {
        if (placesRefreshTimer is null)
        {
            placesRefreshTimer = new DispatcherTimer { Interval = PlacesRefreshDelay };
            placesRefreshTimer.Tick += (_, _) =>
            {
                placesRefreshTimer?.Stop();
                Dispatch(new Msg.PlacesChanged());
            };
        }

        placesRefreshTimer.Stop();
        placesRefreshTimer.Start();
    }

    /// <summary>The assembly's informational version, trimmed of any build metadata suffix.</summary>
    private static string AppVersion()
    {
        var version = typeof(MainWindow).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(MainWindow).Assembly.GetName().Version?.ToString()
            ?? "0.0";

        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }

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
        foreach (var gate in previewGates.Values)
        {
            gate.Stop();
        }

        foreach (var gate in speculativeReadGates.Values)
        {
            gate.Stop();
        }

        placesRefreshTimer?.Stop();
        shellWatcher?.Dispose();
        runtime.Dispose();
        shellExecutor.Dispose();
        jobEngine.Dispose();
        directoryWatcher.Dispose();
    }

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
    /// Holds <paramref name="effect"/> in its pane's gate in <see cref="previewGates"/> (replacing
    /// whatever that pane had pending) and restarts that gate's timer. Only the timer firing ever
    /// actually submits a <see cref="Effect.LoadPreview"/> to <see cref="runtime"/> - this method
    /// never does, which is the coalescing behavior itself (Phase 5 fix P1).
    /// </summary>
    private void SchedulePreviewLoad(Effect.LoadPreview effect) =>
        GateFor(previewGates, effect.Pane).Hold(effect);

    /// <summary>
    /// Holds the newest speculative listing and restarts its pane's timer, exactly as
    /// <see cref="SchedulePreviewLoad"/> does for previews.
    /// </summary>
    private void ScheduleSpeculativeRead(Effect.ReadDirectory effect) =>
        GateFor(speculativeReadGates, effect.At.Pane).Hold(effect);

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
        if (!ColumnBrowser.InputTraceEnabled)
        {
            loop.Dispatch(msg);
            return;
        }

        Trace.WriteLine($"[app] dispatch {msg.GetType().Name}");
        var before = loop.State;
        loop.Dispatch(msg);

        // The state after anything that could change the mode or the view: where a key either took
        // effect in Core or did not.
        var after = loop.State;
        if (after.InputMode != before.InputMode
            || after.Focused.View != before.Focused.View
            || msg is Msg.EnterSortMode or Msg.ExitSortMode or Msg.SetSortMode or Msg.SetSortDescending
                or Msg.ToggleDirectoriesFirst or Msg.ToggleHiddenFiles or Msg.ViewRestored)
        {
            Trace.WriteLine(InputTrace.State(after));
        }
    }

    /// <summary>
    /// A preview splitter drag finished in <paramref name="sender"/>: mirror the width into the other
    /// rows and store it (both panes share one preview width). The width comes from the sender, never
    /// read back from another row: a collapsed row's <c>ActualWidth</c> is 0.
    /// </summary>
    /// <remarks>
    /// Update, not Save: a bare Save writes the whole file, so saving just the width threw away every
    /// pinned place and collapsed section.
    /// </remarks>
    private void OnPreviewWidthChanged(object? sender, PreviewWidthChangedEventArgs e)
    {
        var width = e.Width;
        if (width < 1)
        {
            return;
        }

        foreach (var view in rows)
        {
            if (!ReferenceEquals(view, sender))
            {
                view.PreviewWidth = width;
            }
        }

        settingsStore.Update(s => s with { PreviewWidth = width });
    }

    private static Msg ToCursorMsg(CursorMoveRequestedEventArgs e, ColumnRef at)
    {
        var pageSize = Math.Max(1, e.VisibleRowCount);
        return e.Move switch
        {
            CursorMove.Up => new Msg.CursorUp(at),
            CursorMove.Down => new Msg.CursorDown(at),
            CursorMove.PageUp => new Msg.CursorPageUp(at, pageSize),
            CursorMove.PageDown => new Msg.CursorPageDown(at, pageSize),
            CursorMove.Home => new Msg.CursorHome(at),
            CursorMove.End => new Msg.CursorEnd(at),
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
    private void OnEntryPointerPressed(int row, EntryPointerPressedEventArgs e)
    {
        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine(
                $"[app] OnEntryPointerPressed col={e.ColumnIndex} idx={e.EntryIndex} "
                + $"button={e.Button} modifiers={e.Modifiers}");
        }

        if (!(e.Button == MouseButton.Left && e.Modifiers == ModifierKeys.Control))
        {
            Dispatch(new Msg.CursorTo(RefIn(row, e.ColumnIndex), e.EntryIndex));
        }

        if (e.Button != MouseButton.Right)
        {
            return;
        }

        ShowContextMenu(RefIn(row, e.ColumnIndex), e.EntryIndex, e.ScreenPosition);
    }

    /// <summary>
    /// Opens the shell context menu for one row at <paramref name="screenPosition"/>, on the row's
    /// whole marked set when it is part of one. Does nothing for a row the shell cannot name.
    /// </summary>
    /// <remarks>
    /// Reached both by right-click and by the menu key (see <c>OnWindowKeyDown</c>), so the
    /// two cannot drift apart. Names come from <see cref="ResolveShellName"/> rather than
    /// <see cref="ResolveFullPath"/>: the recycle-bin row has no filesystem path at all, and it is
    /// the row whose menu - "ゴミ箱を空にする" - there is no other way to reach.
    /// </remarks>
    private void ShowContextMenu(ColumnRef at, int entryIndex, Point screenPosition)
    {
        if (ResolveShellName(at, entryIndex) is not { } shellName)
        {
            return;
        }

        // A marked row stands for the shell's whole selection (Column.ShellSelection); a row that is
        // not marked is just itself, whatever else is marked.
        IReadOnlyList<string> paths = [shellName];
        if (IsEntryMarked(at, entryIndex)
            && loop.State.ColumnAt(at)?.ShellSelection() is { IsEmpty: false } selection)
        {
            paths = selection;
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
    private void OnEntryClicked(int row, EntryClickedEventArgs e)
    {
        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine(
                $"[app] OnEntryClicked col={e.ColumnIndex} idx={e.EntryIndex} modifiers={Keyboard.Modifiers}");
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Dispatch(new Msg.ToggleMark(RefIn(row, e.ColumnIndex), e.EntryIndex));
            return;
        }

        Dispatch(new Msg.ClearMarks(RefIn(row, e.ColumnIndex)));
        Dispatch(new Msg.EnterDirectory(RefIn(row, e.ColumnIndex), e.EntryIndex, FocusChild: false));
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
    private void OnRubberBandStarted(int row, RubberBandStartedEventArgs e)
    {
        if (!e.Additive)
        {
            Dispatch(new Msg.ClearMarks(RefIn(row, e.ColumnIndex)));
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
    private void OnEntryDragRequested(int row, object? sender, EntryDragRequestedEventArgs e)
    {
        var at = RefIn(row, e.ColumnIndex);
        if (loop.State.ColumnAt(at) is not { } column || column.Location.FilesystemPath is null)
        {
            return;
        }

        var fullPath = ResolveFullPath(at, e.EntryIndex);
        if (fullPath is null)
        {
            return;
        }

        var paths = IsEntryMarked(at, e.EntryIndex) ? DragSelection(at) : [fullPath];
        if (paths.Count == 0)
        {
            paths = [fullPath];
        }

        var data = new DataObject(DataFormats.FileDrop, paths.ToArray());
        var result = DragDrop.DoDragDrop(sender as DependencyObject ?? rows[row].ColumnBrowser, data, DragDropEffects.Copy | DragDropEffects.Move);
        if (result == DragDropEffects.Move)
        {
            Dispatch(new Msg.Refresh());
        }
    }

    /// <summary>
    /// Resolves the full filesystem path of an entry the same way <c>Transition</c> does
    /// (<see cref="Column.PathOf"/>). Returns <c>null</c> if the indices no longer match the
    /// current state (e.g. a race with a directory reload).
    /// </summary>
    private string? ResolveFullPath(ColumnRef at, int entryIndex)
    {
        if (loop.State.ColumnAt(at) is not { } column)
        {
            return null;
        }

        if (entryIndex < 0 || entryIndex >= column.Entries.Length)
        {
            return null;
        }

        return column.PathOf(column.Entries[entryIndex]);
    }

    /// <summary>
    /// What the shell namespace calls the row at these indices - a filesystem path for almost
    /// everything, the bin's CLSID for the ゴミ箱 row. <c>null</c> when the indices no longer match
    /// the current state, or the row is not something the shell can name at all (a section header).
    /// </summary>
    private string? ResolveShellName(ColumnRef at, int entryIndex)
    {
        if (loop.State.ColumnAt(at) is not { } column)
        {
            return null;
        }

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

    private bool IsEntryMarked(ColumnRef at, int entryIndex)
    {
        if (loop.State.ColumnAt(at) is not { } column)
        {
            return false;
        }

        return entryIndex >= 0 && entryIndex < column.Entries.Length && column.Entries[entryIndex].IsMarked;
    }

    /// <summary>
    /// The paths a drag carries: Core's <see cref="Column.PathsToActOn"/>, the same set as Ctrl+C
    /// and <c>c</c>. A drag is a file operation by path, so it needs real filesystem paths.
    /// </summary>
    private List<string> DragSelection(ColumnRef at)
    {
        var result = new List<string>();
        if (loop.State.ColumnAt(at) is { } column)
        {
            result.AddRange(column.PathsToActOn());
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
        var at = state.Focused.FocusedRef;
        if (state.Focused.FocusedColumnOrNull is not { } focusedColumn)
        {
            return;
        }

        var cursor = focusedColumn.Cursor;
        if (cursor < 0 || TopPane.ColumnBrowser.TryGetRowScreenRect(at.Index, cursor) is not { } rowRect)
        {
            return;
        }

        ShowContextMenu(at, cursor, new Point(rowRect.Left, rowRect.Bottom));
    }

    /// <summary>
    /// While sort mode is open, sees every key before the column list does - otherwise an arrow key
    /// would move the cursor with the mode still open. A key that is not a sort key closes the mode
    /// and carries on to wherever it was going: MessageLoop applies the close synchronously, so by
    /// the time the same key bubbles back up it is read in normal mode.
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ColumnBrowser.InputTraceEnabled)
        {
            TraceKey("previewKey", e);
        }

        if (loop.State.InputMode != Zurari.Core.InputMode.Sort || IsEditingText())
        {
            return;
        }

        var outcome = KeyMap.ResolveSortMode(EffectiveKey(e), Keyboard.Modifiers);
        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine(InputTrace.Resolution("previewKey", outcome.ToString()));
        }

        switch (outcome)
        {
            case KeyMap.SortKeyOutcome.Act act:
                Dispatch(act.Msg);
                e.Handled = true;
                break;
            case KeyMap.SortKeyOutcome.Close:
                Dispatch(new Msg.ExitSortMode());
                break;
        }
    }

    /// <summary>
    /// The key an event is really about. With an IME on - the normal state on a Japanese system - a
    /// letter arrives as <see cref="Key.ImeProcessed"/>, and with Alt held as <see cref="Key.System"/>;
    /// the key actually pressed travels separately.
    /// </summary>
    private static Key EffectiveKey(KeyEventArgs e) => e.Key switch
    {
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.System => e.SystemKey,
        _ => e.Key,
    };

    /// <summary>
    /// Diagnostic-only: one line per key event a window handler receives, written before any
    /// decision is made, so a key that arrives and is then refused still shows up.
    /// </summary>
    private void TraceKey(string phase, KeyEventArgs e) =>
        Trace.WriteLine(InputTrace.KeyEvent(
            phase,
            e.Key,
            EffectiveKey(e),
            Keyboard.Modifiers,
            loop.State.InputMode,
            Keyboard.FocusedElement?.GetType().Name,
            IsEditingText(),
            e.Handled));

    /// <summary>
    /// Whether an editable text box - the rename editor - has keyboard focus. While it does, every key
    /// belongs to it: acting here would toggle a mark on Space, or open sort mode on S, in the middle
    /// of typing a name. The read-only preview text box is left out on purpose, so the window keys
    /// keep working after clicking into a preview.
    /// </summary>
    private static bool IsEditingText() =>
        Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase { IsReadOnly: false };

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (ColumnBrowser.InputTraceEnabled)
        {
            TraceKey("key", e);
        }

        if (IsEditingText())
        {
            return;
        }

        var normal = KeyMap.ResolveNormal(EffectiveKey(e), Keyboard.Modifiers, loop.State);
        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine(InputTrace.Resolution("key", normal?.ToString() ?? "(not a mode key)"));
        }

        if (normal is { } modeMessage)
        {
            Dispatch(modeMessage);
            e.Handled = true;
            return;
        }

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
            Dispatch(new Msg.ToggleMarkAtCursor(loop.State.Focused.FocusedRef));
            e.Handled = true;
        }
        else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+B adds the folder the cursor is pointing at, without having to enter it. The
            // gesture that matters once several columns are open and the drive pane is off-screen.
            Dispatch(new Msg.PinEntryAtCursor(loop.State.Focused.FocusedRef));
            e.Handled = true;
        }
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+D pins the location this column is showing into the drive pane, the way a browser
            // bookmarks the current page. Delete on the pinned row removes it again.
            Dispatch(new Msg.PinFocusedLocation(loop.State.Focused.FocusedRef));
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None)
        {
            // F2 is Explorer's own, so it needs no explaining.
            Dispatch(new Msg.RenameRequested(loop.State.Focused.FocusedRef));
            e.Handled = true;
        }
        else if (e.Key == Key.N && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            // Ctrl+Shift+N is Explorer's own, so it needs no explaining.
            Dispatch(new Msg.CreateFolderRequested(loop.State.Focused.FocusedRef));
            e.Handled = true;
        }
        else if (e.Key is Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
        {
            ShowContextMenuAtCursor();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Dispatch(new Msg.ClearMarks(loop.State.Focused.FocusedRef));
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
        if (loop.State.Focused.FocusedColumnOrNull is not { } column)
        {
            return;
        }

        // The same rule as `c`/`m` (Core's PathsToActOn): the marks, else the cursor entry, and
        // nothing at all for a column that is not a real folder.
        var paths = column.PathsToActOn();
        if (paths.IsEmpty)
        {
            return;
        }

        // A failed placement (a transient clipboard-ownership failure) also skips the cut-pending
        // dispatch below: dimming rows for a cut that never actually reached the clipboard would be
        // misleading.
        if (!FileClipboard.TrySet(paths, isMove))
        {
            return;
        }

        Dispatch(new Msg.SetCutPending(isMove ? paths : []));
    }

    /// <summary>
    /// Ctrl+V: reads the file list back off the clipboard (<see cref="FileClipboard.TryGet"/>) and
    /// dispatches <see cref="Msg.PasteRequested"/> for the focused column 1:1. No-op if the clipboard
    /// holds nothing usable.
    /// </summary>
    private void TryPasteFromClipboard()
    {
        if (FileClipboard.TryGet(out var paths, out var isMove))
        {
            Dispatch(new Msg.PasteRequested(loop.State.Focused.FocusedRef, paths, isMove));
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
        var columnIndex = state.Focused.FocusedColumn;
        var column = state.Focused.Columns[columnIndex];

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
                Dispatch(new Msg.DeleteMarked(state.Focused.Ref(columnIndex), permanent));
            }

            return;
        }

        TryDeleteFocusedEntry(permanent);
    }

    private void TryDeleteFocusedEntry(bool permanent)
    {
        var state = loop.State;
        var columnIndex = state.Focused.FocusedColumn;
        var column = state.Focused.Columns[columnIndex];
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
            Dispatch(new Msg.DeleteEntry(state.Focused.Ref(columnIndex), column.Cursor, permanent));
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
        // Render state.Panes[i] into row i. A row with no pane (the split is closed) is cleared in
        // DropGonePanes, below.
        var visibleRows = Math.Min(state.Panes.Length, rows.Length);
        var watchedChanged = false;
        for (var row = 0; row < visibleRows; row++)
        {
            watchedChanged |= RenderPane(state, row);
        }

        watchedChanged |= DropGonePanes(state);

        // Cheap reconcile (see DirectoryWatcher.SetWatchedPaths' remarks) - only worth doing when
        // some pane's columns actually changed (or a pane went away), the same guard as the
        // Browser.Columns reassignment, so an unrelated render (job progress, preview loads, ...)
        // does not churn watchers.
        if (watchedChanged)
        {
            directoryWatcher.SetWatchedPaths(StateProjection.WatchedPaths(state));
        }

        jobStrip.Reconcile(StateProjection.ProjectJobs(state));
        for (var row = 0; row < visibleRows; row++)
        {
            var pane = state.Panes[row];
            rows[row].RenderPreview(pane.Id, StateProjection.ProjectPreview(state, pane));
        }

        RenderRename(state);

        var statusBar = StateProjection.ProjectStatusBar(state);
        PathBarText.Text = statusBar.Path;
        StatusHints.Text = statusBar.Hints;
        StatusSummary.Text = statusBar.Summary;
    }

    /// <summary>
    /// Pushes the columns of the pane shown in <paramref name="row"/> into that row's browser, unless
    /// nothing that feeds the projection changed since the row was last drawn. Returns whether it did.
    /// </summary>
    private bool RenderPane(AppState state, int row)
    {
        var pane = state.Panes[row];
        var view = rows[row];

        // Reference comparison, not value comparison: Transition returns the very same
        // ImmutableArray<Column> instance (same underlying array) whenever a Msg does not touch the
        // pane's Columns at all (JobProgress/JobFailed/JobCancelRequested/JobDismissed, and the
        // untouched columns inside ShellOpCompleted/JobFinished's per-column loop). Skipping the
        // reassignment in that case is what stops Controls.ColumnBrowser.RebuildColumns and
        // ColumnView.SyncFromColumn from running - and yanking back user scroll/hover state - on
        // renders that have nothing to do with the browsed columns (Phase 5 bug B3).
        var rendered = StateProjection.RenderKey(state, pane);
        if (lastRendered[row] == rendered)
        {
            return false;
        }

        var projected = StateProjection.Project(state, pane, ResolveIcon, For(projectionCaches, pane.Id));
        view.ColumnBrowser.Columns = projected;

        // What the screen was handed, as opposed to what Core holds. The two can only disagree
        // through the projection cache, and this is the line that would show it.
        if (ColumnBrowser.InputTraceEnabled && pane.FocusedColumn >= 0 && pane.FocusedColumn < projected.Count)
        {
            Trace.WriteLine(
                $"[app] render focused={pane.FocusedColumn} "
                + $"shown=[{InputTrace.FirstNames(projected[pane.FocusedColumn].Entries, vm => vm.Name)}]");
        }

        lastRendered[row] = rendered;
        return true;
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

    /// <summary>
    /// Shows or hides the rename editor in every row from whatever the state says, on every render.
    /// </summary>
    /// <remarks>
    /// A projection like everything else (<see cref="StateProjection.ProjectRename"/> decides): the
    /// editor exists because <see cref="AppState.Rename"/> says a row is being renamed, and goes away
    /// when it stops saying so. Nothing here decides anything - the control raises what the user did,
    /// and Core decides what that meant.
    /// </remarks>
    private void RenderRename(AppState state)
    {
        RenameState? stale = null;
        var shown = false;
        for (var row = 0; row < rows.Length; row++)
        {
            var view = row < state.Panes.Length
                ? StateProjection.ProjectRename(state, state.Panes[row])
                : new StateProjection.RenameView.Hidden();
            var browser = rows[row].ColumnBrowser;
            switch (view)
            {
                case StateProjection.RenameView.Shown editor:
                    browser.SyncRenameEditor(editor.ColumnIndex, editor.EntryIndex, editor.Label, editor.Error);
                    shown = true;
                    break;
                case StateProjection.RenameView.Stale staleView:
                    browser.SyncRenameEditor(-1, -1, null, null);
                    stale = staleView.Rename;
                    break;
                default:
                    browser.SyncRenameEditor(-1, -1, null, null);
                    break;
            }
        }

        if (stale is { } staleRename)
        {
            PostRenameCancel(staleRename);
        }
        else if (shown || state.Rename is null)
        {
            cancelPostedFor = null;
        }
    }

    /// <summary>The stale rename a cancel was last posted for, so a render posts it at most once.</summary>
    private RenameState? cancelPostedFor;

    /// <summary>
    /// Cancels a rename that no longer matches the screen. Posted rather than dispatched: a nested
    /// dispatch re-renders inside <see cref="Render"/>, and the outer render would then write its
    /// older state over the newer one (a stale status line).
    /// </summary>
    private void PostRenameCancel(RenameState stale)
    {
        if (cancelPostedFor == stale)
        {
            return;
        }

        cancelPostedFor = stale;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (loop.State.Rename == stale)
                {
                    Dispatch(new Msg.RenameCancelled());
                }
            },
            DispatcherPriority.Background);
    }
}
