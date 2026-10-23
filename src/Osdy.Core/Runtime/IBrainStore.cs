using Osdy.Core.Decisions;
using Osdy.Core.Events;
using Osdy.Core.Experiences;

namespace Osdy.Core.Runtime;

public sealed record MemoryStats(int Events, int Decisions, int Experiences, int TraitChanges);

/// <summary>Persistence port. One <see cref="Commit"/> per step, applied atomically.</summary>
public interface IBrainStore
{
    /// <summary>The last committed snapshot, or null when this robot has never been created.</summary>
    BrainSnapshot? Load();

    /// <summary>Saves the step's snapshot and appends its event, decision, experience and trait changes.</summary>
    void Commit(StepResult result);

    /// <summary>The most recent decisions, oldest first.</summary>
    IReadOnlyList<Decision> GetRecentDecisions(int count);

    /// <summary>The most recent experiences, oldest first.</summary>
    IReadOnlyList<Experience> GetRecentExperiences(int count);

    /// <summary>The most recent trait changes, oldest first.</summary>
    IReadOnlyList<TraitChange> GetRecentTraitChanges(int count);

    /// <summary>Every observed event in the order it was processed.</summary>
    IReadOnlyList<BrainEvent> GetEvents();

    MemoryStats GetStats();
}
