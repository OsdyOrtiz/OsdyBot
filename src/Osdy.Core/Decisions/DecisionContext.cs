using Osdy.Core.Events;
using Osdy.Core.Personality;
using Osdy.Core.States;

namespace Osdy.Core.Decisions;

/// <summary>Everything a rule may look at. Rules are pure functions of this context.</summary>
public sealed record DecisionContext(
    DateTimeOffset Now,
    BrainEvent? Event,
    RuntimeState Runtime,
    PersonalityProfile Personality,
    BrainOptions Options)
{
    public BrainState State => Runtime.State;

    public bool IsTick => Event is null;

    public bool IsEvent(EventType type) => Event?.Type == type;

    public double Trait(TraitKind kind) => Personality[kind].Value;

    public TimeSpan TimeInState => Now - Runtime.StateEnteredAt;
}

/// <summary>A rule's choice before the engine stamps identity, time and trigger on it.</summary>
public sealed record Choice(DecisionKind Kind, BehaviorAction Action, BrainState ToState, IReadOnlyList<string> Reasons);

public interface IDecisionRule
{
    string Name { get; }

    Choice? Evaluate(DecisionContext context);
}
