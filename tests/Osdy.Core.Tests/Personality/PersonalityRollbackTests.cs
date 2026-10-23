using Osdy.Core.Decisions;
using Osdy.Core.Events;
using Osdy.Core.Experiences;
using Osdy.Core.Personality;
using static Osdy.Core.Tests.TestBrains;

namespace Osdy.Core.Tests.Personality;

public sealed class PersonalityRollbackTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Novelty, then three interactions stamped with the same instant: the trait changes interleave and tie on time,
    /// so only the persisted order says what came before the target.
    /// </summary>
    private static (Brain Brain, List<TraitChange> History, PersonalityProfile BeforeNegative) Learned(BrainOptions? options = null)
    {
        var brain = New(options);
        var history = new List<TraitChange>();
        var beforeNegative = brain.Personality;
        foreach (var (type, at) in new[]
        {
            (EventType.NoveltyDetected, S(10)),
            (EventType.PositiveInteraction, Minute),
            (EventType.NegativeInteraction, Minute),
            (EventType.PositiveInteraction, Minute),
        })
        {
            if (type == EventType.NegativeInteraction)
            {
                beforeNegative = brain.Personality;
            }

            history.AddRange(brain.Handle(type, at).TraitChanges);
        }

        return (brain, history, beforeNegative);
    }

    private static BrainEvent Request(Guid target, int minutes = 5) =>
        BrainEvent.Create(EventType.OwnerRollbackRequested, T0 + TimeSpan.FromMinutes(minutes), "cli", target);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Rollback_restores_the_whole_profile_immediately_before_the_target_by_persisted_order(int offsetInNegativeStep)
    {
        var (brain, history, beforeNegative) = Learned();
        var negative = history.Where(c => c.Rule == "negative-interaction").ToArray();
        Assert.True(negative.Length >= 2);
        Assert.True(history.Count(c => c.OccurredAt == negative[0].OccurredAt) > negative.Length, "changes of other events tie with the target");
        var target = negative[offsetInNegativeStep];
        var current = brain.Personality;
        var runtime = brain.Runtime;
        var request = Request(target.Id);

        var step = brain.Rollback(request, history);

        // Everything from the target on is undone, whatever the trait; earlier changes of the same step survive.
        var targetIndex = history.IndexOf(target);
        foreach (var trait in Enum.GetValues<TraitKind>())
        {
            var firstUndone = history.Skip(targetIndex).FirstOrDefault(c => c.Trait == trait);
            var expected = firstUndone?.OldValue ?? current[trait].Value;
            Assert.Equal(expected, brain.Personality[trait].Value);
            Assert.Equal(expected, step.Snapshot.Personality[trait].Value);
        }

        if (offsetInNegativeStep == 0)
        {
            Assert.All(Enum.GetValues<TraitKind>(), t => Assert.Equal(beforeNegative[t].Value, brain.Personality[t].Value));
        }

        Assert.Same(request, step.Event);
        var decision = step.Decision!;
        Assert.Equal("owner-rollback", decision.Rule);
        Assert.Equal(BehaviorAction.RestorePersonality, decision.Action);
        Assert.Equal(request.Id, decision.EventId);
        Assert.Equal(runtime.State, decision.FromState);
        Assert.Equal(runtime.State, decision.ToState);
        Assert.Contains(decision.Reasons, r => r.Contains(target.Id.ToString(), StringComparison.Ordinal));
        var experience = step.Experience!;
        Assert.Equal(ExperienceKind.OwnerRollback, experience.Kind);
        Assert.Equal((request.Id, decision.Id), (experience.EventId, experience.DecisionId));
        Assert.NotEmpty(step.TraitChanges);
        Assert.All(step.TraitChanges, change =>
        {
            Assert.Equal(TraitChangeKind.Restoration, change.Kind);
            Assert.Equal(target.Id, change.RollbackTargetId);
            Assert.Equal(experience.Id, change.ExperienceId);
            Assert.Equal("owner-rollback", change.Rule);
            Assert.Equal(current[change.Trait].Value, change.OldValue);
            Assert.Equal(brain.Personality[change.Trait].Value, change.NewValue);
            Assert.Equal(change.NewValue - change.OldValue, change.Delta);
        });
        Assert.Equal(runtime.State, brain.Runtime.State);
        Assert.Equal(runtime.StateEnteredAt, brain.Runtime.StateEnteredAt);
        Assert.Equal(runtime.LastInteractionAt, brain.Runtime.LastInteractionAt);
    }

    [Fact]
    public void Rollback_leaves_the_learning_budget_as_it_was_and_adds_no_spending()
    {
        var (brain, history, _) = Learned();
        var spent = brain.Personality.Traits.ToDictionary(t => t.Kind, t => t.RecentChanges);

        var step = brain.Rollback(Request(history[0].Id), history);

        Assert.NotEmpty(step.TraitChanges);
        foreach (var trait in brain.Personality.Traits)
        {
            Assert.Equal(spent[trait.Kind], trait.RecentChanges);
        }
    }

    [Fact]
    public void Rollback_with_nothing_to_change_is_still_audited()
    {
        var (brain, history, _) = Learned();
        var target = history[0].Id;
        history.AddRange(brain.Rollback(Request(target), history).TraitChanges);
        var profile = brain.Personality;

        var again = brain.Rollback(Request(target, minutes: 6), history);

        Assert.Empty(again.TraitChanges);
        Assert.Equal(ExperienceKind.OwnerRollback, again.Experience!.Kind);
        Assert.Equal("owner-rollback", again.Decision!.Rule);
        Assert.Contains(again.Decision.Reasons, r => r.Contains("already", StringComparison.Ordinal));
        Assert.True(again.HasRecords);
        Assert.All(Enum.GetValues<TraitKind>(), t => Assert.Equal(profile[t].Value, brain.Personality[t].Value));
    }

    [Fact]
    public void Rollback_to_an_unknown_target_or_with_a_malformed_request_changes_nothing()
    {
        var (brain, history, _) = Learned();
        var personality = brain.Personality.Traits.ToArray();
        var runtime = brain.Runtime;

        Assert.Throws<InvalidOperationException>(() => brain.Rollback(Request(Guid.NewGuid()), history));
        Assert.Throws<ArgumentException>(() => brain.Rollback(
            BrainEvent.Create(EventType.OwnerRollbackRequested, T0 + Minute, "cli"), history));
        Assert.Throws<ArgumentException>(() => brain.Rollback(
            BrainEvent.Create(EventType.PositiveInteraction, T0 + Minute, "cli", history[0].Id), history));
        Assert.Throws<ArgumentException>(() => brain.Handle(Request(history[0].Id)));

        Assert.Equal(personality, brain.Personality.Traits);
        Assert.Equal(runtime, brain.Runtime);
    }

    [Fact]
    public void Rollback_rejects_recorded_values_outside_the_current_bounds_without_partial_changes()
    {
        var recorded = new BrainOptions { Personality = new PersonalityOptions { InitialCuriosity = 0.9 } };
        var (learner, history, _) = Learned(recorded);
        var narrow = new BrainOptions
        {
            Personality = new PersonalityOptions { TraitMax = 0.8, InitialCuriosity = 0.7 },
        };
        var brain = Brain.Restore(learner.Snapshot, narrow);
        var personality = brain.Personality.Traits.ToArray();
        var runtime = brain.Runtime;

        var error = Assert.Throws<InvalidOperationException>(() => brain.Rollback(Request(history[0].Id), history));

        Assert.Contains("bounds", error.Message, StringComparison.Ordinal);
        Assert.Equal(personality, brain.Personality.Traits);
        Assert.Equal(runtime, brain.Runtime);
    }

    [Fact]
    public void A_saved_profile_outside_the_current_bounds_is_reported_not_clamped()
    {
        var saved = New(TestBrains.WithTraits(0.9, 0.35, 0.4)).Personality;

        var misfit = PersonalityRollback.FirstOutOfBounds(saved, new PersonalityOptions { TraitMax = 0.8, InitialCuriosity = 0.7 });

        Assert.Equal((TraitKind.Curiosity, 0.9), (misfit!.Kind, misfit.Value));
        Assert.Null(PersonalityRollback.FirstOutOfBounds(saved, new PersonalityOptions()));
    }
}
