namespace Osdy.Core.Personality;

/// <summary>
/// Current value of one trait plus the window budget that limits how much it may move per window.
/// </summary>
public sealed record TraitState(
    TraitKind Kind,
    double Value,
    double Baseline,
    DateTimeOffset BudgetWindowStart,
    double BudgetUsed);
