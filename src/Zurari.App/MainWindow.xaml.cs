using System.Windows;
using System.Windows.Input;
using Zurari.Controls;
using Zurari.Core;
using Zurari.Runtime;

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
    private readonly MessageLoop loop;

    public MainWindow()
    {
        InitializeComponent();

        runtime = new WorkerRuntime(post: PostToLoop);
        loop = new MessageLoop(AppState.Initial, runtime.Submit, Render);

        Browser.CursorMoveRequested += (_, e) => loop.Dispatch(ToCursorMsg(e));
        Browser.ColumnFocusRequested += (_, e) => loop.Dispatch(new Msg.FocusColumn(e.ColumnIndex));
        Browser.EntryActivated += (_, e) => loop.Dispatch(new Msg.EnterDirectory(e.ColumnIndex, e.EntryIndex));
        Browser.NavigateUpRequested += (_, e) => loop.Dispatch(new Msg.GoToParent(e.ColumnIndex));
        Browser.EntryPointerPressed += (_, e) => loop.Dispatch(new Msg.CursorTo(e.ColumnIndex, e.EntryIndex));

        Closed += (_, _) => Dispose();
        Loaded += (_, _) => Browser.Focus();
        KeyDown += OnWindowKeyDown;

        loop.Dispatch(new Msg.Refresh());
    }

    /// <summary>Disposes the <see cref="WorkerRuntime"/> owned by this window.</summary>
    public void Dispose() => runtime.Dispose();

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

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            loop.Dispatch(new Msg.Refresh());
            e.Handled = true;
        }
    }

    private void Render(AppState state)
    {
        Browser.Columns = StateProjection.Project(state);

        var focused = state.Columns[state.FocusedColumn];
        var focusedPath = focused.Path.Length == 0 ? "ドライブ" : focused.Path;
        Title = "zurari — " + focusedPath;
        StatusText.Text = focusedPath + $" ({focused.Entries.Length} 件)";
    }
}
