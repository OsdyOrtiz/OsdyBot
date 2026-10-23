using Osdy.Core.Decisions;
using Osdy.Core.Decisions.Rules;
using Osdy.Core.Events;
using Osdy.Core.Experiences;
using Osdy.Core.Personality;
using Osdy.Core.States;
using static Osdy.Core.Tests.TestBrains;

namespace Osdy.Core.Tests.Decisions;

public class BrainDecisionTests
{
    [Fact]
    public void Default_personality_engages_when_a_human_interacts()
    {
        var brain = New();

        var step = brain.Handle(EventType.HumanInteractionDetected, S(5));

        var decision = Assert.IsType<Decision>(step.Decision);
        Assert.Equal("human-interaction", decision.Rule);
        Assert.Equal(BehaviorAction.ObserveAndEngage, decision.Action);
        Assert.Equal(DecisionKind.Reactive, decision.Kind);
        Assert.Equal(DecisionTrigger.Event, decision.Trigger);
        Assert.Equal(step.Event!.Id, decision.EventId);
        Assert.Equal((BrainState.Idle, BrainState.Interacting), (decision.FromState, decision.ToState));
        Assert.Equal(
            ["Engagement 0.47 >= 0.45 (curiosity 0.70, sociability 0.35, confidence 0.40)", "Energy sufficient: 1.00 >= 0.30"],
            decision.Reasons);
        Assert.Equal(BrainState.Interacting, brain.Runtime.State);
        Assert.Equal(T0 + S(5), brain.Runtime.StateEnteredAt);
        Assert.Null(step.Experience);
        Assert.Empty(step.TraitChanges);
        Assert.True(step.HasRecords);
    }

    [Fact]
    public void Shy_unconfident_robot_only_observes_when_a_human_interacts()
    {
        var brain = New(WithTraits(curiosity: 0.5, sociability: 0.1, confidence: 0.1));

        var decision = brain.Handle(EventType.HumanInteractionDetected, S(5)).Decision!;

        Assert.Equal(BehaviorAction.ObserveCautiously, decision.Action);
        Assert.Equal(BrainState.Attentive, brain.Runtime.State);
        Assert.StartsWith("Engagement 0.22 < 0.45", decision.Reasons[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Tired_robot_explicitly_declines_to_engage()
    {
        var brain = New(WithEnergy(0.25));

        var decision = brain.Handle(EventType.HumanInteractionDetected, S(1)).Decision!;

        Assert.Equal(("human-interaction", BehaviorAction.NoAction), (decision.Rule, decision.Action));
        Assert.Equal(BrainState.Idle, brain.Runtime.State);
        Assert.Equal(["Too tired: energy 0.25 < 0.30"], decision.Reasons);
    }

    [Fact]
    public void Low_energy_sends_the_robot_to_rest_and_recovered_energy_wakes_it()
    {
        var brain = New(WithEnergy(0.19));

        var rest = brain.Tick(S(1)).Decision!;
        var stillResting = brain.Tick(TimeSpan.FromHours(2));
        var wake = brain.Tick(TimeSpan.FromHours(3)).Decision!;

        Assert.Equal(("exhaustion", BehaviorAction.Rest, BrainState.Resting, DecisionKind.Spontaneous), (rest.Rule, rest.Action, rest.ToState, rest.Kind));
        Assert.Equal(["Energy 0.19 < rest threshold 0.20"], rest.Reasons);
        Assert.Null(stillResting.Decision);
        Assert.Equal(("wake-up", BehaviorAction.WakeUp, BrainState.Idle), (wake.Rule, wake.Action, wake.ToState));
        Assert.Equal(1.0, brain.Runtime.Energy);
    }

    [Fact]
    public void Interaction_while_resting_with_enough_energy_wakes_the_robot_into_attention()
    {
        var brain = New(WithEnergy(0.1));
        brain.Tick(S(1));

        var decision = brain.Handle(EventType.HumanInteractionDetected, TimeSpan.FromHours(1) + S(1)).Decision!;

        Assert.Equal((BehaviorAction.WakeAndNotice, BrainState.Resting, BrainState.Attentive), (decision.Action, decision.FromState, decision.ToState));
        Assert.Equal(0.4, brain.Runtime.Energy, precision: 3);
    }

    [Fact]
    public void Presence_is_noticed_when_idle_but_does_not_interrupt_an_interaction()
    {
        var idle = New();
        var noticed = idle.Handle(EventType.HumanPresenceDetected, S(1)).Decision!;

        var busy = New();
        busy.Handle(EventType.HumanInteractionDetected, S(1));
        var ignored = busy.Handle(EventType.HumanPresenceDetected, S(2)).Decision!;

        Assert.Equal((BehaviorAction.NoticeHuman, BrainState.Attentive), (noticed.Action, noticed.ToState));
        Assert.Equal((BehaviorAction.NoAction, BrainState.Interacting), (ignored.Action, ignored.ToState));
        Assert.Equal(["Presence noted; staying Interacting"], ignored.Reasons);
    }

    [Fact]
    public void Positive_interaction_is_saved_as_an_experience_and_teaches_sociability_and_confidence()
    {
        var brain = New();
        brain.Handle(EventType.HumanInteractionDetected, S(1));

        var step = brain.Handle(EventType.PositiveInteraction, S(10));

        var decision = step.Decision!;
        Assert.Equal(("interaction-feedback", BehaviorAction.EnjoyInteraction, BrainState.Interacting), (decision.Rule, decision.Action, decision.ToState));

        var experience = Assert.IsType<Experience>(step.Experience);
        Assert.Equal(ExperienceKind.PositiveInteraction, experience.Kind);
        Assert.Equal((1.0, 0.5), (experience.Valence, experience.Importance));
        Assert.Equal((step.Event!.Id, decision.Id), (experience.EventId, experience.DecisionId));
        Assert.Equal((BrainState.Interacting, BehaviorAction.EnjoyInteraction), (experience.State, experience.Action));
        Assert.Equal("EnjoyInteraction -> Interacting", experience.Outcome);
        Assert.Equal("test", experience.Source);

        Assert.Equal([TraitKind.Sociability, TraitKind.Confidence], step.TraitChanges.Select(c => c.Trait));
        Assert.All(step.TraitChanges, c => Assert.Equal(("positive-interaction", experience.Id), (c.Rule, c.ExperienceId)));
        var confidence = step.TraitChanges.Single(c => c.Trait == TraitKind.Confidence);
        Assert.Equal(0.01 * 0.6 * 0.5 * (0.55 / 0.90), confidence.Delta, precision: 10);
        Assert.Equal(0.40, confidence.OldValue);
        Assert.Equal(confidence.NewValue, brain.Personality[TraitKind.Confidence].Value);
        Assert.Equal(0.35 + (0.01 * 0.5 * (0.60 / 0.90)), brain.Personality[TraitKind.Sociability].Value, precision: 10);
    }

    [Fact]
    public void Negative_interaction_makes_the_robot_back_off_and_lowers_confidence_and_sociability()
    {
        var brain = New();
        brain.Handle(EventType.HumanInteractionDetected, S(1));

        var step = brain.Handle(EventType.NegativeInteraction, S(10));

        Assert.Equal((BehaviorAction.BackOff, BrainState.Attentive), (step.Decision!.Action, step.Decision.ToState));
        Assert.Equal(ExperienceKind.NegativeInteraction, step.Experience!.Kind);
        Assert.Equal([TraitKind.Confidence, TraitKind.Sociability], step.TraitChanges.Select(c => c.Trait));
        Assert.All(step.TraitChanges, c => Assert.True(c.Delta < 0));
        Assert.All(step.TraitChanges, c => Assert.Equal("negative-interaction", c.Rule));
        Assert.True(brain.Personality[TraitKind.Confidence].Value < 0.40);
    }

    [Fact]
    public void Interaction_while_resting_gets_no_response_but_is_still_remembered()
    {
        var brain = New(WithEnergy(0.1));
        brain.Tick(S(1));

        var step = brain.Handle(EventType.PositiveInteraction, S(5));

        Assert.Equal((BehaviorAction.NoAction, BrainState.Resting), (step.Decision!.Action, step.Decision.ToState));
        Assert.Equal(["Resting; did not respond"], step.Decision.Reasons);
        Assert.Equal((BrainState.Resting, BehaviorAction.NoAction), (step.Experience!.State, step.Experience.Action));
        Assert.NotEmpty(step.TraitChanges);
    }

    [Fact]
    public void Ending_an_interaction_returns_to_idle()
    {
        var brain = New();
        brain.Handle(EventType.HumanInteractionDetected, S(1));

        var decision = brain.Handle(EventType.InteractionEnded, S(30)).Decision!;

        Assert.Equal(("interaction-end", BehaviorAction.ReturnToIdle, BrainState.Idle), (decision.Rule, decision.Action, decision.ToState));
    }

    [Fact]
    public void Events_that_no_rule_handles_get_an_explicit_no_action_decision()
    {
        var brain = New();

        var decision = brain.Handle(EventType.HumanLeft, S(1)).Decision!;

        Assert.Equal((DecisionEngine.NoRuleName, BehaviorAction.NoAction, BrainState.Idle), (decision.Rule, decision.Action, decision.ToState));
        Assert.Equal(["No rule applies to HumanLeft in state Idle"], decision.Reasons);
    }

    [Fact]
    public void Curious_robot_investigates_novelty_and_learns_from_it()
    {
        var brain = New();

        var step = brain.Handle(EventType.NoveltyDetected, S(1));

        Assert.Equal(("novelty", BehaviorAction.Investigate, BrainState.Curious), (step.Decision!.Rule, step.Decision.Action, step.Decision.ToState));
        Assert.Equal(ExperienceKind.NoveltyExplored, step.Experience!.Kind);
        var change = Assert.Single(step.TraitChanges);
        Assert.Equal((TraitKind.Curiosity, "novelty-explored"), (change.Trait, change.Rule));
        Assert.True(change.Delta > 0);
    }

    [Fact]
    public void Incurious_robot_ignores_novelty_without_learning()
    {
        var brain = New(WithTraits(curiosity: 0.3, sociability: 0.35, confidence: 0.4));

        var step = brain.Handle(EventType.NoveltyDetected, S(1));

        Assert.Equal((BehaviorAction.NoAction, BrainState.Idle), (step.Decision!.Action, step.Decision.ToState));
        Assert.Equal(["Curiosity 0.30 < 0.40; novelty ignored"], step.Decision.Reasons);
        Assert.Null(step.Experience);
        Assert.Empty(step.TraitChanges);
    }

    [Fact]
    public void Novelty_does_not_distract_an_interacting_robot()
    {
        var brain = New();
        brain.Handle(EventType.HumanInteractionDetected, S(1));

        var step = brain.Handle(EventType.NoveltyDetected, S(2));

        Assert.Equal(["Busy (Interacting); novelty ignored"], step.Decision!.Reasons);
        Assert.Null(step.Experience);
    }

    [Fact]
    public void Curiosity_fades_after_the_configured_duration()
    {
        var brain = New();
        brain.Handle(EventType.NoveltyDetected, S(0));

        var before = brain.Tick(S(29));
        var after = brain.Tick(S(30)).Decision!;

        Assert.Null(before.Decision);
        Assert.Equal(("timeouts", BehaviorAction.LoseInterest, BrainState.Idle), (after.Rule, after.Action, after.ToState));
        Assert.Equal(["Curious for 00:00:30 >= 00:00:30"], after.Reasons);
    }

    [Fact]
    public void Investigating_novelty_while_already_curious_restarts_the_curiosity_period()
    {
        var brain = New();
        brain.Handle(EventType.NoveltyDetected, S(0));
        brain.Handle(EventType.NoveltyDetected, S(20));

        var before = brain.Tick(S(49));
        var after = brain.Tick(S(50)).Decision!;

        Assert.Null(before.Decision);
        Assert.Equal(BehaviorAction.LoseInterest, after.Action);
    }

    [Fact]
    public void Ignoring_an_event_does_not_reset_the_time_in_state()
    {
        var brain = New();
        brain.Handle(EventType.NoveltyDetected, S(0));
        brain.Handle(EventType.HumanLeft, S(20)); // no rule applies → NoAction

        Assert.Equal(BehaviorAction.LoseInterest, brain.Tick(S(30)).Decision!.Action);
    }

    [Fact]
    public void Interacting_times_out_only_after_the_last_interaction()
    {
        var brain = New();
        brain.Handle(EventType.HumanInteractionDetected, S(0));
        brain.Handle(EventType.PositiveInteraction, S(60));

        var before = brain.Tick(S(60 + 119));
        var after = brain.Tick(S(60 + 120)).Decision!;

        Assert.Null(before.Decision);
        Assert.Equal((BehaviorAction.ReturnToIdle, BrainState.Interacting, BrainState.Idle), (after.Action, after.FromState, after.ToState));
        Assert.Equal(["No interaction for 00:02:00 >= 00:02:00"], after.Reasons);
    }

    [Fact]
    public void Attention_times_out_when_nobody_interacts()
    {
        var brain = New();
        brain.Handle(EventType.HumanPresenceDetected, S(0));

        var decision = brain.Tick(S(60)).Decision!;

        Assert.Equal((BehaviorAction.ReturnToIdle, BrainState.Attentive, BrainState.Idle), (decision.Action, decision.FromState, decision.ToState));
    }

    [Fact]
    public void Bored_robot_explores_spontaneously_but_respects_the_cooldown()
    {
        var brain = New(); // curiosity 0.70 → boredom time 2 min × 0.8 = 96 s

        var tooSoon = brain.Tick(S(95));
        var explore = brain.Tick(S(97)).Decision!;
        var loseInterest = brain.Tick(S(127)).Decision!;
        var cooldown = brain.Tick(S(127 + 97));
        var again = brain.Tick(S(97 + 300)).Decision!;

        Assert.Null(tooSoon.Decision);
        Assert.Equal(("spontaneous-explore", BehaviorAction.ExploreSpontaneously, DecisionKind.Spontaneous), (explore.Rule, explore.Action, explore.Kind));
        Assert.Equal(["Idle for 00:01:37 >= boredom time 00:01:36 (curiosity 0.70)", "Energy 1.00 >= 0.50"], explore.Reasons);
        Assert.Equal(BehaviorAction.LoseInterest, loseInterest.Action);
        Assert.Null(cooldown.Decision);
        Assert.Equal(BehaviorAction.ExploreSpontaneously, again.Action);
    }

    [Fact]
    public void Low_energy_robot_does_not_explore()
    {
        var brain = New(WithEnergy(0.45));

        Assert.Null(brain.Tick(S(600)).Decision);
    }

    [Fact]
    public void Quiet_ticks_produce_nothing_to_record()
    {
        var step = New().Tick(S(1));

        Assert.Null(step.Decision);
        Assert.False(step.HasRecords);
    }

    [Fact]
    public void Energy_drains_according_to_the_current_state()
    {
        var brain = New();
        brain.Handle(EventType.HumanInteractionDetected, S(0));

        var step = brain.Tick(TimeSpan.FromHours(1));

        Assert.Equal(1.0 - 0.15, step.Snapshot.Runtime.Energy, precision: 10);
    }

    [Fact]
    public void A_rule_that_chooses_a_forbidden_transition_is_rejected()
    {
        var engine = new DecisionEngine([new ExhaustionRule(), new RogueRule()]);
        var brain = Brain.CreateNew(WithEnergy(0.1), T0, engine);
        brain.Tick(S(1));

        var error = Assert.Throws<InvalidOperationException>(() => brain.Tick(S(2)));

        Assert.Contains("Resting -> Curious", error.Message, StringComparison.Ordinal);
        Assert.Equal(BrainState.Resting, brain.Runtime.State);
    }

    [Fact]
    public void Restart_resumes_transient_states_as_idle_and_counts_downtime_as_rest()
    {
        var before = New(WithEnergy(0.5));
        before.Handle(EventType.HumanInteractionDetected, S(0));
        var restored = Brain.Restore(before.Snapshot, before.Options);

        var step = restored.Start(T0 + TimeSpan.FromHours(1));

        var decision = step.Decision!;
        Assert.Equal(("restart-recovery", BehaviorAction.RecoverAfterRestart, DecisionKind.System, DecisionTrigger.Startup), (decision.Rule, decision.Action, decision.Kind, decision.Trigger));
        Assert.Equal((BrainState.Interacting, BrainState.Idle), (decision.FromState, decision.ToState));
        Assert.Equal(["State Interacting is transient; resuming as Idle after 01:00:00 downtime"], decision.Reasons);
        Assert.Equal(0.8, restored.Runtime.Energy, precision: 10);
        Assert.Equal(before.Identity, restored.Identity);
    }

    [Fact]
    public void Restart_resumes_resting_without_a_decision()
    {
        var before = New(WithEnergy(0.1));
        before.Tick(S(1));
        var restored = Brain.Restore(before.Snapshot, before.Options);

        var step = restored.Start(T0 + S(60));

        Assert.Null(step.Decision);
        Assert.Equal(BrainState.Resting, restored.Runtime.State);
    }

    private sealed class RogueRule : IDecisionRule
    {
        public string Name => "rogue";

        public Choice? Evaluate(DecisionContext context) =>
            new(DecisionKind.Spontaneous, BehaviorAction.Investigate, BrainState.Curious, ["test"]);
    }
}
