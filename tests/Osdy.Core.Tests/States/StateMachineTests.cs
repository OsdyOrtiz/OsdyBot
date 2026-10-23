using Osdy.Core.States;

namespace Osdy.Core.Tests.States;

public class StateMachineTests
{
    [Theory]
    [InlineData(BrainState.Idle, BrainState.Curious)]
    [InlineData(BrainState.Idle, BrainState.Attentive)]
    [InlineData(BrainState.Idle, BrainState.Interacting)]
    [InlineData(BrainState.Idle, BrainState.Resting)]
    [InlineData(BrainState.Curious, BrainState.Idle)]
    [InlineData(BrainState.Curious, BrainState.Attentive)]
    [InlineData(BrainState.Curious, BrainState.Interacting)]
    [InlineData(BrainState.Curious, BrainState.Resting)]
    [InlineData(BrainState.Attentive, BrainState.Idle)]
    [InlineData(BrainState.Attentive, BrainState.Interacting)]
    [InlineData(BrainState.Attentive, BrainState.Resting)]
    [InlineData(BrainState.Interacting, BrainState.Idle)]
    [InlineData(BrainState.Interacting, BrainState.Attentive)]
    [InlineData(BrainState.Interacting, BrainState.Resting)]
    [InlineData(BrainState.Resting, BrainState.Idle)]
    [InlineData(BrainState.Resting, BrainState.Attentive)]
    public void Documented_transitions_are_allowed(BrainState from, BrainState to)
    {
        Assert.True(StateMachine.CanTransition(from, to));
        StateMachine.EnsureValid(from, to);
    }

    [Theory]
    [InlineData(BrainState.Resting, BrainState.Curious)]
    [InlineData(BrainState.Resting, BrainState.Interacting)]
    [InlineData(BrainState.Attentive, BrainState.Curious)]
    public void Forbidden_transitions_are_rejected(BrainState from, BrainState to)
    {
        Assert.False(StateMachine.CanTransition(from, to));
        var error = Assert.Throws<InvalidOperationException>(() => StateMachine.EnsureValid(from, to));
        Assert.Contains($"{from} -> {to}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Staying_in_the_same_state_is_always_valid()
    {
        Assert.All(Enum.GetValues<BrainState>(), s => Assert.True(StateMachine.CanTransition(s, s)));
    }

    [Fact]
    public void Only_situational_states_are_transient()
    {
        Assert.Equal(
            [BrainState.Curious, BrainState.Attentive, BrainState.Interacting],
            Enum.GetValues<BrainState>().Where(StateMachine.IsTransient));
    }
}
