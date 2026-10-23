using Osdy.Core.Events;
using Osdy.Core.Personality;
using Osdy.Core.States;
using static Osdy.Core.Decisions.Rules.Format;

namespace Osdy.Core.Decisions.Rules;

/// <summary>Rule 7: something new appears.</summary>
public sealed class NoveltyRule : IDecisionRule
{
    public string Name => "novelty";

    public Choice? Evaluate(DecisionContext context)
    {
        if (!context.IsEvent(EventType.NoveltyDetected))
        {
            return null;
        }

        if (context.State is not (BrainState.Idle or BrainState.Curious))
        {
            return new Choice(DecisionKind.Reactive, BehaviorAction.NoAction, context.State, [$"Busy ({context.State}); novelty ignored"]);
        }

        var options = context.Options.Decisions;
        var curiosity = context.Trait(TraitKind.Curiosity);
        var energy = context.Runtime.Energy;
        if (curiosity < options.NoveltyCuriosityThreshold)
        {
            return new Choice(
                DecisionKind.Reactive,
                BehaviorAction.NoAction,
                context.State,
                [$"Curiosity {N(curiosity)} < {N(options.NoveltyCuriosityThreshold)}; novelty ignored"]);
        }

        if (energy < options.MinEnergyToEngage)
        {
            return new Choice(
                DecisionKind.Reactive,
                BehaviorAction.NoAction,
                context.State,
                [$"Too tired to investigate: energy {N(energy)} < {N(options.MinEnergyToEngage)}"]);
        }

        return new Choice(
            DecisionKind.Reactive,
            BehaviorAction.Investigate,
            BrainState.Curious,
            [$"Curiosity {N(curiosity)} >= {N(options.NoveltyCuriosityThreshold)}", $"Energy {N(energy)}"]);
    }
}

/// <summary>Rule 8: states that should not last forever.</summary>
public sealed class TimeoutRule : IDecisionRule
{
    public string Name => "timeouts";

    public Choice? Evaluate(DecisionContext context)
    {
        if (!context.IsTick)
        {
            return null;
        }

        var options = context.Options.Decisions;
        switch (context.State)
        {
            case BrainState.Curious when context.TimeInState >= options.CuriousDuration:
                return new Choice(
                    DecisionKind.Spontaneous,
                    BehaviorAction.LoseInterest,
                    BrainState.Idle,
                    [$"Curious for {T(context.TimeInState)} >= {T(options.CuriousDuration)}"]);

            case BrainState.Attentive when SinceLastInteraction(context) >= options.AttentiveTimeout:
                return new Choice(
                    DecisionKind.Spontaneous,
                    BehaviorAction.ReturnToIdle,
                    BrainState.Idle,
                    [$"No interaction for {T(SinceLastInteraction(context))} >= {T(options.AttentiveTimeout)}"]);

            case BrainState.Interacting when SinceLastInteraction(context) >= options.InteractingTimeout:
                return new Choice(
                    DecisionKind.Spontaneous,
                    BehaviorAction.ReturnToIdle,
                    BrainState.Idle,
                    [$"No interaction for {T(SinceLastInteraction(context))} >= {T(options.InteractingTimeout)}"]);

            default:
                return null;
        }
    }

    private static TimeSpan SinceLastInteraction(DecisionContext context)
    {
        var last = context.Runtime.LastInteractionAt is { } at && at > context.Runtime.StateEnteredAt
            ? at
            : context.Runtime.StateEnteredAt;
        return context.Now - last;
    }
}

/// <summary>Rule 9: boredom leads to exploring, limited by a cooldown.</summary>
public sealed class SpontaneousExploreRule : IDecisionRule
{
    public string Name => "spontaneous-explore";

    public static TimeSpan BoredomTime(DecisionOptions options, double curiosity) =>
        options.IdleBeforeExplore * (1.5 - curiosity);

    public Choice? Evaluate(DecisionContext context)
    {
        if (!context.IsTick || context.State != BrainState.Idle)
        {
            return null;
        }

        var options = context.Options.Decisions;
        var curiosity = context.Trait(TraitKind.Curiosity);
        var boredom = BoredomTime(options, curiosity);
        if (context.TimeInState < boredom || context.Runtime.Energy < options.ExploreMinEnergy)
        {
            return null;
        }

        if (context.Runtime.LastSpontaneousAt is { } last && context.Now - last < options.MinTimeBetweenSpontaneous)
        {
            return null;
        }

        return new Choice(
            DecisionKind.Spontaneous,
            BehaviorAction.ExploreSpontaneously,
            BrainState.Curious,
            [
                $"Idle for {T(context.TimeInState)} >= boredom time {T(boredom)} (curiosity {N(curiosity)})",
                $"Energy {N(context.Runtime.Energy)} >= {N(options.ExploreMinEnergy)}",
            ]);
    }
}
