using Osdy.Core.Personality;

namespace Osdy.Core.Tests.Personality;

public class TraitEvolutionTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly PersonalityOptions Bounds = new();
    private static readonly LearningOptions Learning = new();

    private static TraitState Trait(double value) => new(TraitKind.Confidence, value, value, Start, 0);

    [Fact]
    public void Positive_signal_matches_the_documented_worked_example()
    {
        // Confidence 0.40, gain 0.6, valence +1, importance 0.5 → ≈ +0.0018 (printed +0.002).
        var update = TraitEvolution.Apply(Trait(0.40), signal: 0.6, importance: 0.5, Start, Bounds, Learning);

        Assert.Equal(0.01 * 0.6 * 0.5 * (0.55 / 0.90), update.Delta, precision: 10);
        Assert.Equal(0.40 + update.Delta, update.Trait.Value, precision: 10);
        Assert.Equal(Math.Abs(update.Delta), update.Trait.BudgetUsed, precision: 10);
    }

    [Fact]
    public void Negative_signal_lowers_the_trait_using_distance_to_the_minimum()
    {
        var update = TraitEvolution.Apply(Trait(0.40), signal: -1.0, importance: 0.7, Start, Bounds, Learning);

        Assert.Equal(-0.01 * 0.7 * (0.35 / 0.90), update.Delta, precision: 10);
        Assert.True(update.Trait.Value < 0.40);
    }

    [Fact]
    public void A_single_experience_never_moves_a_trait_more_than_max_step()
    {
        var update = TraitEvolution.Apply(Trait(0.10), signal: 50, importance: 1, Start, Bounds, Learning);

        Assert.Equal(Learning.MaxStep, update.Delta, precision: 10);
    }

    [Fact]
    public void Movement_inside_one_budget_window_never_exceeds_the_budget()
    {
        var trait = Trait(0.40);
        double total = 0;
        for (var i = 0; i < 1000; i++)
        {
            var update = TraitEvolution.Apply(trait, signal: 1, importance: 1, Start.AddSeconds(i), Bounds, Learning);
            total += Math.Abs(update.Delta);
            trait = update.Trait;
        }

        Assert.True(total <= Learning.BudgetPerWindow + 1e-9, $"moved {total}");
        Assert.Equal(0.40 + Learning.BudgetPerWindow, trait.Value, precision: 9);
    }

    [Fact]
    public void Budget_resets_when_the_window_elapses()
    {
        var trait = Trait(0.40) with { BudgetUsed = Learning.BudgetPerWindow };

        var blocked = TraitEvolution.Apply(trait, 1, 1, Start.AddHours(23), Bounds, Learning);
        var allowed = TraitEvolution.Apply(trait, 1, 1, Start.AddHours(24), Bounds, Learning);

        Assert.Equal(0, blocked.Delta);
        Assert.Same(trait, blocked.Trait);
        Assert.True(allowed.Delta > 0);
        Assert.Equal(Start.AddHours(24), allowed.Trait.BudgetWindowStart);
        Assert.Equal(allowed.Delta, allowed.Trait.BudgetUsed, precision: 10);
    }

    [Fact]
    public void A_burst_across_a_window_boundary_is_capped_at_twice_the_budget()
    {
        var trait = TraitEvolution.Apply(Trait(0.40), 1, 1, Start, Bounds, Learning).Trait;
        var anchor = trait.BudgetWindowStart;
        double moved = 0;
        var at = anchor + Learning.BudgetWindow - TimeSpan.FromMinutes(5);
        for (var i = 0; i < 200; i++, at += TimeSpan.FromSeconds(5))
        {
            var update = TraitEvolution.Apply(trait, 1, 1, at, Bounds, Learning);
            moved += Math.Abs(update.Delta);
            trait = update.Trait;
        }

        // Documented worst case of the fixed window (docs/personality-model.md, property 3).
        Assert.True(moved <= (2 * Learning.BudgetPerWindow) + 1e-9, $"moved {moved}");
        Assert.True(moved > Learning.BudgetPerWindow, "the fixed window allows more than one budget at a boundary");
    }

    [Fact]
    public void Thousands_of_experiences_over_a_year_keep_the_trait_within_bounds_and_monotone()
    {
        var trait = Trait(0.40);
        for (var i = 0; i < 10_000; i++)
        {
            var update = TraitEvolution.Apply(trait, 1, 1, Start.AddHours(i), Bounds, Learning);
            Assert.True(update.Delta >= 0);
            Assert.InRange(update.Trait.Value, Bounds.TraitMin, Bounds.TraitMax);
            trait = update.Trait;
        }

        Assert.True(trait.Value > 0.90, $"expected near max, got {trait.Value}");
    }

    [Fact]
    public void Alternating_experiences_do_not_amplify_into_oscillations()
    {
        var trait = Trait(0.50);
        for (var i = 0; i < 1000; i++)
        {
            var signal = i % 2 == 0 ? 1.0 : -1.0;
            var update = TraitEvolution.Apply(trait, signal, 1, Start.AddMinutes(i * 30), Bounds, Learning);
            Assert.True(Math.Abs(update.Delta) <= Learning.MaxStep + 1e-12);
            trait = update.Trait;
        }

        Assert.InRange(trait.Value, 0.50 - Learning.MaxStep, 0.50 + Learning.MaxStep);
    }

    [Fact]
    public void Changes_below_the_minimum_are_discarded()
    {
        var trait = Trait(0.40);

        var update = TraitEvolution.Apply(trait, signal: 1e-9, importance: 1, Start, Bounds, Learning);

        Assert.Equal(0, update.Delta);
        Assert.Same(trait, update.Trait);
    }

    [Fact]
    public void A_trait_at_its_bound_cannot_cross_it()
    {
        var update = TraitEvolution.Apply(Trait(Bounds.TraitMax), 1, 1, Start, Bounds, Learning);

        Assert.Equal(0, update.Delta);
        Assert.Equal(Bounds.TraitMax, update.Trait.Value);
    }
}
