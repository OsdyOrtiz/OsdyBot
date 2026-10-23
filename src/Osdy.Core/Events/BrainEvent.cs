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
}

/// <summary>An observed fact reported by a sensor, adapter or simulator. Never contains interpretation.</summary>
public sealed record BrainEvent(Guid Id, EventType Type, DateTimeOffset OccurredAt, string Source)
{
    public static BrainEvent Create(EventType type, DateTimeOffset occurredAt, string source) =>
        new(Guid.CreateVersion7(occurredAt), type, occurredAt, source);

    /// <summary>Events in which a human addresses the robot directly.</summary>
    public bool IsInteraction =>
        Type is EventType.HumanInteractionDetected or EventType.PositiveInteraction or EventType.NegativeInteraction;
}
