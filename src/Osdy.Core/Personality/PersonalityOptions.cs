namespace Osdy.Core.Personality;

public sealed record PersonalityOptions
{
    public double TraitMin { get; init; } = 0.05;
    public double TraitMax { get; init; } = 0.95;
    public double InitialCuriosity { get; init; } = 0.70;
    public double InitialSociability { get; init; } = 0.35;
    public double InitialConfidence { get; init; } = 0.40;

    public double Initial(TraitKind kind) => kind switch
    {
        TraitKind.Curiosity => InitialCuriosity,
        TraitKind.Sociability => InitialSociability,
        TraitKind.Confidence => InitialConfidence,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (TraitMin < 0 || TraitMax > 1 || TraitMin >= TraitMax)
        {
            errors.Add($"Trait bounds must satisfy 0 <= TraitMin < TraitMax <= 1 (got {TraitMin}..{TraitMax}).");
        }

        foreach (var kind in Enum.GetValues<TraitKind>())
        {
            var value = Initial(kind);
            if (value < TraitMin || value > TraitMax)
            {
                errors.Add($"Initial {kind} ({value}) must be within [{TraitMin}, {TraitMax}].");
            }
        }

        return errors;
    }
}
