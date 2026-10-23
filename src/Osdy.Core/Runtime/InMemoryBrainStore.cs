using Osdy.Core.Decisions;
using Osdy.Core.Events;
using Osdy.Core.Experiences;

namespace Osdy.Core.Runtime;

/// <summary>Volatile store for simulations and tests.</summary>
public sealed class InMemoryBrainStore : IBrainStore
{
    private readonly List<BrainEvent> _events = [];
    private readonly List<Decision> _decisions = [];
    private readonly List<Experience> _experiences = [];
    private readonly List<TraitChange> _traitChanges = [];
    private BrainSnapshot? _snapshot;

    public BrainSnapshot? Load() => _snapshot;

    public void Commit(StepResult result)
    {
        _snapshot = result.Snapshot;
        if (result.Event is not null)
        {
            _events.Add(result.Event);
        }

        if (result.Decision is not null)
        {
            _decisions.Add(result.Decision);
        }

        if (result.Experience is not null)
        {
            _experiences.Add(result.Experience);
        }

        _traitChanges.AddRange(result.TraitChanges);
    }

    public IReadOnlyList<Decision> GetRecentDecisions(int count) => [.. _decisions.TakeLast(count)];

    public IReadOnlyList<Experience> GetRecentExperiences(int count) => [.. _experiences.TakeLast(count)];

    public IReadOnlyList<TraitChange> GetRecentTraitChanges(int count) => [.. _traitChanges.TakeLast(count)];

    public IReadOnlyList<BrainEvent> GetEvents() => [.. _events];

    public MemoryStats GetStats() => new(_events.Count, _decisions.Count, _experiences.Count, _traitChanges.Count);
}
