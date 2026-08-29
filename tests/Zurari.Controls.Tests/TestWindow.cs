using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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

    static TestWindow()
    {
        // Software rendering, process-wide, before the first window exists.
        //
        // These are real WPF windows, so by default they render through the GPU. Under a test run -
        // seven assemblies in parallel, windows created and destroyed in rapid succession - the
        // compositor occasionally dies with UCEERR_RENDERTHREADFAILURE (0x88980406), which takes
        // down the whole test host. That aborts the run partway through the assembly, so it shows
        // up as "テスト実行が中止されました" with *no individual test failing*: every printed summary
        // still says 成功, while `dotnet test` exits non-zero and scripts/check.ps1 reports the
        // useless "Tests failed." It then passes on retry, because nothing about the code was wrong.
        //
        // Nothing here depends on GPU rasterization - the tests assert layout, virtualization and
        // hit-testing, all of which behave identically in software.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }

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
