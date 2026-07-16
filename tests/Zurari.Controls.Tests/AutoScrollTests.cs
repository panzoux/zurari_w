namespace Zurari.Controls.Tests;

/// <summary>
/// Unit coverage for <see cref="ColumnView.ComputeAutoScrollStep"/>, the pure step function behind
/// rubber-band auto-scroll (R1). The timer loop that calls it (<c>OnAutoScrollTick</c>) needs a
/// live pointer position and is NOT covered here - see the Phase 4 manual checklist; this class
/// only asserts the direction/magnitude math, which is what makes the timer loop itself trivial.
/// </summary>
public class AutoScrollTests
{
    [Fact]
    public void No_scroll_when_the_pointer_is_inside_the_viewport()
    {
        Assert.Equal(0, ColumnView.ComputeAutoScrollStep(pointerY: 50, viewportTop: 0, viewportBottom: 400));
    }

    [Fact]
    public void No_scroll_exactly_at_the_viewport_edges()
    {
        Assert.Equal(0, ColumnView.ComputeAutoScrollStep(pointerY: 0, viewportTop: 0, viewportBottom: 400));
        Assert.Equal(0, ColumnView.ComputeAutoScrollStep(pointerY: 400, viewportTop: 0, viewportBottom: 400));
    }

    [Fact]
    public void Scrolls_up_with_a_negative_step_when_the_pointer_is_above_the_viewport()
    {
        var step = ColumnView.ComputeAutoScrollStep(pointerY: -5, viewportTop: 0, viewportBottom: 400);

        Assert.True(step < 0, $"expected a negative (upward) step, got {step}");
    }

    [Fact]
    public void Scrolls_down_with_a_positive_step_when_the_pointer_is_below_the_viewport()
    {
        var step = ColumnView.ComputeAutoScrollStep(pointerY: 405, viewportTop: 0, viewportBottom: 400);

        Assert.True(step > 0, $"expected a positive (downward) step, got {step}");
    }

    [Fact]
    public void Step_magnitude_grows_with_distance_past_the_edge_up_to_a_cap_of_three()
    {
        var near = ColumnView.ComputeAutoScrollStep(pointerY: 410, viewportTop: 0, viewportBottom: 400);
        var mid = ColumnView.ComputeAutoScrollStep(pointerY: 460, viewportTop: 0, viewportBottom: 400);
        var far = ColumnView.ComputeAutoScrollStep(pointerY: 600, viewportTop: 0, viewportBottom: 400);

        Assert.Equal(1, near);
        Assert.Equal(2, mid);
        Assert.Equal(3, far);
    }

    [Fact]
    public void Step_magnitude_is_symmetric_above_and_below_the_viewport()
    {
        var below = ColumnView.ComputeAutoScrollStep(pointerY: 600, viewportTop: 0, viewportBottom: 400);
        var above = ColumnView.ComputeAutoScrollStep(pointerY: -200, viewportTop: 0, viewportBottom: 400);

        Assert.Equal(below, -above);
    }
}
