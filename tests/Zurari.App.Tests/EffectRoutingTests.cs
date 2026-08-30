using System.Reflection;
using System.Runtime.CompilerServices;
using Zurari.App;
using Zurari.Core;

namespace Zurari.App.Tests;

/// <summary>
/// Every <see cref="Effect"/> must reach an executor.
/// </summary>
/// <remarks>
/// The composition root used to route effects with a switch statement, and a missing case simply
/// fell through and did nothing. Effect.SetPinned and Effect.CancelPreview were both unrouted for
/// several commits: adding or removing a place did nothing at all in the running app, and preview
/// cancellation never reached the runtime. No test noticed, because tests submit effects straight
/// to the executor they are about and never go through the router.
///
/// This walks the Effect hierarchy by reflection rather than a hand-written list, so a new effect
/// is covered the moment it exists.
/// </remarks>
public class EffectRoutingTests
{
    private static IEnumerable<Type> AllEffectTypes() =>
        typeof(Effect).GetNestedTypes(BindingFlags.Public)
            .Where(t => typeof(Effect).IsAssignableFrom(t) && !t.IsAbstract);

    [Fact]
    public void Every_effect_has_an_executor()
    {
        var unrouted = new List<string>();

        foreach (var type in AllEffectTypes())
        {
            // Built without running a constructor, so no per-effect argument list is needed here -
            // routing only looks at the type.
            var effect = (Effect)RuntimeHelpers.GetUninitializedObject(type);
            try
            {
                _ = EffectRouting.For(effect);
            }
            catch (NotSupportedException)
            {
                unrouted.Add(type.Name);
            }
        }

        Assert.Empty(unrouted);
    }

    [Fact]
    public void The_hierarchy_is_not_empty_so_the_walk_actually_checks_something()
    {
        // Guards the guard: a reflection query that silently matched nothing would pass the test
        // above no matter how many effects went unrouted.
        Assert.True(AllEffectTypes().Count() >= 9);
    }

    /// <summary>
    /// The walk above is exhaustive, not merely broad.
    /// </summary>
    /// <remarks>
    /// <see cref="Effect"/>'s constructor is <c>private protected</c>, so nothing outside Core can
    /// derive from it: the nested types *are* the complete set. That is what makes enumerating them
    /// equivalent to enumerating every effect that can ever be dispatched.
    /// </remarks>
    [Fact]
    public void The_effect_hierarchy_is_closed()
    {
        // Excluding the compiler-generated copy constructor, which every record has and which is
        // protected by definition - it exists to serve `with`, not to open the hierarchy.
        var declared = typeof(Effect)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(c => c.GetParameters() is not [{ ParameterType: var p }] || p != typeof(Effect))
            .ToList();

        Assert.NotEmpty(declared);
        Assert.All(declared, c => Assert.True(c.IsFamilyAndAssembly || c.IsPrivate));
    }
}
