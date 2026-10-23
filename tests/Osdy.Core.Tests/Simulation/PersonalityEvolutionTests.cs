using Osdy.Core.Decisions;
using Osdy.Core.Events;
using Osdy.Core.Experiences;
using Osdy.Core.Personality;
using Osdy.Core.Simulation;
using static Osdy.Core.Tests.TestBrains;

namespace Osdy.Core.Tests.Simulation;

public class PersonalityEvolutionTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public void Hundreds_of_experiences_in_one_day_stay_within_bounds_and_the_daily_budget()
    {
        var brain = New();
        var learning = brain.Options.Learning;
        var changes = new List<TraitChange>();
        brain.Handle(EventType.HumanInteractionDetected, S(0));

        for (var i = 1; i <= 500; i++)
        {
            var type = i % 5 == 0 ? EventType.NegativeInteraction : EventType.PositiveInteraction;
            var step = brain.Handle(type, S(i * 60));
            changes.AddRange(step.TraitChanges);
            Assert.All(brain.Personality.Traits, t => Assert.InRange(t.Value, 0.05, 0.95));
        }

        Assert.NotEmpty(changes);
        Assert.All(changes, c => Assert.True(Math.Abs(c.Delta) <= learning.MaxStep + 1e-12));
        foreach (var trait in Enum.GetValues<TraitKind>())
        {
            var moved = changes.Where(c => c.Trait == trait).Sum(c => Math.Abs(c.Delta));
            Assert.True(moved <= learning.BudgetPerWindow + 1e-9, $"{trait} moved {moved} in one window");
        }
    }

    [Fact]
    public void Two_robots_with_the_same_start_diverge_according_to_their_history()
    {
        var options = WithTraits(curiosity: 0.5, sociability: 0.3, confidence: 0.3);
        var (robotA, decisionsA) = Live(options, days: 30, sessionEveryDays: 1, positivesPerSession: 10);
        var (robotB, decisionsB) = Live(options, days: 30, sessionEveryDays: 7, positivesPerSession: 1);

        var sociabilityA = robotA.Personality[TraitKind.Sociability].Value;
        var sociabilityB = robotB.Personality[TraitKind.Sociability].Value;
        Assert.True(sociabilityA > sociabilityB + 0.2, $"A {sociabilityA} vs B {sociabilityB}");
        Assert.True(sociabilityB - 0.3 <= 5 * options.Learning.BudgetPerWindow, $"B moved too much: {sociabilityB}");

        foreach (var robot in new[] { robotA, robotB })
        {
            Assert.All(robot.Personality.Traits, t => Assert.InRange(t.Value, 0.05, 0.95));
            Assert.All(robot.Personality.Traits, t => Assert.True(Math.Abs(t.Value - t.Baseline) <= 30 * options.Learning.BudgetPerWindow));
        }

        // Personality shows up in behavior: A starts cautious and ends up engaging; B stays cautious.
        var greetingsA = decisionsA.Where(d => d.Rule == "human-interaction").ToList();
        Assert.Equal(BehaviorAction.ObserveCautiously, greetingsA[0].Action);
        Assert.Contains(greetingsA.TakeLast(5), d => d.Action == BehaviorAction.ObserveAndEngage);
        Assert.DoesNotContain(decisionsB, d => d.Action == BehaviorAction.ObserveAndEngage);
    }

    private static (Brain Brain, List<Decision> Decisions) Live(BrainOptions options, int days, int sessionEveryDays, int positivesPerSession)
    {
        var brain = New(options);
        var decisions = new List<Decision>();
        var simulator = new ScenarioSimulator(brain, T0, step =>
        {
            if (step.Decision is { } d)
            {
                decisions.Add(d);
            }
        }, tickInterval: Minute);

        for (var day = 0; day < days; day++)
        {
            if (day % sessionEveryDays != 0)
            {
                continue;
            }

            var session = T0 + TimeSpan.FromDays(day) + TimeSpan.FromHours(2);
            simulator.Post(EventType.HumanInteractionDetected, session);
            for (var i = 1; i <= positivesPerSession; i++)
            {
                simulator.Post(EventType.PositiveInteraction, session + (i * Minute));
            }

            simulator.Post(EventType.InteractionEnded, session + ((positivesPerSession + 1) * Minute));
        }

        simulator.AdvanceTo(T0 + TimeSpan.FromDays(days));
        return (brain, decisions);
    }
}
