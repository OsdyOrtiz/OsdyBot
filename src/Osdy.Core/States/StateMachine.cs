namespace Osdy.Core.States;

/// <summary>The explicit transition table from docs/execution-loop.md.</summary>
public static class StateMachine
{
    private static readonly Dictionary<BrainState, BrainState[]> Allowed = new()
    {
        [BrainState.Idle] = [BrainState.Curious, BrainState.Attentive, BrainState.Interacting, BrainState.Resting],
        [BrainState.Curious] = [BrainState.Idle, BrainState.Attentive, BrainState.Interacting, BrainState.Resting],
        [BrainState.Attentive] = [BrainState.Idle, BrainState.Interacting, BrainState.Resting],
        [BrainState.Interacting] = [BrainState.Idle, BrainState.Attentive, BrainState.Resting],
        [BrainState.Resting] = [BrainState.Idle, BrainState.Attentive],
    };

    /// <summary>Staying in the same state is always allowed and is not a transition.</summary>
    public static bool CanTransition(BrainState from, BrainState to) => from == to || Allowed[from].Contains(to);

    public static void EnsureValid(BrainState from, BrainState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Invalid state transition {from} -> {to}.");
        }
    }

    /// <summary>States that depend on a situation that does not survive a restart.</summary>
    public static bool IsTransient(BrainState state) =>
        state is BrainState.Curious or BrainState.Attentive or BrainState.Interacting;
}
