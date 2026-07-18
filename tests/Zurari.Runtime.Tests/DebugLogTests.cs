using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

/// <summary>
/// <see cref="DebugLog"/> is a process-wide static (it backs the opt-in --debug-input tracing
/// started once at app startup), so these tests must tolerate another test in this class having
/// already called <see cref="DebugLog.Enable"/> first — xunit does not guarantee method order
/// within a class. The "still disabled" test degrades to a no-op check in that case; the "enable
/// and write" test always has something to assert regardless of ordering.
/// </summary>
public class DebugLogTests
{
    /// <summary>
    /// Reads the whole file with FileShare.ReadWrite - DebugLog's own writer keeps the file open
    /// with FileShare.ReadWrite for the rest of the process, and (at least in this environment)
    /// a reader must request the same share flags to be let in; <see cref="File.ReadAllLines"/>'s
    /// FileShare.Read-only default is refused with an IOException even though the writer allows
    /// concurrent reads.
    /// </summary>
    private static string[] ReadAllLinesWhileOpenForWrite(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [Fact]
    public void Write_before_Enable_does_not_throw_when_still_disabled()
    {
        if (DebugLog.IsEnabled)
        {
            // Another test in this process already called Enable (process-wide static state) -
            // nothing left to verify about the disabled path here.
            return;
        }

        DebugLog.Write("should be a silent no-op");
    }

    [Fact]
    public void Enable_then_Write_appends_elapsed_prefixed_lines_to_the_file()
    {
        // DebugLog.Enable keeps the file open for the rest of the process (by design - it backs a
        // process-lifetime diagnostic log, never explicitly closed), so this test cannot delete
        // its temp file afterward like a normal fixture; it is left behind under %TEMP%.
        var path = Path.Combine(Path.GetTempPath(), "zurari-debuglog-test-" + Guid.NewGuid() + ".log");

        DebugLog.Enable(path);
        DebugLog.Write("hello from test");

        Assert.True(DebugLog.IsEnabled);
        Assert.True(File.Exists(path));

        var lines = ReadAllLinesWhileOpenForWrite(path);
        Assert.True(lines.Length >= 2, "expected at least the 'log enabled' line and the test line");

        // Every line starts with "<elapsed-ms F1> " - e.g. "0.0 log enabled".
        foreach (var line in lines)
        {
            var spaceIndex = line.IndexOf(' ', StringComparison.Ordinal);
            Assert.True(spaceIndex > 0, $"line missing elapsed-ms prefix: '{line}'");
            Assert.True(
                double.TryParse(
                    line[..spaceIndex],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _),
                $"prefix is not a number: '{line}'");
        }

        Assert.Contains(lines, l => l.EndsWith("log enabled", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.EndsWith("hello from test", StringComparison.Ordinal));

        // A second Enable() call is idempotent - it must not reopen the file or throw.
        DebugLog.Enable(path);
        DebugLog.Write("after second enable");
        var linesAfter = ReadAllLinesWhileOpenForWrite(path);
        Assert.Contains(linesAfter, l => l.EndsWith("after second enable", StringComparison.Ordinal));
    }
}
