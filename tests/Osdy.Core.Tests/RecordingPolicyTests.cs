using System.Text.Json;
using Osdy.Core.Decisions;
using Osdy.Core.Events;
using Osdy.Core.States;
using static Osdy.Core.Tests.TestBrains;

namespace Osdy.Core.Tests;

public class RecordingPolicyTests
{
    private static BrainOptions Without(params EventType[] excluded) => new()
    {
        Recording = new RecordingOptions
        {
            EventTypes = [.. Enum.GetValues<EventType>().Except(excluded).Select(type => type.ToString())],
        },
    };

    [Fact]
    public void Recorded_event_types_still_learn_as_before()
    {
        var brain = New();
        brain.Handle(EventType.HumanInteractionDetected, S(5));

        var step = brain.Handle(EventType.NegativeInteraction, S(10));

        Assert.True(step.EventRecorded);
        Assert.Same(step.Event, step.RecordedEvent);
        Assert.Equal(step.Event!.Id, step.Decision!.EventId);
        Assert.NotNull(step.Experience);
        Assert.NotEmpty(step.TraitChanges);
    }

    [Fact]
    public void Unrecorded_event_is_processed_but_neither_learned_nor_linked()
    {
        var recorded = New();
        var excluded = New(Without(EventType.NegativeInteraction));
        recorded.Handle(EventType.HumanInteractionDetected, S(5));
        excluded.Handle(EventType.HumanInteractionDetected, S(5));
        var before = excluded.Personality;

        var step = excluded.Handle(EventType.NegativeInteraction, S(10));
        var reference = recorded.Handle(EventType.NegativeInteraction, S(10));

        Assert.False(step.EventRecorded);
        Assert.Equal(EventType.NegativeInteraction, step.Event!.Type);
        Assert.Null(step.RecordedEvent);
        var decision = Assert.IsType<Decision>(step.Decision);
        Assert.Null(decision.EventId);
        Assert.Equal((reference.Decision!.Rule, reference.Decision.Action, reference.Decision.ToState), (decision.Rule, decision.Action, decision.ToState));
        Assert.Equal(reference.Decision.Reasons, decision.Reasons);
        Assert.Equal(reference.Snapshot.Runtime.State, excluded.Runtime.State);
        Assert.Equal((BrainState.Interacting, BrainState.Attentive), (decision.FromState, decision.ToState));
        Assert.Equal(BrainState.Attentive, excluded.Runtime.State);
        Assert.Null(step.Experience);
        Assert.Empty(step.TraitChanges);
        Assert.Same(before, excluded.Personality);
        Assert.True(step.HasRecords);
    }

    [Fact]
    public void Default_recording_policy_records_every_event_type()
    {
        var options = new RecordingOptions();

        Assert.Empty(options.Validate());
        Assert.All(Enum.GetValues<EventType>(), type => Assert.True(options.IsRecorded(type)));
    }

    [Fact]
    public void Owner_rollback_cannot_be_excluded()
    {
        var error = Assert.Throws<ArgumentException>(() => New(Without(EventType.OwnerRollbackRequested)));

        Assert.Contains(
            "Recording.EventTypes must include OwnerRollbackRequested: the owner's rollback is always recorded.",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_event_type_names_are_rejected_with_the_valid_names()
    {
        var error = Assert.Throws<ArgumentException>(() => BrainOptionsJson.Parse("""{ "recording": { "eventTypes": ["Dance", "OwnerRollbackRequested"] } }"""));

        Assert.Contains(
            "Recording.EventTypes contains unknown event type 'Dance'. Valid types: " + string.Join(", ", Enum.GetNames<EventType>()) + ".",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Json_selects_the_recorded_event_types_and_rejects_unknown_properties()
    {
        var options = BrainOptionsJson.Parse("""{ "recording": { "eventTypes": ["HumanPresenceDetected", "OwnerRollbackRequested"] } }""");

        Assert.True(options.Recording.IsRecorded(EventType.HumanPresenceDetected));
        Assert.False(options.Recording.IsRecorded(EventType.NegativeInteraction));
        Assert.Throws<JsonException>(() => BrainOptionsJson.Parse("""{ "recording": { "excluded": [] } }"""));
    }
}
