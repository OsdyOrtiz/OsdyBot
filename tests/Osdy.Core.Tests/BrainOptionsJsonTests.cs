using System.Text.Json;
using Osdy.Core.States;

namespace Osdy.Core.Tests;

public class BrainOptionsJsonTests
{
    [Fact]
    public void Partial_json_overrides_only_the_given_values()
    {
        var options = BrainOptionsJson.Parse("""
            {
              // comments are allowed
              "robotName": "Pip",
              "personality": { "initialSociability": 0.6 },
              "runtime": { "tickInterval": "00:00:02" },
            }
            """);

        Assert.Equal("Pip", options.RobotName);
        Assert.Equal(0.6, options.Personality.InitialSociability);
        Assert.Equal(0.70, options.Personality.InitialCuriosity);
        Assert.Equal(TimeSpan.FromSeconds(2), options.Runtime.TickInterval);
        Assert.Equal(0.01, options.Learning.LearningRate);
    }

    [Fact]
    public void Serialized_defaults_parse_back_to_equivalent_options()
    {
        var parsed = BrainOptionsJson.Parse(BrainOptionsJson.Serialize(new BrainOptions()));

        Assert.Equal(new BrainOptions().Personality, parsed.Personality);
        Assert.Equal(new BrainOptions().Decisions, parsed.Decisions);
        Assert.Equal(0.30, parsed.Vitals.EnergyPerHour[BrainState.Resting]);
        Assert.Equal(3, parsed.Experiences.Rules.Count);
    }

    [Fact]
    public void Unknown_properties_are_rejected()
    {
        Assert.Throws<JsonException>(() => BrainOptionsJson.Parse("""{ "personality": { "initialCuriosty": 0.5 } }"""));
    }

    [Fact]
    public void Invalid_values_are_rejected_with_the_validation_message()
    {
        var error = Assert.Throws<ArgumentException>(() => BrainOptionsJson.Parse("""{ "personality": { "initialCuriosity": 2 } }"""));

        Assert.Contains("Initial Curiosity (2)", error.Message, StringComparison.Ordinal);
    }
}
