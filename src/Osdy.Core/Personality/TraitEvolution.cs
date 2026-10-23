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
        var recent = trait.RecentChanges
            .Where(change => change.OccurredAt > now - learning.BudgetWindow && change.OccurredAt <= now)
            .ToArray();
        var used = recent.Sum(change => change.Magnitude);
        var pruned = recent.Length == trait.RecentChanges.Count
            ? trait
            : trait with { RecentChanges = Array.AsReadOnly(recent) };

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
            return new TraitUpdate(pruned, 0);
        }

        var updated = trait with
        {
            Value = newValue,
            RecentChanges = Array.AsReadOnly<TraitBudgetEntry>([.. recent, new(now, Math.Abs(applied))]),
        };
        return new TraitUpdate(updated, applied);
    }

    /// <summary>
    /// Owner correction: assigns a recorded value exactly. It bypasses learning attenuation and neither reads nor
    /// spends the sliding budget, but still refuses values outside the current bounds.
    /// </summary>
    public static TraitUpdate Apply(TraitState trait, double restoredValue, PersonalityOptions bounds)
    {
        if (!(restoredValue >= bounds.TraitMin && restoredValue <= bounds.TraitMax))
        {
            throw new InvalidOperationException(
                $"Recorded {trait.Kind} value {restoredValue.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)} is outside the current bounds [{bounds.TraitMin}, {bounds.TraitMax}].");
        }

        return new TraitUpdate(trait with { Value = restoredValue }, restoredValue - trait.Value);
    }
}
