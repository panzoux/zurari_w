using System.Diagnostics;
using Zurari.Runtime;

namespace Zurari.App;

/// <summary>
/// Forwards <see cref="Trace"/> output — the <c>[input]</c> lines <c>Zurari.Controls.ColumnView</c>
/// emits when <see cref="Zurari.Controls.ColumnBrowser.InputTraceEnabled"/> is set, and the
/// <c>[app]</c> lines <see cref="MainWindow"/> emits from its own handlers — to
/// <see cref="DebugLog"/>, the only place outside <c>Zurari.Runtime</c> allowed to touch the
/// filesystem. Registered only when the process is started with <c>--debug-input</c>; see
/// <see cref="ZurariApp.OnStartup"/>.
/// </summary>
internal sealed class DebugLogTraceListener : TraceListener
{
    /// <inheritdoc />
    public override void Write(string? message)
    {
        if (message is not null)
        {
            DebugLog.Write(message);
        }
    }

    /// <inheritdoc />
    public override void WriteLine(string? message) => DebugLog.Write(message ?? string.Empty);
}
