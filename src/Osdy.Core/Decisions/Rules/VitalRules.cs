using Osdy.Core.States;
using static Osdy.Core.Decisions.Rules.Format;

namespace Osdy.Core.Decisions.Rules;

/// <summary>Rule 1: go to rest when energy runs low.</summary>
public sealed class ExhaustionRule : IDecisionRule
{
    public string Name => "exhaustion";

    public Choice? Evaluate(DecisionContext context)
    {
        var threshold = context.Options.Vitals.RestThreshold;
        if (!context.IsTick || context.State == BrainState.Resting || context.Runtime.Energy >= threshold)
        {
            return null;
        }

        return new Choice(
            DecisionKind.Spontaneous,
            BehaviorAction.Rest,
            BrainState.Resting,
            [$"Energy {N(context.Runtime.Energy)} < rest threshold {N(threshold)}"]);
    }
}

/// <summary>Rule 2: wake up once energy has recovered.</summary>
public sealed class WakeUpRule : IDecisionRule
{
    public string Name => "wake-up";

    public Choice? Evaluate(DecisionContext context)
    {
        var threshold = context.Options.Vitals.WakeThreshold;
        if (!context.IsTick || context.State != BrainState.Resting || context.Runtime.Energy < threshold)
        {
            return null;
        }

        return new Choice(
            DecisionKind.Spontaneous,
            BehaviorAction.WakeUp,
            BrainState.Idle,
            [$"Energy {N(context.Runtime.Energy)} >= wake threshold {N(threshold)}"]);
    }
}
