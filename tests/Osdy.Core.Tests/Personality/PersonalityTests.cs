using Osdy.Core.Personality;

namespace Osdy.Core.Tests.Personality;

public class PersonalityTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Initial_personality_uses_the_three_configured_traits_as_value_and_baseline()
    {
        var personality = PersonalityProfile.Initial(new PersonalityOptions(), Now);

        Assert.Equal(3, personality.Traits.Count);
        Assert.Equal(0.70, personality[TraitKind.Curiosity].Value);
        Assert.Equal(0.35, personality[TraitKind.Sociability].Value);
        Assert.Equal(0.40, personality[TraitKind.Confidence].Value);
        Assert.All(personality.Traits, t => Assert.Equal(t.Value, t.Baseline));
        Assert.All(personality.Traits, t => Assert.Equal(0, t.BudgetUsed));
    }

    [Fact]
    public void Restore_clamps_values_into_the_current_bounds()
    {
        var stored = new[]
        {
            new TraitState(TraitKind.Curiosity, 0.99, 0.70, Now, 0),
            new TraitState(TraitKind.Sociability, 0.01, 0.35, Now, 0),
            new TraitState(TraitKind.Confidence, 0.40, 0.40, Now, 0),
        };

        var personality = PersonalityProfile.Restore(stored, new PersonalityOptions());

        Assert.Equal(0.95, personality[TraitKind.Curiosity].Value);
        Assert.Equal(0.05, personality[TraitKind.Sociability].Value);
    }

    [Fact]
    public void Restore_requires_every_trait()
    {
        var stored = new[] { new TraitState(TraitKind.Curiosity, 0.5, 0.5, Now, 0) };

        Assert.Throws<InvalidOperationException>(() => PersonalityProfile.Restore(stored, new PersonalityOptions()));
    }

    [Fact]
    public void With_replaces_a_single_trait_without_mutating_the_original()
    {
        var original = PersonalityProfile.Initial(new PersonalityOptions(), Now);

        var changed = original.With(original[TraitKind.Confidence] with { Value = 0.5 });

        Assert.Equal(0.40, original[TraitKind.Confidence].Value);
        Assert.Equal(0.5, changed[TraitKind.Confidence].Value);
        Assert.Equal(0.70, changed[TraitKind.Curiosity].Value);
    }

    [Theory]
    [InlineData(0.6, 0.5, 0.7, 0.35, 0.40)]   // min >= max
    [InlineData(0.05, 0.95, 0.99, 0.35, 0.40)] // initial outside bounds
    [InlineData(-0.1, 0.95, 0.70, 0.35, 0.40)] // bounds outside [0, 1]
    public void Invalid_personality_options_are_rejected(double min, double max, double curiosity, double sociability, double confidence)
    {
        var options = new PersonalityOptions
        {
            TraitMin = min,
            TraitMax = max,
            InitialCuriosity = curiosity,
            InitialSociability = sociability,
            InitialConfidence = confidence,
        };

        Assert.NotEmpty(options.Validate());
    }

    [Fact]
    public void Default_options_are_valid()
    {
        Assert.Empty(new PersonalityOptions().Validate());
        Assert.Empty(new LearningOptions().Validate());
    }

    [Fact]
    public void Invalid_learning_options_are_rejected()
    {
        var options = new LearningOptions { LearningRate = -1, MaxStep = 0, BudgetPerWindow = -0.1, BudgetWindow = TimeSpan.Zero };

        Assert.Equal(4, options.Validate().Count);
    }
}
