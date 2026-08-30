using Zurari.Core;
using Zurari.Shell;

namespace Zurari.Shell.Tests;

/// <summary>
/// Touches the real shell. Asserts what is true regardless of what is in the bin on this machine -
/// a listing that depends on the tester's recycle bin would be a coin toss.
/// </summary>
public class RecycleBinFolderTests
{
    [Fact]
    public void Query_returns_plausible_totals_without_throwing()
    {
        var summary = RecycleBinFolder.Query();

        Assert.True(summary.ItemCount >= 0);
        Assert.True(summary.TotalBytes >= 0);

        // An empty bin has no size; a non-empty one generally does. Stated as an implication so the
        // test holds either way.
        if (summary.ItemCount == 0)
        {
            Assert.Equal(0, summary.TotalBytes);
        }
    }

    [Fact]
    public void Enumerate_returns_a_usable_list_without_throwing()
    {
        var names = RecycleBinFolder.Enumerate();

        Assert.NotNull(names);
        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
    }

    /// <summary>
    /// The two use entirely different shell APIs, so agreement is real evidence the enumeration is
    /// talking to the bin rather than quietly returning nothing.
    /// </summary>
    /// <remarks>
    /// Stated as "non-empty implies at least one row" rather than an exact count, so a deletion
    /// landing between the two calls cannot make it flake. The empty case is asserted exactly,
    /// because that direction is where a silently-failing enumeration would hide.
    /// </remarks>
    [Fact]
    public void Enumerate_agrees_with_Query_on_whether_anything_is_there()
    {
        var summary = RecycleBinFolder.Query();
        var names = RecycleBinFolder.Enumerate();

        if (summary.ItemCount == 0)
        {
            Assert.Empty(names);
        }
        else
        {
            Assert.NotEmpty(names);
        }
    }

    [Fact]
    public void Repeated_enumeration_does_not_leak_or_change_its_answer()
    {
        // Each pass releases its COM objects; a mistake there tends to show up as a second call
        // behaving differently from the first.
        var first = RecycleBinFolder.Enumerate();
        var second = RecycleBinFolder.Enumerate();

        Assert.Equal(first, second);
    }
}
