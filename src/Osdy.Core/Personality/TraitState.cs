namespace Osdy.Core.Personality;

/// <summary>Absolute movement applied to a trait at a caller-provided time.</summary>
public readonly record struct TraitBudgetEntry(DateTimeOffset OccurredAt, double Magnitude);

/// <summary>Current value of one trait and its recent absolute movements for the sliding budget.</summary>
public sealed record TraitState(
    TraitKind Kind,
    double Value,
    double Baseline,
    IReadOnlyList<TraitBudgetEntry> RecentChanges);
