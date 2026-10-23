namespace Osdy.Core.Personality;

/// <summary>Parameters of the bounded learning model described in docs/personality-model.md.</summary>
public sealed record LearningOptions
{
    public double LearningRate { get; init; } = 0.01;
    public double MaxStep { get; init; } = 0.005;
    public double BudgetPerWindow { get; init; } = 0.03;
    public TimeSpan BudgetWindow { get; init; } = TimeSpan.FromHours(24);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (LearningRate < 0)
        {
            errors.Add("LearningRate must be >= 0.");
        }

        if (MaxStep <= 0)
        {
            errors.Add("MaxStep must be > 0.");
        }

        if (BudgetPerWindow < 0)
        {
            errors.Add("BudgetPerWindow must be >= 0.");
        }

        if (BudgetWindow <= TimeSpan.Zero)
        {
            errors.Add("BudgetWindow must be positive.");
        }

        return errors;
    }
}
