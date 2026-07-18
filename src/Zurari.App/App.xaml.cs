using System.Diagnostics;
using System.IO;
using System.Windows;
using Zurari.Controls;
using Zurari.Runtime;

namespace Zurari.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class ZurariApp : Application
{
    static ZurariApp()
    {
        // Precision-touchpad input reaches WPF through the WISP stylus/touch stack, which then
        // promotes it to mouse events. The --debug-input diagnostics proved that a physical
        // touchpad button's WM_LBUTTONUP occasionally never reaches this app at all, while the
        // very next mouse-move already carries the released state - a known-flaky corner of that
        // promotion layer. This switch bypasses WISP so WPF consumes raw Win32 mouse messages
        // directly. Touch-SCREEN manipulation (touch panning) is lost, which this
        // keyboard/mouse-centric app does not use; the in-app recovery (move-observed release +
        // release watchdog in ColumnView) stays as a second line of defense either way.
        AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.DisableStylusAndTouchSupport", true);
    }

    /// <summary>
    /// Command-line switch that turns on the opt-in input-pipeline diagnostic tracing: sets
    /// <see cref="ColumnBrowser.InputTraceEnabled"/>, opens the log file via
    /// <see cref="DebugLog.Enable"/>, and attaches a <see cref="DebugLogTraceListener"/> so every
    /// <see cref="Trace"/> line from the input pipeline is forwarded there. Added to diagnose a bug
    /// where a touchpad's physical left button sometimes selects a row but never enters the
    /// directory. This must run before <see cref="MainWindow"/> is constructed (i.e. before
    /// <see cref="Application.OnStartup"/> triggers the <c>StartupUri</c>-driven window creation)
    /// so startup-time dispatches are captured too.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (Array.IndexOf(e.Args, "--debug-input") >= 0)
        {
            ColumnBrowser.InputTraceEnabled = true;

            // Zurari.App may not touch System.IO.File/Directory directly (BannedSymbols.txt) -
            // Path is allowed, so only the destination STRING is built here; DebugLog (in
            // Zurari.Runtime) does the actual file I/O.
            var logPath = Path.Combine(Path.GetTempPath(), "zurari-input.log");
            DebugLog.Enable(logPath);
            Trace.Listeners.Add(new DebugLogTraceListener());
        }

        base.OnStartup(e);
    }
}
