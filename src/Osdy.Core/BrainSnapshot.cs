using Osdy.Core.Decisions;
using Osdy.Core.Events;
using Osdy.Core.Experiences;
using Osdy.Core.Identity;
using Osdy.Core.Personality;
using Osdy.Core.States;

namespace Osdy.Core;

/// <summary>Working state of the brain between steps.</summary>
public sealed record RuntimeState(
    BrainState State,
    DateTimeOffset StateEnteredAt,
    double Energy,
    DateTimeOffset LastUpdatedAt,
    DateTimeOffset? LastSpontaneousAt,
    DateTimeOffset? LastInteractionAt);

/// <summary>Everything needed to restore the brain after a restart.</summary>
public sealed record BrainSnapshot(RobotIdentity Identity, PersonalityProfile Personality, RuntimeState Runtime);

/// <summary>What happened in one step. The unit of persistence and observation.</summary>
public sealed record StepResult(
    DateTimeOffset At,
    DecisionTrigger Trigger,
    BrainEvent? Event,
    Decision? Decision,
    Experience? Experience,
    IReadOnlyList<TraitChange> TraitChanges,
    BrainSnapshot Snapshot,
    bool EventRecorded = true)
{
    /// <summary>The event to persist: null when there is none or the recording policy excludes its type.</summary>
    public BrainEvent? RecordedEvent => EventRecorded ? Event : null;

    /// <summary>True when the step produced something that must be recorded (not just energy drift).</summary>
    public bool HasRecords => Event is not null || Decision is not null || Experience is not null || TraitChanges.Count > 0;
}
