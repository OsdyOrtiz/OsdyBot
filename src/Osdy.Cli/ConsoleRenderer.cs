using System.Globalization;
using Osdy.Core;
using Osdy.Core.Decisions;
using Osdy.Core.Runtime;

namespace Osdy.Cli;

/// <summary>Renders brain activity in the tagged console format of the project brief.</summary>
public sealed class ConsoleRenderer(TextWriter output, Func<DateTimeOffset, string>? clock = null) : IBrainObserver
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public void Starting() => output.WriteLine("[BRAIN] Iniciando...");

    public void Booted(BootResult boot, MemoryStats stats)
    {
        var identity = boot.Brain.Identity;
        output.WriteLine(boot.Created
            ? $"[IDENTITY] Identidad creada: {identity.Name} ({identity.Id})"
            : $"[IDENTITY] Identidad cargada: {identity.Name} ({identity.Id}, creada {identity.CreatedAt.ToString("u", Invariant)})");
        output.WriteLine($"[MEMORY] Experiencias recuperadas: {stats.Experiences} (eventos {stats.Events}, decisiones {stats.Decisions}, cambios de rasgos {stats.TraitChanges})");

        var stateWritten = false;
        if (boot.Startup.Decision is { } decision)
        {
            WriteDecision(decision);
            stateWritten = decision.ChangesState;
        }

        WriteSnapshot(boot.Brain.Snapshot, includeState: !stateWritten);
    }

    public void WriteSnapshot(BrainSnapshot snapshot) => WriteSnapshot(snapshot, includeState: true);

    private void WriteSnapshot(BrainSnapshot snapshot, bool includeState)
    {
        if (includeState)
        {
            output.WriteLine($"[STATE] {snapshot.Runtime.State}");
        }

        output.WriteLine($"[ENERGY] {N(snapshot.Runtime.Energy)}");
        foreach (var trait in snapshot.Personality.Traits)
        {
            output.WriteLine($"[TRAIT] {trait.Kind}: {N(trait.Value)}");
        }
    }

    public void OnStep(StepResult result)
    {
        if (clock is not null)
        {
            output.WriteLine($"[TIME] {clock(result.At)}");
        }

        if (result.Event is { } evt)
        {
            output.WriteLine($"[EVENT] {evt.Type}");
        }

        if (result.Decision is { } decision)
        {
            WriteDecision(decision);
        }

        if (result.Experience is { } experience)
        {
            output.WriteLine(
                $"[EXPERIENCE] Saved ({experience.Kind}, valence {experience.Valence.ToString("+0.00;-0.00", Invariant)}, importance {N(experience.Importance)})");
        }

        foreach (var change in result.TraitChanges)
        {
            output.WriteLine($"[LEARNING] {change.Trait} {Delta(change.Delta)} ({change.Rule})");
        }
    }

    public void OnError(Exception exception) => output.WriteLine($"[ERROR] {exception.GetType().Name}: {exception.Message}");

    private void WriteDecision(Decision decision)
    {
        output.WriteLine($"[DECISION] {decision.Action} ({decision.Rule})");
        foreach (var reason in decision.Reasons)
        {
            output.WriteLine($"[REASON] {reason}");
        }

        if (decision.ChangesState)
        {
            output.WriteLine($"[STATE] {decision.ToState}");
        }
    }

    public static string N(double value) => value.ToString("0.00", Invariant);

    /// <summary>Three decimals like the brief's example; four when the change would otherwise print as zero.</summary>
    public static string Delta(double delta) =>
        delta.ToString(Math.Abs(delta) < 0.0005 ? "+0.0000;-0.0000" : "+0.000;-0.000", Invariant);
}
