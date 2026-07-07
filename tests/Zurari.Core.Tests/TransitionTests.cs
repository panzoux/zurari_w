using CsCheck;
using Zurari.Core;

namespace Zurari.Core.Tests;

public class TransitionTests
{
    [Fact]
    public void Noop_returns_same_state_and_no_effects()
    {
        var (state, effects) = Transition.Apply(AppState.Initial, new Msg.Noop());

        Assert.Same(AppState.Initial, state);
        Assert.Empty(effects);
    }

    [Fact]
    public void Apply_rejects_nulls()
    {
        Assert.Throws<ArgumentNullException>(() => Transition.Apply(null!, new Msg.Noop()));
        Assert.Throws<ArgumentNullException>(() => Transition.Apply(AppState.Initial, null!));
    }
}

/// <summary>
/// Property-based tests (rwf's proptest equivalent). As Msg variants are added,
/// extend <see cref="GenMsg"/> so every property automatically covers them.
/// </summary>
public class TransitionProperties
{
    private static Gen<Msg> GenMsg => Gen.Const<Msg>(new Msg.Noop());

    [Fact]
    public void Any_msg_sequence_yields_valid_state_and_effects()
    {
        GenMsg.List[0, 50].Sample(msgs =>
        {
            var state = AppState.Initial;
            foreach (var msg in msgs)
            {
                var (next, effects) = Transition.Apply(state, msg);
                Assert.NotNull(next);
                Assert.NotNull(effects);
                state = next;
            }
        });
    }

    [Fact]
    public void Apply_is_deterministic()
    {
        GenMsg.Sample(msg =>
        {
            var first = Transition.Apply(AppState.Initial, msg);
            var second = Transition.Apply(AppState.Initial, msg);
            Assert.Equal(first.State, second.State);
            Assert.Equal(first.Effects, second.Effects);
        });
    }
}
