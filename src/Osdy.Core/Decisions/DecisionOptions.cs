namespace Osdy.Core.Decisions;

public sealed record DecisionOptions
{
    public double EngageWeightSociability { get; init; } = 0.4;
    public double EngageWeightCuriosity { get; init; } = 0.3;
    public double EngageWeightConfidence { get; init; } = 0.3;
    public double EngageThreshold { get; init; } = 0.45;
    public double MinEnergyToEngage { get; init; } = 0.30;

    public double NoveltyCuriosityThreshold { get; init; } = 0.40;
    public double ExploreMinEnergy { get; init; } = 0.50;

    /// <summary>Base idle time before spontaneous exploration; scaled by (1.5 - curiosity).</summary>
    public TimeSpan IdleBeforeExplore { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Anti-annoyance cooldown between spontaneous behaviors.</summary>
    public TimeSpan MinTimeBetweenSpontaneous { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan CuriousDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan AttentiveTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan InteractingTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (EngageWeightSociability < 0 || EngageWeightCuriosity < 0 || EngageWeightConfidence < 0)
        {
            errors.Add("Engagement weights must be >= 0.");
        }

        foreach (var (name, value) in new[]
                 {
                     (nameof(EngageThreshold), EngageThreshold),
                     (nameof(MinEnergyToEngage), MinEnergyToEngage),
                     (nameof(NoveltyCuriosityThreshold), NoveltyCuriosityThreshold),
                     (nameof(ExploreMinEnergy), ExploreMinEnergy),
                 })
        {
            if (value is < 0 or > 1)
            {
                errors.Add($"{name} must be within [0, 1].");
            }
        }

        foreach (var (name, value) in new[]
                 {
                     (nameof(IdleBeforeExplore), IdleBeforeExplore),
                     (nameof(MinTimeBetweenSpontaneous), MinTimeBetweenSpontaneous),
                     (nameof(CuriousDuration), CuriousDuration),
                     (nameof(AttentiveTimeout), AttentiveTimeout),
                     (nameof(InteractingTimeout), InteractingTimeout),
                 })
        {
            if (value <= TimeSpan.Zero)
            {
                errors.Add($"{name} must be positive.");
            }
        }

        return errors;
    }
}
