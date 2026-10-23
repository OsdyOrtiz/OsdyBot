namespace Osdy.Core.Events;

public enum EventType
{
    HumanPresenceDetected,
    HumanLeft,
    HumanInteractionDetected,
    PositiveInteraction,
    NegativeInteraction,
    InteractionEnded,
    NoveltyDetected,

    /// <summary>The owner asked to undo a trait change (<see cref="BrainEvent.TargetId"/>) and everything after it.</summary>
    OwnerRollbackRequested,
}

/// <summary>An observed fact reported by a sensor, adapter or simulator. Never contains interpretation.</summary>
public sealed record BrainEvent(Guid Id, EventType Type, DateTimeOffset OccurredAt, string Source, Guid? TargetId = null)
{
    public static BrainEvent Create(EventType type, DateTimeOffset occurredAt, string source, Guid? targetId = null) =>
        new(Guid.CreateVersion7(occurredAt), type, occurredAt, source, targetId);

    /// <summary>Events in which a human addresses the robot directly.</summary>
    public bool IsInteraction =>
        Type is EventType.HumanInteractionDetected or EventType.PositiveInteraction or EventType.NegativeInteraction;
}
