using Osdy.Core.Decisions;
using Osdy.Core.Personality;
using Osdy.Core.States;

namespace Osdy.Core.Experiences;

public enum ExperienceKind
{
    PositiveInteraction,
    NegativeInteraction,
    NoveltyExplored,
    OwnerRollback,
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

public enum TraitChangeKind
{
    /// <summary>Bounded learning from an experience; it spends the sliding budget.</summary>
    Learning,

    /// <summary>Owner correction that restores recorded values; it never spends the learning budget.</summary>
    Restoration,
}

/// <summary>
/// A recorded trait change, always linked to the experience and rule that caused it. A restoration also names the
/// change the owner rolled back to.
/// </summary>
public sealed record TraitChange(
    Guid Id,
    DateTimeOffset OccurredAt,
    TraitKind Trait,
    double OldValue,
    double NewValue,
    double Delta,
    Guid ExperienceId,
    string Rule,
    TraitChangeKind Kind = TraitChangeKind.Learning,
    Guid? RollbackTargetId = null);
