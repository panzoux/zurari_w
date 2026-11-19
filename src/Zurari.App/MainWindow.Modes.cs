using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Zurari.Controls;
using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// The mode bar of <see cref="MainWindow"/>: the text box a <c>/</c> search or a Leap is typed into,
/// in the path bar's place. Mirrors <c>Transition.Modes.cs</c> in Core. Nothing is decided here: the
/// text box reports what was typed, <see cref="KeyMap"/> names the keys a mode owns, and the bar is
/// redrawn from <see cref="StateProjection.ProjectModeBar"/>.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly Brush StatusBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

    private static readonly Brush ErrorBrush = Brushes.Red;

    /// <summary>The mode <see cref="RenderModeBar"/> last drew, to notice one opening or closing.</summary>
    private StateProjection.ModeBarKind shownMode;

    /// <summary>
    /// Set while <see cref="RenderModeBar"/> writes the text box, so the <c>TextChanged</c> that write
    /// raises is not reported back as typing.
    /// </summary>
    private bool syncingModeBox;

    private void RenderModeBar(AppState state)
    {
        var bar = StateProjection.ProjectModeBar(state);
        var open = bar.Kind != StateProjection.ModeBarKind.Hidden;
        PathBarText.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        ModeBar.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ModePrefix.Text = bar.Prefix;
        ModeStatus.Text = bar.Status;
        ModeStatus.Foreground = bar.IsError ? ErrorBrush : StatusBrush;

        // Written only when it differs: rewriting what was just typed would move the caret.
        if (!string.Equals(ModeBox.Text, bar.Text, StringComparison.Ordinal))
        {
            syncingModeBox = true;
            try
            {
                ModeBox.Text = bar.Text;
                ModeBox.CaretIndex = bar.Text.Length;
            }
            finally
            {
                syncingModeBox = false;
            }
        }

        if (bar.Kind == shownMode)
        {
            return;
        }

        shownMode = bar.Kind;
        if (open)
        {
            // After the key that opened the mode has finished being handled, so that key is never
            // typed into the box it opened.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(ModeBox));
        }
        else if (ModeBox.IsKeyboardFocusWithin)
        {
            FocusRow(state.FocusedPane);
        }
    }

    private void OnModeBoxTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (syncingModeBox)
        {
            return;
        }

        var pane = loop.State.Focused;
        if (pane.Search is not null)
        {
            Dispatch(new Msg.SearchTextChanged(ModeBox.Text));
        }
        else if (pane.Leap is not null)
        {
            Dispatch(new Msg.LeapTextChanged(ModeBox.Text));
        }
    }

    private void OnModeBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var state = loop.State;
        var key = EffectiveKey(e);
        var msg = state.Focused.Search is not null
            ? KeyMap.ResolveSearchKey(key, Keyboard.Modifiers)
            : state.Focused.Leap is not null
                ? KeyMap.ResolveLeapKey(key, Keyboard.Modifiers, state, ModeBox.Text.Length == 0)
                : null;
        if (ColumnBrowser.InputTraceEnabled)
        {
            Trace.WriteLine(InputTrace.Resolution("modeKey", msg?.ToString() ?? "(typed)"));
        }

        if (msg is not null)
        {
            Dispatch(msg);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Focus moving elsewhere in the window - a click in a pane - confirms the mode where it stands.
    /// Leaving the window (Alt+Tab) does not: there is nowhere new for focus to be (<c>NewFocus</c> is
    /// <c>null</c>), and coming back should find the search still open.
    /// </summary>
    private void OnModeBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is null)
        {
            return;
        }

        var pane = loop.State.Focused;
        if (pane.Search is not null)
        {
            Dispatch(new Msg.EndSearch(Restore: false));
        }
        else if (pane.Leap is not null)
        {
            Dispatch(new Msg.EndLeap(Restore: false));
        }
    }
}
