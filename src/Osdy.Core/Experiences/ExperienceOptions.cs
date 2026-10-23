using Osdy.Core.Personality;

namespace Osdy.Core.Experiences;

/// <summary>How an experience kind is read: valence in [-1, 1], importance in [0, 1].</summary>
public sealed record Appraisal(double Valence, double Importance);

/// <summary>Which traits an experience kind teaches, and how strongly (gain per trait).</summary>
public sealed record LearningRule(string Name, ExperienceKind Experience, IReadOnlyDictionary<TraitKind, double> Gains);

public sealed record ExperienceOptions
{
    public IReadOnlyDictionary<ExperienceKind, Appraisal> Appraisals { get; init; } = new Dictionary<ExperienceKind, Appraisal>
    {
        [ExperienceKind.PositiveInteraction] = new(1.0, 0.5),
        [ExperienceKind.NegativeInteraction] = new(-1.0, 0.7),
        [ExperienceKind.NoveltyExplored] = new(0.5, 0.3),
    };

    public IReadOnlyList<LearningRule> Rules { get; init; } =
    [
        new("positive-interaction", ExperienceKind.PositiveInteraction, new Dictionary<TraitKind, double>
        {
            [TraitKind.Sociability] = 1.0,
            [TraitKind.Confidence] = 0.6,
        }),
        new("negative-interaction", ExperienceKind.NegativeInteraction, new Dictionary<TraitKind, double>
        {
            [TraitKind.Confidence] = 1.0,
            [TraitKind.Sociability] = 0.5,
        }),
        new("novelty-explored", ExperienceKind.NoveltyExplored, new Dictionary<TraitKind, double>
        {
            [TraitKind.Curiosity] = 1.0,
        }),
    ];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        // Owner rollbacks are corrections, not interpretations: they are never appraised or learned from.
        foreach (var kind in Enum.GetValues<ExperienceKind>().Where(k => k != ExperienceKind.OwnerRollback))
        {
            if (!Appraisals.TryGetValue(kind, out var appraisal))
            {
                errors.Add($"Missing appraisal for {kind}.");
            }
            else if (appraisal.Valence is < -1 or > 1 || appraisal.Importance is < 0 or > 1)
            {
                errors.Add($"Appraisal for {kind} must have valence in [-1, 1] and importance in [0, 1].");
            }
        }

        foreach (var rule in Rules.Where(r => r.Gains.Values.Any(g => g < 0)))
        {
            errors.Add($"Learning rule {rule.Name} has a negative gain; use valence for direction.");
        }

        return errors;
    }
}
