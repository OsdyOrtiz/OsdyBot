using Osdy.Core.Events;
using Osdy.Core.Simulation;
using static Osdy.Core.Tests.TestBrains;

namespace Osdy.Core.Tests.Simulation;

public class ScenarioTests
{
    private const string FirstMeeting = """
        # first meeting
        00:00:05 HumanPresenceDetected
        00:00:10 HumanInteractionDetected
        00:00:20 PositiveInteraction
        00:00:40 PositiveInteraction
        00:01:00 InteractionEnded
        00:01:30 NoveltyDetected
        00:15:00 wait
        """;

    [Fact]
    public void Parses_offsets_events_waits_and_comments()
    {
        var entries = ScenarioParser.Parse(FirstMeeting);

        Assert.Equal(7, entries.Count);
        Assert.Equal(new ScenarioEntry(S(5), EventType.HumanPresenceDetected), entries[0]);
        Assert.Equal(new ScenarioEntry(TimeSpan.FromMinutes(15), null), entries[^1]);
    }

    [Theory]
    [InlineData("00:00:05", "Scenario line 1: expected")]
    [InlineData("5s HumanLeft", "Scenario line 1: invalid offset '5s'")]
    [InlineData("00:00:05 Dance", "Scenario line 1: unknown event 'Dance'")]
    [InlineData("00:00:10 HumanLeft\n00:00:05 HumanLeft", "Scenario line 2: offset 00:00:05 is earlier")]
    public void Rejects_malformed_lines_with_their_line_number(string text, string expected)
    {
        var error = Assert.Throws<FormatException>(() => ScenarioParser.Parse(text));

        Assert.StartsWith(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Formatting_round_trips_through_the_parser()
    {
        var entries = ScenarioParser.Parse(FirstMeeting + "\n1.02:00:00 HumanLeft");

        Assert.Equal(entries, ScenarioParser.Parse(ScenarioParser.Format(entries)));
    }

    [Fact]
    public void Replaying_the_same_scenario_produces_the_same_trace()
    {
        var first = Trace(FirstMeeting);
        var second = Trace(FirstMeeting);

        Assert.Equal(first, second);
        Assert.Contains(first, line => line.Contains("ObserveAndEngage", StringComparison.Ordinal));
        Assert.Contains(first, line => line.Contains("ExploreSpontaneously", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_decision_in_a_scenario_is_explained()
    {
        var steps = new List<StepResult>();
        new ScenarioSimulator(New(), T0, steps.Add).Run(ScenarioParser.Parse(FirstMeeting));

        var decisions = steps.Select(s => s.Decision).OfType<Osdy.Core.Decisions.Decision>().ToList();
        Assert.NotEmpty(decisions);
        Assert.All(decisions, d => Assert.False(string.IsNullOrWhiteSpace(d.Rule)));
        Assert.All(decisions, d => Assert.NotEmpty(d.Reasons));
    }

    private static List<string> Trace(string scenario)
    {
        var trace = new List<string>();
        new ScenarioSimulator(New(), T0, step =>
        {
            if (step.Decision is { } d)
            {
                trace.Add($"{d.OccurredAt:O} {d.Rule} {d.Action} {d.FromState}->{d.ToState} {string.Join(" | ", d.Reasons)}");
            }

            trace.AddRange(step.TraitChanges.Select(c => $"{c.Trait} {c.OldValue:R}->{c.NewValue:R} {c.Rule}"));
        }).Run(ScenarioParser.Parse(scenario));
        return trace;
    }
}
