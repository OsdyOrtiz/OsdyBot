using Osdy.Core.Personality;

namespace Osdy.Core.Tests.Personality;

public class TraitEvolutionTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly PersonalityOptions Bounds = new();
    private static readonly LearningOptions Learning = new();

    private static TraitState Trait(double value) => new(TraitKind.Confidence, value, value, []);

    [Fact]
    public void Positive_signal_matches_the_documented_worked_example()
    {
        // Confidence 0.40, gain 0.6, valence +1, importance 0.5 → ≈ +0.0018 (printed +0.002).
        var update = TraitEvolution.Apply(Trait(0.40), signal: 0.6, importance: 0.5, Start, Bounds, Learning);

        Assert.Equal(0.01 * 0.6 * 0.5 * (0.55 / 0.90), update.Delta, precision: 10);
        Assert.Equal(0.40 + update.Delta, update.Trait.Value, precision: 10);
        Assert.Equal(new TraitBudgetEntry(Start, Math.Abs(update.Delta)), Assert.Single(update.Trait.RecentChanges));
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
    public void A_change_frees_its_budget_exactly_at_the_sliding_window_edge()
    {
        var trait = Trait(0.40) with
        {
            RecentChanges = [new(Start, Learning.BudgetPerWindow)],
        };
        var edge = Start + Learning.BudgetWindow;

        var blocked = TraitEvolution.Apply(trait, 1, 1, edge.AddTicks(-1), Bounds, Learning);
        var allowed = TraitEvolution.Apply(blocked.Trait, 1, 1, edge, Bounds, Learning);

        Assert.Equal(0, blocked.Delta);
        Assert.Same(trait, blocked.Trait);
        Assert.True(allowed.Delta > 0);
        Assert.Equal(new TraitBudgetEntry(edge, allowed.Delta), Assert.Single(allowed.Trait.RecentChanges));
        Assert.Single(trait.RecentChanges); // The previous immutable state is unchanged.
    }

    [Fact]
    public void Expired_changes_are_pruned_even_when_no_movement_is_applied()
    {
        var trait = Trait(0.40) with { RecentChanges = [new(Start, 0.01)] };

        var update = TraitEvolution.Apply(trait, 0, 1, Start + Learning.BudgetWindow, Bounds, Learning);

        Assert.Equal(0, update.Delta);
        Assert.Equal(trait.Value, update.Trait.Value);
        Assert.Empty(update.Trait.RecentChanges);
        Assert.Single(trait.RecentChanges);
    }

    [Fact]
    public void A_burst_across_a_window_boundary_is_capped_at_one_budget()
    {
        var trait = TraitEvolution.Apply(Trait(0.40), 1, 1, Start, Bounds, Learning).Trait;
        var anchor = Start;
        double moved = 0;
        var at = anchor + Learning.BudgetWindow - TimeSpan.FromMinutes(5);
        for (var i = 0; i < 200; i++, at += TimeSpan.FromSeconds(5))
        {
            var update = TraitEvolution.Apply(trait, 1, 1, at, Bounds, Learning);
            moved += Math.Abs(update.Delta);
            trait = update.Trait;
        }

        // Any window-length span is capped at one budget, including the old fixed-window boundary.
        Assert.True(moved <= Learning.BudgetPerWindow + 1e-9, $"moved {moved}, budget {Learning.BudgetPerWindow}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Only_expired_movement_is_freed_and_both_directions_consume_budget(double signal)
    {
        var retainedAt = Start.AddHours(1);
        var trait = Trait(0.40) with
        {
            RecentChanges = [new(Start, 0.001), new(retainedAt, Learning.BudgetPerWindow - 0.001)],
        };
        var at = Start + Learning.BudgetWindow;

        var update = TraitEvolution.Apply(trait, signal, 1, at, Bounds, Learning);

        Assert.Equal(signal * 0.001, update.Delta, precision: 10);
        Assert.Equal(trait.Value + update.Delta, update.Trait.Value, precision: 10);
        Assert.Equal(2, update.Trait.RecentChanges.Count);
        Assert.Equal(trait.RecentChanges[1], update.Trait.RecentChanges[0]);
        Assert.Equal(new TraitBudgetEntry(at, Math.Abs(update.Delta)), update.Trait.RecentChanges[1]);
        Assert.Equal(Learning.BudgetPerWindow, update.Trait.RecentChanges.Sum(change => change.Magnitude), precision: 10);
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
