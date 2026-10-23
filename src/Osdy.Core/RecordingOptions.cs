using Osdy.Core.Events;

namespace Osdy.Core;

/// <summary>
/// Which event types are persisted and learned from. An unrecorded event is still processed (decision and state),
/// but produces no experience and no trait change, and only its decision is stored, without an event link.
/// </summary>
public sealed record RecordingOptions
{
    /// <summary>Names of the recorded <see cref="EventType"/>s. Defaults to every type.</summary>
    public IReadOnlyList<string> EventTypes { get; init; } = [.. Enum.GetNames<EventType>()];

    public bool IsRecorded(EventType type) =>
        EventTypes.Contains(type.ToString(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        foreach (var name in EventTypes.Where(name => !IsKnown(name)))
        {
            errors.Add($"Recording.EventTypes contains unknown event type '{name}'. Valid types: {string.Join(", ", Enum.GetNames<EventType>())}.");
        }

        if (!IsRecorded(EventType.OwnerRollbackRequested))
        {
            errors.Add($"Recording.EventTypes must include {nameof(EventType.OwnerRollbackRequested)}: the owner's rollback is always recorded.");
        }

        return errors;
    }

    private static bool IsKnown(string name) =>
        Enum.GetNames<EventType>().Contains(name, StringComparer.OrdinalIgnoreCase);
}
