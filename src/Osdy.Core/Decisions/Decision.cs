using Osdy.Core.States;

namespace Osdy.Core.Decisions;

public enum DecisionKind
{
    /// <summary>Caused by an external event.</summary>
    Reactive,

    /// <summary>Caused by time and internal state (boredom, fatigue, timeouts).</summary>
    Spontaneous,

    /// <summary>Caused by the runtime itself (e.g. restart recovery).</summary>
    System,
}

public enum DecisionTrigger
{
    Startup,
    Event,
    Tick,
}

public enum BehaviorAction
{
    NoAction,
    ObserveAndEngage,
    ObserveCautiously,
    WakeAndNotice,
    NoticeHuman,
    EnjoyInteraction,
    BackOff,
    ReturnToIdle,
    Investigate,
    LoseInterest,
    ExploreSpontaneously,
    Rest,
    WakeUp,
    RecoverAfterRestart,
    RestorePersonality,
}

/// <summary>What the brain chose, why, and the state transition it caused.</summary>
public sealed record Decision(
    Guid Id,
    DateTimeOffset OccurredAt,
    DecisionTrigger Trigger,
    Guid? EventId,
    DecisionKind Kind,
    string Rule,
    BehaviorAction Action,
    BrainState FromState,
    BrainState ToState,
    IReadOnlyList<string> Reasons)
{
    public bool ChangesState => FromState != ToState;
}
