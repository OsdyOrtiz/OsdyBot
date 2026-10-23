using Osdy.Core.Decisions;
using Osdy.Core.Personality;
using Osdy.Core.States;

namespace Osdy.Core.Experiences;

public enum ExperienceKind
{
    PositiveInteraction,
    NegativeInteraction,
    NoveltyExplored,
}

/// <summary>
/// The brain's interpretation of an observed event: what happened, how it reacted, and how it read the result.
/// </summary>
public sealed record Experience(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid EventId,
    Guid DecisionId,
    ExperienceKind Kind,
    BrainState State,
    BehaviorAction Action,
    double Valence,
    double Importance,
    string Outcome,
    string Source);

/// <summary>A recorded trait change, always linked to the experience and rule that caused it.</summary>
public sealed record TraitChange(
    Guid Id,
    DateTimeOffset OccurredAt,
    TraitKind Trait,
    double OldValue,
    double NewValue,
    double Delta,
    Guid ExperienceId,
    string Rule);
