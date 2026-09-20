using Zurari.Core;

namespace Zurari.TestSupport;

/// <summary>
/// Picking apart what <see cref="Transition.Apply"/> returned, without LINQ. A transition hands
/// back a mixed list of effects and nearly every assertion is about the ones of a single kind
/// ("exactly one <c>LoadPreview</c>", "no <c>ReadDirectory</c>"), so that selection lives here
/// rather than being re-spelled as a loop in every test.
/// </summary>
public static class TestEffects
{
    /// <summary>The effects of type <typeparamref name="T"/>, in the order Core emitted them.</summary>
    public static List<T> Of<T>(IEnumerable<Effect> effects)
        where T : Effect
    {
        ArgumentNullException.ThrowIfNull(effects);

        var matched = new List<T>();
        foreach (var effect in effects)
        {
            if (effect is T typed)
            {
                matched.Add(typed);
            }
        }

        return matched;
    }
}

/// <summary>Reading a column's rows back out, without LINQ.</summary>
public static class TestEntries
{
    /// <summary>The names of <paramref name="entries"/>, in order.</summary>
    public static List<string> Names(IEnumerable<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var names = new List<string>();
        foreach (var entry in entries)
        {
            names.Add(entry.Name);
        }

        return names;
    }

    /// <summary>The entries <paramref name="keep"/> accepts, in order.</summary>
    public static List<Entry> Where(IEnumerable<Entry> entries, Func<Entry, bool> keep)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(keep);

        var kept = new List<Entry>();
        foreach (var entry in entries)
        {
            if (keep(entry))
            {
                kept.Add(entry);
            }
        }

        return kept;
    }
}
