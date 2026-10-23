using System.Globalization;
using Osdy.Core;
using Osdy.Core.Runtime;

namespace Osdy.Cli;

/// <summary>Versioned export contract, independent of domain serialization and SQLite schema versions.</summary>
internal sealed record MemoryExport(
    int SchemaVersion,
    string ExportedAt,
    IdentityExport Identity,
    IReadOnlyList<TraitExport> Traits,
    RuntimeExport Runtime,
    IReadOnlyList<EventExport> Events,
    IReadOnlyList<DecisionExport> Decisions,
    IReadOnlyList<ExperienceExport> Experiences,
    IReadOnlyList<TraitChangeExport> TraitChanges)
{
    public static MemoryExport Create(BrainSnapshot snapshot, IBrainStore store, DateTimeOffset exportedAt)
    {
        var identity = snapshot.Identity;
        var runtime = snapshot.Runtime;
        return new MemoryExport(
            1,
            Timestamp(exportedAt),
            new IdentityExport(identity.Id, identity.Name, Timestamp(identity.CreatedAt)),
            snapshot.Personality.Traits.Select(t => new TraitExport(t.Kind.ToString(), t.Value, t.Baseline)).ToArray(),
            new RuntimeExport(runtime.State.ToString(), runtime.Energy, Timestamp(runtime.StateEnteredAt),
                Timestamp(runtime.LastUpdatedAt), OptionalTimestamp(runtime.LastSpontaneousAt), OptionalTimestamp(runtime.LastInteractionAt)),
            store.GetEvents().Select(e => new EventExport(e.Id, e.Type.ToString(), Timestamp(e.OccurredAt), e.Source)).ToArray(),
            store.GetRecentDecisions(int.MaxValue).Select(d => new DecisionExport(
                d.Id, Timestamp(d.OccurredAt), d.Trigger.ToString(), d.EventId, d.Kind.ToString(), d.Rule,
                d.Action.ToString(), d.FromState.ToString(), d.ToState.ToString(), d.Reasons)).ToArray(),
            store.GetRecentExperiences(int.MaxValue).Select(e => new ExperienceExport(
                e.Id, Timestamp(e.OccurredAt), e.EventId, e.DecisionId, e.Kind.ToString(), e.State.ToString(),
                e.Action.ToString(), e.Valence, e.Importance, e.Outcome, e.Source)).ToArray(),
            store.GetRecentTraitChanges(int.MaxValue).Select(c => new TraitChangeExport(
                c.Id, Timestamp(c.OccurredAt), c.Trait.ToString(), c.OldValue, c.NewValue, c.Delta, c.ExperienceId, c.Rule)).ToArray());
    }

    private static string Timestamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static string? OptionalTimestamp(DateTimeOffset? value) => value is { } at ? Timestamp(at) : null;
}

internal sealed record IdentityExport(Guid Id, string Name, string CreatedAt);
internal sealed record TraitExport(string Kind, double Value, double Baseline);
internal sealed record RuntimeExport(
    string State, double Energy, string StateEnteredAt, string LastUpdatedAt, string? LastSpontaneousAt, string? LastInteractionAt);
internal sealed record EventExport(Guid Id, string Type, string OccurredAt, string Source);
internal sealed record DecisionExport(
    Guid Id, string OccurredAt, string Trigger, Guid? EventId, string Kind, string Rule,
    string Action, string FromState, string ToState, IReadOnlyList<string> Reasons);
internal sealed record ExperienceExport(
    Guid Id, string OccurredAt, Guid EventId, Guid DecisionId, string Kind, string State,
    string Action, double Valence, double Importance, string Outcome, string Source);
internal sealed record TraitChangeExport(
    Guid Id, string OccurredAt, string Trait, double OldValue, double NewValue, double Delta, Guid ExperienceId, string Rule);
