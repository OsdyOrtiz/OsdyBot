using Osdy.Core.Decisions.Rules;
using Osdy.Core.States;

namespace Osdy.Core.Decisions;

/// <summary>Evaluates rules in priority order; the first one that chooses wins.</summary>
public sealed class DecisionEngine
{
    public const string NoRuleName = "no-rule";

    private readonly IReadOnlyList<IDecisionRule> _rules;

    public DecisionEngine(IReadOnlyList<IDecisionRule> rules) => _rules = rules;

    public static DecisionEngine Default { get; } = new(
    [
        new ExhaustionRule(),
        new WakeUpRule(),
        new HumanInteractionRule(),
        new HumanPresenceRule(),
        new InteractionFeedbackRule(),
        new InteractionEndRule(),
        new NoveltyRule(),
        new TimeoutRule(),
        new SpontaneousExploreRule(),
    ]);

    public IReadOnlyList<IDecisionRule> Rules => _rules;

    /// <summary>
    /// Returns the decision for this step. Events always get a decision (an explicit NoAction when no rule applies);
    /// ticks only when a rule chose something.
    /// </summary>
    public Decision? Decide(DecisionContext context, DecisionTrigger trigger)
    {
        foreach (var rule in _rules)
        {
            var choice = rule.Evaluate(context);
            if (choice is not null)
            {
                StateMachine.EnsureValid(context.State, choice.ToState);
                return Stamp(context, trigger, rule.Name, choice);
            }
        }

        if (context.Event is null)
        {
            return null;
        }

        var none = new Choice(
            DecisionKind.Reactive,
            BehaviorAction.NoAction,
            context.State,
            [$"No rule applies to {context.Event.Type} in state {context.State}"]);
        return Stamp(context, trigger, NoRuleName, none);
    }

    internal static Decision Stamp(DecisionContext context, DecisionTrigger trigger, string rule, Choice choice) => new(
        Guid.CreateVersion7(context.Now),
        context.Now,
        trigger,
        context.Event?.Id,
        choice.Kind,
        rule,
        choice.Action,
        context.State,
        choice.ToState,
        choice.Reasons);
}
