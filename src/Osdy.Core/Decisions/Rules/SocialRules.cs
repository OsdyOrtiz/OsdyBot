using Osdy.Core.Events;
using Osdy.Core.Personality;
using Osdy.Core.States;
using static Osdy.Core.Decisions.Rules.Format;

namespace Osdy.Core.Decisions.Rules;

/// <summary>Rule 3: someone addresses the robot. Personality decides between engaging and observing.</summary>
public sealed class HumanInteractionRule : IDecisionRule
{
    public string Name => "human-interaction";

    public Choice? Evaluate(DecisionContext context)
    {
        if (!context.IsEvent(EventType.HumanInteractionDetected))
        {
            return null;
        }

        var options = context.Options.Decisions;
        var energy = context.Runtime.Energy;
        if (energy < options.MinEnergyToEngage)
        {
            return new Choice(
                DecisionKind.Reactive,
                BehaviorAction.NoAction,
                context.State,
                [$"Too tired: energy {N(energy)} < {N(options.MinEnergyToEngage)}"]);
        }

        if (context.State == BrainState.Resting)
        {
            return new Choice(
                DecisionKind.Reactive,
                BehaviorAction.WakeAndNotice,
                BrainState.Attentive,
                [$"Woken by interaction; energy {N(energy)} >= {N(options.MinEnergyToEngage)}"]);
        }

        var curiosity = context.Trait(TraitKind.Curiosity);
        var sociability = context.Trait(TraitKind.Sociability);
        var confidence = context.Trait(TraitKind.Confidence);
        var engagement = (options.EngageWeightSociability * sociability)
                         + (options.EngageWeightCuriosity * curiosity)
                         + (options.EngageWeightConfidence * confidence);
        var traits = $"curiosity {N(curiosity)}, sociability {N(sociability)}, confidence {N(confidence)}";
        var energyReason = $"Energy sufficient: {N(energy)} >= {N(options.MinEnergyToEngage)}";

        return engagement >= options.EngageThreshold
            ? new Choice(
                DecisionKind.Reactive,
                BehaviorAction.ObserveAndEngage,
                BrainState.Interacting,
                [$"Engagement {N(engagement)} >= {N(options.EngageThreshold)} ({traits})", energyReason])
            : new Choice(
                DecisionKind.Reactive,
                BehaviorAction.ObserveCautiously,
                BrainState.Attentive,
                [$"Engagement {N(engagement)} < {N(options.EngageThreshold)} ({traits})", energyReason]);
    }
}

/// <summary>Rule 4: a human appears nearby.</summary>
public sealed class HumanPresenceRule : IDecisionRule
{
    public string Name => "human-presence";

    public Choice? Evaluate(DecisionContext context)
    {
        if (!context.IsEvent(EventType.HumanPresenceDetected))
        {
            return null;
        }

        return context.State is BrainState.Idle or BrainState.Curious
            ? new Choice(DecisionKind.Reactive, BehaviorAction.NoticeHuman, BrainState.Attentive, [$"Human detected while {context.State}"])
            : new Choice(DecisionKind.Reactive, BehaviorAction.NoAction, context.State, [$"Presence noted; staying {context.State}"]);
    }
}

/// <summary>Rule 5: the human's interaction is pleasant or unpleasant.</summary>
public sealed class InteractionFeedbackRule : IDecisionRule
{
    public string Name => "interaction-feedback";

    public Choice? Evaluate(DecisionContext context)
    {
        var positive = context.IsEvent(EventType.PositiveInteraction);
        if (!positive && !context.IsEvent(EventType.NegativeInteraction))
        {
            return null;
        }

        if (context.State == BrainState.Resting)
        {
            return new Choice(DecisionKind.Reactive, BehaviorAction.NoAction, BrainState.Resting, ["Resting; did not respond"]);
        }

        return positive
            ? new Choice(DecisionKind.Reactive, BehaviorAction.EnjoyInteraction, BrainState.Interacting, ["Positive interaction received"])
            : new Choice(DecisionKind.Reactive, BehaviorAction.BackOff, BrainState.Attentive, ["Negative interaction received; keeping distance"]);
    }
}

/// <summary>Rule 6: the interaction is over or the human left.</summary>
public sealed class InteractionEndRule : IDecisionRule
{
    public string Name => "interaction-end";

    public Choice? Evaluate(DecisionContext context)
    {
        if (!context.IsEvent(EventType.InteractionEnded) && !context.IsEvent(EventType.HumanLeft))
        {
            return null;
        }

        if (context.State is not (BrainState.Interacting or BrainState.Attentive))
        {
            return null;
        }

        return new Choice(
            DecisionKind.Reactive,
            BehaviorAction.ReturnToIdle,
            BrainState.Idle,
            [$"{context.Event!.Type} while {context.State}"]);
    }
}
