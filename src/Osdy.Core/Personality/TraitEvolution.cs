namespace Osdy.Core.Personality;

public readonly record struct TraitUpdate(TraitState Trait, double Delta);

/// <summary>
/// The only place where trait values change. See docs/personality-model.md for the formula and its guarantees.
/// </summary>
public static class TraitEvolution
{
    public const double MinimumChange = 1e-6;

    /// <param name="signal">Rule gain multiplied by experience valence; positive raises the trait.</param>
    /// <param name="importance">Experience importance in [0, 1].</param>
    public static TraitUpdate Apply(
        TraitState trait,
        double signal,
        double importance,
        DateTimeOffset now,
        PersonalityOptions bounds,
        LearningOptions learning)
    {
        var windowElapsed = now - trait.BudgetWindowStart >= learning.BudgetWindow;
        var windowStart = windowElapsed ? now : trait.BudgetWindowStart;
        var used = windowElapsed ? 0 : trait.BudgetUsed;

        var raw = learning.LearningRate * signal * importance;
        var range = bounds.TraitMax - bounds.TraitMin;
        var headroom = raw >= 0
            ? (bounds.TraitMax - trait.Value) / range
            : (trait.Value - bounds.TraitMin) / range;

        var delta = Math.Clamp(raw * Math.Clamp(headroom, 0, 1), -learning.MaxStep, learning.MaxStep);
        var remaining = Math.Max(0, learning.BudgetPerWindow - used);
        delta = Math.Sign(delta) * Math.Min(Math.Abs(delta), remaining);

        var newValue = Math.Clamp(trait.Value + delta, bounds.TraitMin, bounds.TraitMax);
        var applied = newValue - trait.Value;
        if (Math.Abs(applied) < MinimumChange)
        {
            return new TraitUpdate(trait, 0);
        }

        var updated = trait with
        {
            Value = newValue,
            BudgetWindowStart = windowStart,
            BudgetUsed = used + Math.Abs(applied),
        };
        return new TraitUpdate(updated, applied);
    }
}
