using Osdy.Core.Decisions;
using Osdy.Core.Experiences;
using Osdy.Core.Personality;
using Osdy.Core.Vitals;

namespace Osdy.Core;

public sealed record RuntimeOptions
{
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Steps that only drained energy are persisted at most this often.</summary>
    public TimeSpan CheckpointInterval { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxConsecutiveFailures { get; init; } = 5;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (TickInterval <= TimeSpan.Zero)
        {
            errors.Add("TickInterval must be positive.");
        }

        if (CheckpointInterval < TimeSpan.Zero)
        {
            errors.Add("CheckpointInterval must be >= 0.");
        }

        if (MaxConsecutiveFailures < 1)
        {
            errors.Add("MaxConsecutiveFailures must be >= 1.");
        }

        return errors;
    }
}

/// <summary>Every tunable parameter of Osdy Core, with documented defaults.</summary>
public sealed record BrainOptions
{
    public string RobotName { get; init; } = "Osdy";
    public PersonalityOptions Personality { get; init; } = new();
    public LearningOptions Learning { get; init; } = new();
    public ExperienceOptions Experiences { get; init; } = new();
    public VitalsOptions Vitals { get; init; } = new();
    public DecisionOptions Decisions { get; init; } = new();
    public RuntimeOptions Runtime { get; init; } = new();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(RobotName))
        {
            errors.Add("RobotName is required.");
        }

        errors.AddRange(Personality.Validate());
        errors.AddRange(Learning.Validate());
        errors.AddRange(Experiences.Validate());
        errors.AddRange(Vitals.Validate());
        errors.AddRange(Decisions.Validate());
        errors.AddRange(Runtime.Validate());
        return errors;
    }

    public BrainOptions EnsureValid()
    {
        var errors = Validate();
        return errors.Count == 0
            ? this
            : throw new ArgumentException("Invalid brain options:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
}
