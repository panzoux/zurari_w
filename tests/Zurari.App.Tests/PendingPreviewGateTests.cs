using Zurari.App;
using Zurari.Core;

namespace Zurari.App.Tests;

/// <summary>
/// Unit tests for the "replace pending + latest wins" bookkeeping behind
/// <see cref="MainWindow"/>'s preview-load debounce (Phase 5 fix P1). The timer itself (when it
/// actually fires) is not exercised here - see the CLAUDE.md-tracked plan's manual-verification
/// note - only the coalescing logic <see cref="MainWindow.SchedulePreviewLoad"/> drives it with.
/// </summary>
public class PendingPreviewGateTests
{
    [Fact]
    public void Take_with_nothing_held_returns_null()
    {
        var gate = new PendingPreviewGate();

        Assert.Null(gate.Take());
    }

    [Fact]
    public void Take_returns_the_single_held_effect()
    {
        var gate = new PendingPreviewGate();
        var effect = new Effect.LoadPreview(1, @"C:\a.txt");

        gate.Hold(effect);

        Assert.Same(effect, gate.Take());
    }

    [Fact]
    public void A_second_Hold_replaces_the_first_so_only_the_latest_is_ever_taken()
    {
        var gate = new PendingPreviewGate();
        var first = new Effect.LoadPreview(1, @"C:\a.txt");
        var second = new Effect.LoadPreview(2, @"C:\b.txt");

        gate.Hold(first);
        gate.Hold(second);

        Assert.Same(second, gate.Take());
    }

    [Fact]
    public void Take_clears_the_held_effect_so_a_second_Take_returns_null()
    {
        var gate = new PendingPreviewGate();
        gate.Hold(new Effect.LoadPreview(1, @"C:\a.txt"));

        gate.Take();

        Assert.Null(gate.Take());
    }

    [Fact]
    public void Hold_rejects_null()
    {
        var gate = new PendingPreviewGate();

        Assert.Throws<ArgumentNullException>(() => gate.Hold(null!));
    }
}
