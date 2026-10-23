using Osdy.Core.Experiences;

namespace Osdy.Core.Personality;

/// <summary>Finds what the profile looked like before a recorded trait change. See docs/memory-and-persistence.md.</summary>
public static class PersonalityRollback
{
    /// <summary>
    /// For each trait changed by the target or any later change, the value recorded immediately before its first such
    /// change. <paramref name="history"/> must be in persistence order: timestamps tie, so they never decide "later".
    /// </summary>
    public static IReadOnlyDictionary<TraitKind, double> ValuesBefore(IReadOnlyList<TraitChange> history, Guid targetId)
    {
        var index = -1;
        for (var i = 0; i < history.Count && index < 0; i++)
        {
            index = history[i].Id == targetId ? i : -1;
        }

        if (index < 0)
        {
            throw new InvalidOperationException($"Trait change {targetId} does not exist.");
        }

        var values = new Dictionary<TraitKind, double>();
        foreach (var change in history.Skip(index))
        {
            values.TryAdd(change.Trait, change.OldValue);
        }

        return values;
    }

    /// <summary>
    /// The first saved trait that the current bounds cannot hold, or null. Rollback checks this before restoring the
    /// brain, because <see cref="Brain.Restore(BrainSnapshot, BrainOptions, Decisions.DecisionEngine?)"/> clamps silently,
    /// and a clamp would change a value with no <see cref="TraitChange"/>.
    /// </summary>
    public static TraitState? FirstOutOfBounds(PersonalityProfile saved, PersonalityOptions bounds) =>
        saved.Traits.FirstOrDefault(t => !(t.Value >= bounds.TraitMin && t.Value <= bounds.TraitMax));
}
