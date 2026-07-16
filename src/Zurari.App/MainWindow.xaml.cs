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

        Browser.CursorMoveRequested += (_, e) => loop.Dispatch(ToCursorMsg(e));
        Browser.ColumnFocusRequested += (_, e) => loop.Dispatch(new Msg.FocusColumn(e.ColumnIndex));
        Browser.EntryActivated += (_, e) => loop.Dispatch(new Msg.EnterDirectory(e.ColumnIndex, e.EntryIndex));
        Browser.NavigateUpRequested += (_, e) => loop.Dispatch(new Msg.GoToParent(e.ColumnIndex));
        Browser.EntryPointerPressed += OnEntryPointerPressed;
        Browser.EntryClicked += (_, e) => loop.Dispatch(new Msg.EnterDirectory(e.ColumnIndex, e.EntryIndex));
        Browser.EntryDragRequested += OnEntryDragRequested;
        Browser.FileDropRequested += (_, e) => loop.Dispatch(
            new Msg.DropFiles(e.ColumnIndex, e.TargetEntryIndex, [.. e.Paths], e.ShiftHeld, e.CtrlHeld));
        Browser.MarkRangeRequested += (_, e) => loop.Dispatch(
            new Msg.MarkRange(e.ColumnIndex, e.FromIndex, e.ToIndex, e.Additive));

        Closed += (_, _) => Dispose();
        Loaded += (_, _) => Browser.Focus();
        KeyDown += OnWindowKeyDown;

        loop.Dispatch(new Msg.Refresh());
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

    private void PostToLoop(Msg msg) => Dispatcher.BeginInvoke(() => loop.Dispatch(msg));

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
    /// Every press just moves the cursor there - directory activation happens on release-without-
    /// drag instead (see <see cref="Zurari.Controls.EntryClickedEventArgs"/>), so a press that
    /// turns into a drag never enters a directory out from under column 3, which would otherwise
    /// collapse the very column the drag needs as a drop target. Ctrl+left instead toggles the
    /// entry's mark (does not move the cursor via <see cref="Msg.CursorTo"/> - <see cref="Msg.ToggleMark"/>
    /// moves it itself). A right click additionally shows the shell context menu - for every marked
    /// entry in the column when the pressed row is marked, otherwise just that entry - blocking
    /// synchronously until the user picks a command or dismisses it, and - if a command was invoked
    /// - dispatches <see cref="Msg.Refresh"/> since the shell may have renamed/deleted/pasted
    /// something that this pane needs to reflect.
    /// </summary>
    private void OnEntryPointerPressed(object? sender, EntryPointerPressedEventArgs e)
    {
        if (e.Button == MouseButton.Left && e.Modifiers == ModifierKeys.Control)
        {
            loop.Dispatch(new Msg.ToggleMark(e.ColumnIndex, e.EntryIndex));
        }
        else
        {
            loop.Dispatch(new Msg.CursorTo(e.ColumnIndex, e.EntryIndex));
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
            loop.Dispatch(new Msg.Refresh());
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
            loop.Dispatch(new Msg.Refresh());
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
            loop.Dispatch(new Msg.Refresh());
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            TryDelete();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
        {
            loop.Dispatch(new Msg.ToggleMarkAtCursor(loop.State.FocusedColumn));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            loop.Dispatch(new Msg.ClearMarks(loop.State.FocusedColumn));
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
                loop.Dispatch(new Msg.DeleteMarked(columnIndex));
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
            loop.Dispatch(new Msg.DeleteEntry(columnIndex, column.Cursor));
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
