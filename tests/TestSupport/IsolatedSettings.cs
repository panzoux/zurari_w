using System.IO;

namespace Zurari.TestSupport;

/// <summary>
/// A settings directory no other test and no real user shares, for tests that build a
/// <c>WorkerRuntime</c>.
/// </summary>
/// <remarks>
/// Left to its default, <c>WorkerRuntime</c> opens the real per-user <c>settings.json</c>: it reads the
/// user's pinned places and collapsed sections, and writes pins, collapses and sort order back. A pin
/// made in the app turned up as an unexpected favorites section in tests that expect drives only - and
/// the writes went the other way. The directory is only named here, not created: the store creates it
/// on its first save, so a test that never saves leaves nothing behind.
/// </remarks>
public static class IsolatedSettings
{
    /// <summary>A fresh, not yet existing directory under the system temp folder.</summary>
    public static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "zurari_w-settings-" + Guid.NewGuid().ToString("N"));
}
