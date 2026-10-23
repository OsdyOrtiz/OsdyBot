using System.Globalization;
using System.Text.Json;
using Osdy.Core;
using Osdy.Core.Events;
using Osdy.Core.Experiences;
using Osdy.Core.Runtime;
using Osdy.Core.Simulation;
using Osdy.Persistence.Sqlite;

namespace Osdy.Cli;

/// <summary>Command-line entry point, separated from <c>Program</c> so it can be tested with fake I/O and time.</summary>
public sealed class CliApp(TextReader input, TextWriter output, TextWriter error, TimeProvider time)
{
    public const int Ok = 0;
    public const int Failure = 1;
    public const int Usage = 2;

    public const string DefaultDatabase = "data/osdy-brain.db";

    public const string UsageText = """
        Osdy Brain — Osdy Core v0.1

        Uso:
          osdy run [--db <archivo>] [--config <archivo.json>]
              Ejecuta el cerebro en tiempo real. Teclas (Enter después de cada una):
                p  HumanPresenceDetected      l  HumanLeft
                h  HumanInteractionDetected   e  InteractionEnded
                +  PositiveInteraction        -  NegativeInteraction
                n  NoveltyDetected            s  estado actual
                q  salir (también Ctrl+C o fin de la entrada)
          osdy simulate <escenario.txt> [--db <archivo>] [--config <archivo.json>]
              Reproduce un escenario en tiempo virtual. Sin --db no guarda nada.
          osdy status [--db <archivo>] [--count <n>]
              Muestra identidad, rasgos, estado y las últimas decisiones con sus causas.
          osdy memories list [--db <archivo>] [--count <n>]
              Lista las últimas experiencias (10 por defecto), de más antigua a más reciente.
          osdy memories show <id> [--db <archivo>]
              Muestra una experiencia, su evento, decisión y cambios de rasgos.
          osdy forget <id> [--db <archivo>]
              Elimina la experiencia y su evento; conserva las acciones y el aprendizaje.
          osdy events export <escenario.txt> [--db <archivo>]
              Exporta los eventos guardados al formato de escenario para reproducirlos.
          osdy export <archivo.json> [--db <archivo>]
              Exporta toda la memoria guardada a JSON sin modificar la base de datos.

        Base de datos por defecto: data/osdy-brain.db
        """;

    // Relaxed escaping keeps '+' in timestamps and accents readable; the file is data, never embedded in HTML.
    private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Dictionary<string, EventType> Keys = new(StringComparer.Ordinal)
    {
        ["p"] = EventType.HumanPresenceDetected,
        ["l"] = EventType.HumanLeft,
        ["h"] = EventType.HumanInteractionDetected,
        ["e"] = EventType.InteractionEnded,
        ["+"] = EventType.PositiveInteraction,
        ["-"] = EventType.NegativeInteraction,
        ["n"] = EventType.NoveltyDetected,
    };

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var command = Arguments.Parse(args);
            return command.Name switch
            {
                "run" when command.Positional.Count == 0 => await RunBrainAsync(command, cancellationToken).ConfigureAwait(false),
                "simulate" when command.Positional.Count == 1 => Simulate(command),
                "status" when command.Positional.Count == 0 => Status(command),
                "events" when command.Positional is ["export", _] => ExportEvents(command),
                "export" when command.Positional.Count == 1 => ExportMemory(command),
                "memories" when command.Positional is ["list"] => Memories(command, null),
                "memories" when command.Positional is ["show", var id] => Memories(command, MemoryId(id)),
                "forget" when command.Positional is [var id] => Forget(command, MemoryId(id)),
                "help" or "--help" or "-h" => Help(),
                _ => UsageError($"Comando no válido: {string.Join(' ', args)}"),
            };
        }
        catch (ArgumentException e)
        {
            return UsageError(e.Message);
        }
        catch (Exception e) when (e is IOException or FormatException or JsonException or InvalidOperationException
                                       or UnauthorizedAccessException or System.Data.Common.DbException)
        {
            await error.WriteLineAsync($"[ERROR] {e.Message}").ConfigureAwait(false);
            return Failure;
        }
    }

    private async Task<int> RunBrainAsync(Arguments command, CancellationToken cancellationToken)
    {
        var options = LoadOptions(command);
        using var store = new SqliteBrainStore(command.Database);
        var now = time.GetUtcNow();
        if (store.Load() is { } saved && saved.Runtime.LastUpdatedAt > now)
        {
            // A live brain cannot run before its own past: it would stay frozen until the clock caught up.
            throw new InvalidOperationException(
                $"La última actividad guardada en {command.Database} ({Utc(saved.Runtime.LastUpdatedAt)}) es posterior a la hora "
                + $"actual ({Utc(now)}), probablemente por 'simulate --db'. Usá otra base para 'run' o esperá a esa hora.");
        }

        var renderer = new ConsoleRenderer(output);
        renderer.Starting();
        var boot = BrainBootstrap.Boot(store, options, time.GetUtcNow());
        renderer.Booted(boot, store.GetStats());
        await output.WriteLineAsync("Teclas: p l h e + - n s q (ver 'osdy help')").ConfigureAwait(false);

        var runner = new BrainRunner(boot.Brain, store, renderer, time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loop = runner.RunAsync(stop.Token);
        var reading = ReadKeysAsync(runner, renderer, stop.Token);

        // Whichever ends first (quit key, end of input, Ctrl+C, or the loop giving up) stops the other.
        await Task.WhenAny(loop, reading).ConfigureAwait(false);
        await stop.CancelAsync().ConfigureAwait(false);
        await reading.ConfigureAwait(false);
        await loop.ConfigureAwait(false);
        await output.WriteLineAsync("[BRAIN] Detenido. Estado guardado.").ConfigureAwait(false);
        return Ok;
    }

    private async Task ReadKeysAsync(BrainRunner runner, ConsoleRenderer renderer, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var line = await input.ReadLineAsync(stop).ConfigureAwait(false);
                if (line is null || line.Trim() == "q")
                {
                    return;
                }

                var key = line.Trim();
                if (Keys.TryGetValue(key, out var type))
                {
                    await runner.PostAsync(type, "console", stop).ConfigureAwait(false);
                }
                else if (key == "s")
                {
                    renderer.WriteSnapshot(runner.Brain.Snapshot);
                }
                else if (key.Length > 0)
                {
                    await output.WriteLineAsync($"Tecla desconocida '{key}'. Válidas: p l h e + - n s q").ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Ctrl+C or the loop stopped.
        }
    }

    private int Simulate(Arguments command)
    {
        var options = LoadOptions(command);
        var entries = ScenarioParser.Parse(File.ReadAllText(command.Positional[0]));
        using var sqlite = command.HasDatabase ? new SqliteBrainStore(command.Database) : null;
        IBrainStore store = sqlite is null ? new InMemoryBrainStore() : sqlite;

        var stored = store.Load();
        var now = time.GetUtcNow();
        var start = stored is not null && stored.Runtime.LastUpdatedAt > now ? stored.Runtime.LastUpdatedAt : now;

        var renderer = new ConsoleRenderer(output, at => "+" + Elapsed(at - start));
        renderer.Starting();
        var boot = BrainBootstrap.Boot(store, options, start);
        renderer.Booted(boot, store.GetStats());
        var before = boot.Brain.Personality;

        var simulator = new ScenarioSimulator(boot.Brain, start, step =>
        {
            if (step.HasRecords)
            {
                store.Commit(step);
                renderer.OnStep(step);
            }
        });
        simulator.Run(entries);
        store.Commit(boot.Brain.Tick(simulator.Now));

        output.WriteLine($"[SIMULATION] Fin tras {Elapsed(simulator.Now - start)} de tiempo virtual");
        foreach (var trait in boot.Brain.Personality.Traits)
        {
            output.WriteLine($"[TRAIT] {trait.Kind}: {ConsoleRenderer.N(trait.Value)} ({ConsoleRenderer.Delta(trait.Value - before[trait.Kind].Value)} en esta simulación)");
        }

        output.WriteLine($"[STATE] {boot.Brain.Runtime.State}");
        return Ok;
    }

    private int Status(Arguments command)
    {
        if (!File.Exists(command.Database))
        {
            error.WriteLine($"[ERROR] No existe la base de datos {command.Database}");
            return Failure;
        }

        using var store = SqliteBrainStore.OpenReadOnly(command.Database);
        if (store?.Load() is not { } snapshot)
        {
            error.WriteLine($"[ERROR] No hay ningún cerebro guardado en {command.Database}");
            return Failure;
        }

        var count = command.Count;
        var stats = store.GetStats();
        var identity = snapshot.Identity;
        output.WriteLine($"[IDENTITY] {identity.Name} ({identity.Id}, creada {identity.CreatedAt.ToString("u", CultureInfo.InvariantCulture)})");
        output.WriteLine($"[STATE] {snapshot.Runtime.State} desde {snapshot.Runtime.StateEnteredAt.ToString("u", CultureInfo.InvariantCulture)}");
        output.WriteLine($"[ENERGY] {ConsoleRenderer.N(snapshot.Runtime.Energy)}");
        foreach (var trait in snapshot.Personality.Traits)
        {
            output.WriteLine($"[TRAIT] {trait.Kind}: {ConsoleRenderer.N(trait.Value)} (base {ConsoleRenderer.N(trait.Baseline)}, {ConsoleRenderer.Delta(trait.Value - trait.Baseline)})");
        }

        output.WriteLine($"[MEMORY] eventos {stats.Events}, decisiones {stats.Decisions}, experiencias {stats.Experiences}, cambios de rasgos {stats.TraitChanges}");

        output.WriteLine($"-- Últimas {count} decisiones --");
        foreach (var d in store.GetRecentDecisions(count))
        {
            output.WriteLine($"{d.OccurredAt.ToString("u", CultureInfo.InvariantCulture)} {d.Action} ({d.Rule}, {d.Kind}) {d.FromState} -> {d.ToState}");
            output.WriteLine($"    porque: {string.Join("; ", d.Reasons)}");
        }

        output.WriteLine($"-- Últimas {count} experiencias --");
        foreach (var e in store.GetRecentExperiences(count))
        {
            output.WriteLine($"{e.OccurredAt.ToString("u", CultureInfo.InvariantCulture)} {e.Kind} valence {e.Valence.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)}: {e.Outcome}");
        }

        output.WriteLine($"-- Últimos {count} cambios de rasgos --");
        foreach (var c in store.GetRecentTraitChanges(count))
        {
            WriteTraitChange(c, store);
        }

        return Ok;
    }

    private static Guid MemoryId(string text) => Guid.TryParse(text, out var id)
        ? id : throw new ArgumentException($"Id de experiencia no válido: {text}");

    private int Memories(Arguments command, Guid? id)
    {
        if (!File.Exists(command.Database))
        {
            error.WriteLine($"[ERROR] No existe la base de datos {command.Database}");
            return Failure;
        }

        using var store = SqliteBrainStore.OpenReadOnly(command.Database);
        if (store?.Load() is null)
        {
            error.WriteLine($"[ERROR] No hay ningún cerebro guardado en {command.Database}");
            return Failure;
        }

        if (id is null)
        {
            foreach (var memory in store.GetRecentExperiences(command.Count))
            {
                WriteMemory(memory);
            }
            return Ok;
        }

        if (store.GetExperience(id.Value) is not { } experience)
        {
            return UnknownMemory(id.Value);
        }

        WriteMemory(experience);
        var evt = store.GetEvents().Single(e => e.Id == experience.EventId);
        output.WriteLine($"[EVENT] {evt.Id} {evt.Type} {Utc(evt.OccurredAt)} fuente: {evt.Source}");
        var decision = store.GetRecentDecisions(int.MaxValue).Single(d => d.Id == experience.DecisionId);
        output.WriteLine($"[DECISION] {decision.Action} ({decision.Rule})");
        output.WriteLine($"    porque: {string.Join("; ", decision.Reasons)}");
        foreach (var change in store.GetRecentTraitChanges(int.MaxValue).Where(c => c.ExperienceId == experience.Id))
        {
            WriteTraitChange(change, store);
        }
        return Ok;
    }

    private int Forget(Arguments command, Guid id)
    {
        // Inspect first without creating or migrating a database, including on unknown ids.
        Experience experience;
        if (!File.Exists(command.Database))
        {
            error.WriteLine($"[ERROR] No existe la base de datos {command.Database}");
            return Failure;
        }
        using (var readOnly = SqliteBrainStore.OpenReadOnly(command.Database))
        {
            if (readOnly?.GetExperience(id) is not { } found)
            {
                return UnknownMemory(id);
            }
            experience = found;
        }
        using var store = new SqliteBrainStore(command.Database);
        if (!store.ForgetExperience(id))
        {
            return UnknownMemory(id);
        }
        output.WriteLine($"[MEMORY] Experiencia {id} y evento {experience.EventId} eliminados; aprendizaje conservado.");
        return Ok;
    }

    private int UnknownMemory(Guid id)
    {
        error.WriteLine($"[ERROR] No existe la experiencia {id}");
        return Failure;
    }

    private void WriteMemory(Experience memory) => output.WriteLine(
        $"[MEMORY] {memory.Id} {Utc(memory.OccurredAt)} {memory.Kind} valence {memory.Valence.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)}: {memory.Outcome}");

    private void WriteTraitChange(TraitChange change, SqliteBrainStore store)
    {
        var tombstone = store.GetExperience(change.ExperienceId) is null ? " (olvidada)" : "";
        output.WriteLine($"{Utc(change.OccurredAt)} {change.Trait} {ConsoleRenderer.Delta(change.Delta)} -> {change.NewValue.ToString("0.0000", CultureInfo.InvariantCulture)} ({change.Rule}, experiencia {change.ExperienceId}{tombstone})");
    }

    private int ExportMemory(Arguments command)
    {
        if (!File.Exists(command.Database))
        {
            error.WriteLine($"[ERROR] No existe la base de datos {command.Database}");
            return Failure;
        }

        using var store = SqliteBrainStore.OpenReadOnly(command.Database);
        if (store?.Load() is not { } snapshot)
        {
            error.WriteLine($"[ERROR] No hay ningún cerebro guardado en {command.Database}");
            return Failure;
        }

        var memory = MemoryExport.Create(snapshot, store, time.GetUtcNow());
        var path = command.Positional[0];
        // Never overwrite an existing file: the output could alias the database (even through a link).
        FileStream destination;
        try
        {
            destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        }
        catch (IOException) when (File.Exists(path) || Directory.Exists(path))
        {
            error.WriteLine($"[ERROR] El archivo {path} ya existe; elegí otro nombre para el export.");
            return Failure;
        }

        using (destination)
        {
            JsonSerializer.Serialize(destination, memory, ExportJsonOptions);
        }

        var count = (long)memory.Events.Count + memory.Decisions.Count + memory.Experiences.Count + memory.TraitChanges.Count;
        output.WriteLine($"[EXPORT] {count.ToString(CultureInfo.InvariantCulture)} registros exportados a {path}");
        return Ok;
    }

    private int ExportEvents(Arguments command)
    {
        if (!File.Exists(command.Database))
        {
            error.WriteLine($"[ERROR] No existe la base de datos {command.Database}");
            return Failure;
        }

        using var store = SqliteBrainStore.OpenReadOnly(command.Database);
        var events = store?.GetEvents() ?? [];
        if (events.Count == 0)
        {
            error.WriteLine("[ERROR] No hay eventos guardados para exportar");
            return Failure;
        }

        // The final wait keeps the time the brain kept running after its last event (ticks are not stored as events).
        var first = events[0].OccurredAt;
        var entries = events.Select(e => new ScenarioEntry(e.OccurredAt - first, e.Type)).ToList();
        if (store!.Load() is { } snapshot && snapshot.Runtime.LastUpdatedAt > events[^1].OccurredAt)
        {
            entries.Add(new ScenarioEntry(snapshot.Runtime.LastUpdatedAt - first, null));
        }

        var path = command.Positional[1];
        File.WriteAllText(
            path,
            $"# Exported from {command.Database}; first event at {first.ToString("O", CultureInfo.InvariantCulture)}\n" + ScenarioParser.Format(entries));
        output.WriteLine($"[EVENTS] {events.Count} eventos exportados a {path}");
        return Ok;
    }

    private static string Utc(DateTimeOffset value) => value.ToString("u", CultureInfo.InvariantCulture);

    private static string Elapsed(TimeSpan value) =>
        value.ToString(value.Days > 0 ? @"d\.hh\:mm\:ss" : @"hh\:mm\:ss", CultureInfo.InvariantCulture);

    private static BrainOptions LoadOptions(Arguments command) =>
        command.Config is { } path ? BrainOptionsJson.Parse(File.ReadAllText(path)) : new BrainOptions();

    private int Help()
    {
        output.WriteLine(UsageText);
        return Ok;
    }

    private int UsageError(string message)
    {
        error.WriteLine($"[ERROR] {message}");
        error.WriteLine(UsageText);
        return Usage;
    }

    private sealed record Arguments(string Name, IReadOnlyList<string> Positional, string? DatabaseOption, string? Config, int Count)
    {
        public bool HasDatabase => DatabaseOption is not null;

        public string Database => DatabaseOption ?? DefaultDatabase;

        public static Arguments Parse(string[] args)
        {
            if (args.Length == 0)
            {
                throw new ArgumentException("Falta el comando.");
            }

            var positional = new List<string>();
            string? database = null;
            string? config = null;
            var count = 10;
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--db":
                        database = Value(args, ref i);
                        break;
                    case "--config":
                        config = Value(args, ref i);
                        break;
                    case "--count":
                        if (!int.TryParse(Value(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1)
                        {
                            throw new ArgumentException("--count debe ser un entero positivo.");
                        }

                        break;
                    case var option when option.StartsWith("--", StringComparison.Ordinal):
                        throw new ArgumentException($"Opción desconocida: {option}");
                    default:
                        positional.Add(args[i]);
                        break;
                }
            }

            return new Arguments(args[0], positional, database, config, count);
        }

        private static string Value(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Falta el valor de {args[i]}.");
            }

            return args[++i];
        }
    }
}
