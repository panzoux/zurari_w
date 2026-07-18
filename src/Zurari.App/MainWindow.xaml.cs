using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
    private readonly MessageLoop loop;
    private readonly ShellIconCache iconCache = new();

    public MainWindow()
    {
        InitializeComponent();

        runtime = new WorkerRuntime(post: PostToLoop);
        shellExecutor = new ShellEffectExecutor(post: PostToLoop);
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

        Closed += (_, _) => Dispose();
        Loaded += (_, _) => Browser.Focus();
        KeyDown += OnWindowKeyDown;

        Dispatch(new Msg.Refresh());
    }

    /// <summary>Disposes the <see cref="WorkerRuntime"/> and <see cref="ShellEffectExecutor"/> owned by this window.</summary>
    public void Dispose()
    {
        runtime.Dispose();
        shellExecutor.Dispose();
    }

    /// <summary>
    /// Routes an <see cref="Effect"/> to the executor that can perform it: filesystem effects go
    /// to <see cref="WorkerRuntime"/>, shell effects go to <see cref="ShellEffectExecutor"/>. Pure
    /// dispatch by effect type — no decisions beyond "which executor".
    /// </summary>
    private void RunEffect(Effect effect)
    {
        switch (effect)
        {
            case Effect.ReadDirectory _:
                runtime.Submit(effect);
                break;
            case Effect.DeleteToRecycleBin _:
            case Effect.ShellCopyOrMove _:
                shellExecutor.Submit(effect);
                break;
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

        var ownerHwnd = new WindowInteropHelper(this).Handle;
        if (ShellContextMenu.Show(ownerHwnd, paths, (int)e.ScreenPosition.X, (int)e.ScreenPosition.Y))
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

        if (state.Columns[e.ColumnIndex].Path.Length == 0)
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
        return column.Path.Length == 0 ? entry.Name : System.IO.Path.Combine(column.Path, entry.Name);
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
                result.Add(column.Path.Length == 0 ? entry.Name : System.IO.Path.Combine(column.Path, entry.Name));
            }
        }

        return result;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            Dispatch(new Msg.Refresh());
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            TryDelete();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
        {
            Dispatch(new Msg.ToggleMarkAtCursor(loop.State.FocusedColumn));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Dispatch(new Msg.ClearMarks(loop.State.FocusedColumn));
            e.Handled = true;
        }
    }

    /// <summary>
    /// Deletes the marked entries of the focused column when it has any (Drives are never
    /// eligible, so a column with only a marked drive falls through to the single-entry path
    /// below); otherwise falls back to deleting just the cursor entry, as before marks existed.
    /// </summary>
    private void TryDelete()
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
                $"選択した {markedCount} 件をゴミ箱に移動しますか?",
                "削除の確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (markedResult == MessageBoxResult.Yes)
            {
                Dispatch(new Msg.DeleteMarked(columnIndex));
            }

            return;
        }

        TryDeleteFocusedEntry();
    }

    private void TryDeleteFocusedEntry()
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
            $"{entry.Name} をゴミ箱に移動しますか?",
            "削除の確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            Dispatch(new Msg.DeleteEntry(columnIndex, column.Cursor));
        }
    }

    private void Render(AppState state)
    {
        Browser.Columns = StateProjection.Project(state, e => iconCache.GetIcon(e.Kind, e.Name));

        var focused = state.Columns[state.FocusedColumn];
        var focusedPath = focused.Path.Length == 0 ? "ドライブ" : focused.Path;
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

        StatusText.Text = statusText;
    }
}
