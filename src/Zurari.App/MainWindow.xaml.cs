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
    /// collapse the very column the drag needs as a drop target. A right click additionally shows
    /// the shell context menu for the entry, blocking synchronously until the user picks a command
    /// or dismisses it, and - if a command was invoked - dispatches <see cref="Msg.Refresh"/> since
    /// the shell may have renamed/deleted/pasted something that this pane needs to reflect.
    /// </summary>
    private void OnEntryPointerPressed(object? sender, EntryPointerPressedEventArgs e)
    {
        loop.Dispatch(new Msg.CursorTo(e.ColumnIndex, e.EntryIndex));

        if (e.Button != MouseButton.Right)
        {
            return;
        }

        var fullPath = ResolveFullPath(e.ColumnIndex, e.EntryIndex);
        if (fullPath is null)
        {
            return;
        }

        var ownerHwnd = new WindowInteropHelper(this).Handle;
        if (ShellContextMenu.Show(ownerHwnd, [fullPath], (int)e.ScreenPosition.X, (int)e.ScreenPosition.Y))
        {
            loop.Dispatch(new Msg.Refresh());
        }
    }

    /// <summary>
    /// Drag-out gesture reported by <see cref="ColumnBrowser"/>: resolves the entry's full path
    /// (drive entries at the virtual root are skipped - dragging a drive letter out means nothing)
    /// and starts the actual OLE drag via <see cref="DragDrop.DoDragDrop"/>, which the control
    /// itself never touches. If the drag ends as a move, the source location may no longer contain
    /// the entry, so a <see cref="Msg.Refresh"/> is dispatched afterward.
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

        var data = new DataObject(DataFormats.FileDrop, new[] { fullPath });
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

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            loop.Dispatch(new Msg.Refresh());
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            TryDeleteFocusedEntry();
            e.Handled = true;
        }
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
        StatusText.Text = focusedPath + $" ({focused.Entries.Length} 件)";
    }
}
