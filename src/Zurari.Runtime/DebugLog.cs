using System.Diagnostics;

namespace Zurari.Runtime;

/// <summary>
/// Opt-in, append-only diagnostic log file used by the <c>--debug-input</c> tracing added to
/// track down the touchpad-physical-button-click bug (clicking sometimes moves the cursor but
/// never enters the directory). This is the only place outside <see cref="WorkerRuntime"/> that
/// touches the filesystem — <c>Zurari.App</c> and <c>Zurari.Controls</c> are banned from
/// <see cref="System.IO.File"/> and route through here instead.
/// </summary>
/// <remarks>
/// No-ops silently when not enabled, so call sites in <c>Zurari.Controls</c> (gated by
/// <c>ColumnBrowser.InputTraceEnabled</c>) and <c>Zurari.App</c> never need to check
/// <see cref="IsEnabled"/> themselves before writing.
/// </remarks>
public static class DebugLog
{
    private static readonly object Gate = new();
    private static StreamWriter? writer;
    private static Stopwatch? stopwatch;

    /// <summary>True once <see cref="Enable"/> has successfully opened the log file.</summary>
    public static bool IsEnabled
    {
        get
        {
            lock (Gate)
            {
                return writer is not null;
            }
        }
    }

    /// <summary>
    /// Opens (creating if necessary, appending if it already exists) the log file at
    /// <paramref name="filePath"/> and starts the elapsed-time clock used to prefix every line
    /// written by <see cref="Write"/>. Idempotent — a second call is a no-op, so callers do not
    /// need to guard against being invoked twice (e.g. re-entrant startup paths).
    /// </summary>
    public static void Enable(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        lock (Gate)
        {
            if (writer is not null)
            {
                return;
            }

            // FileShare.ReadWrite (rather than StreamWriter's default FileShare.Read-only-for-
            // others) so the log can be tailed - or read by a test - while this writer keeps the
            // file open for the rest of the process.
            var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            writer = new StreamWriter(stream) { AutoFlush = true };
            stopwatch = Stopwatch.StartNew();
        }

        Write("log enabled");
    }

    /// <summary>
    /// Appends one line to the log file, prefixed with the milliseconds elapsed since
    /// <see cref="Enable"/> was called (formatted "F1"). Thread-safe — worker threads and the UI
    /// thread may call this concurrently. A no-op when <see cref="Enable"/> was never called.
    /// </summary>
    public static void Write(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        lock (Gate)
        {
            if (writer is null || stopwatch is null)
            {
                return;
            }

            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            writer.WriteLine(elapsedMs + " " + line);
        }
    }
}
