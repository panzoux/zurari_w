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
