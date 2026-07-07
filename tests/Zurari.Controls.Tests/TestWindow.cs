using System.Windows;
using System.Windows.Threading;

namespace Zurari.Controls.Tests;

/// <summary>
/// Hosts a control in a real (off-screen) window so templates apply, layout runs
/// and virtualization realizes containers — the closest thing to production WPF
/// behaviour a unit test can get. Requires an STA thread ([StaFact]/[StaTheory]).
/// </summary>
internal sealed class TestWindow : IDisposable
{
    private readonly Window window;

    public TestWindow(FrameworkElement content, double width = 1200, double height = 800)
    {
        window = new Window
        {
            Content = content,
            Width = width,
            Height = height,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = false,
            // Park the window far off-screen so test runs do not flash windows at the user.
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
        };
        window.Show();
        DoEvents();
    }

    /// <summary>Pumps the dispatcher until pending layout/render work has run.</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    public void Dispose()
    {
        window.Close();
        DoEvents();
    }
}
