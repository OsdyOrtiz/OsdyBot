using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Osdy.Cli.Tests;

public sealed class CliAppTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private const string Meeting = """
        00:00:05 HumanPresenceDetected
        00:00:10 HumanInteractionDetected
        00:00:20 PositiveInteraction
        00:01:00 InteractionEnded
        00:03:00 NoveltyDetected
        00:05:00 wait
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "osdy-cli-" + Guid.NewGuid().ToString("N"));

    public CliAppTests() => Directory.CreateDirectory(_directory);

    private string Db => Path.Combine(_directory, "brain.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Simulate_prints_the_tagged_trace_of_the_brief()
    {
        var result = await Run("simulate", Write("meeting.txt", Meeting));

        Assert.Equal(CliApp.Ok, result.ExitCode);
        AssertInOrder(result.Output,
            "[BRAIN] Iniciando...",
            "[IDENTITY] Identidad creada: Osdy",
            "[MEMORY] Experiencias recuperadas: 0",
            "[STATE] Idle",
            "[TRAIT] Curiosity: 0.70",
            "[TRAIT] Sociability: 0.35",
            "[TRAIT] Confidence: 0.40",
            "[EVENT] HumanInteractionDetected",
            "[DECISION] ObserveAndEngage (human-interaction)",
            "[REASON] Engagement 0.47 >= 0.45",
            "[STATE] Interacting",
            "[EVENT] PositiveInteraction",
            "[EXPERIENCE] Saved (PositiveInteraction",
            "[LEARNING] Confidence +0.002 (positive-interaction)",
            "[EVENT] InteractionEnded",
            "[STATE] Idle",
            "[SIMULATION] Fin tras 00:05:00 de tiempo virtual");
        Assert.Empty(result.Error);
        Assert.False(File.Exists(Path.Combine(_directory, "data", "osdy-brain.db")));
    }

    [Fact]
    public async Task Simulating_into_a_database_survives_restarts_and_status_explains_it()
    {
        var scenario = Write("meeting.txt", Meeting);

        var first = await Run("simulate", scenario, "--db", Db);
        var second = await Run("simulate", scenario, "--db", Db);
        var status = await Run("status", "--db", Db, "--count", "3");

        Assert.Equal((CliApp.Ok, CliApp.Ok, CliApp.Ok), (first.ExitCode, second.ExitCode, status.ExitCode));
        Assert.Contains("[IDENTITY] Identidad cargada: Osdy", second.Output, StringComparison.Ordinal);
        Assert.Contains("[MEMORY] Experiencias recuperadas: 2 (eventos 5,", second.Output, StringComparison.Ordinal);
        AssertInOrder(status.Output,
            "[IDENTITY] Osdy",
            "[TRAIT] Sociability: 0.36 (base 0.35, +0.007)",
            "[MEMORY] eventos 10,",
            "-- Últimas 3 decisiones --",
            "    porque: ",
            "-- Últimas 3 experiencias --",
            "NoveltyExplored valence +0.50: Investigate -> Curious",
            "-- Últimos 3 cambios de rasgos --",
            "(novelty-explored, experiencia ");
    }

    [Fact]
    public async Task Exported_events_replay_with_the_same_reactions()
    {
        var original = await Run("simulate", Write("meeting.txt", Meeting), "--db", Db);
        var exported = Path.Combine(_directory, "exported.txt");

        var export = await Run("events", "export", exported, "--db", Db);
        var replay = await Run("simulate", exported);

        Assert.Equal(CliApp.Ok, export.ExitCode);
        Assert.Contains($"[EVENTS] 5 eventos exportados a {exported}", export.Output, StringComparison.Ordinal);
        Assert.StartsWith("# Exported from", File.ReadAllText(exported), StringComparison.Ordinal);
        Assert.Equal(Reactions(original.Output), Reactions(replay.Output));
    }

    [Fact]
    public async Task Export_keeps_sub_second_offsets_and_the_trailing_wait()
    {
        await Run("simulate", Write("precise.txt", "00:00:05.5 HumanInteractionDetected\n00:00:20 PositiveInteraction\n00:05:00 wait\n"), "--db", Db);
        var exported = Path.Combine(_directory, "exported.txt");

        var export = await Run("events", "export", exported, "--db", Db);

        Assert.Equal(CliApp.Ok, export.ExitCode);
        Assert.Equal($"[EVENTS] 2 eventos exportados a {exported}", export.Output.TrimEnd());
        Assert.Equal(
            ["00:00:00 HumanInteractionDetected", "00:00:14.5 PositiveInteraction", "00:04:54.5 wait"],
            File.ReadAllLines(exported).Skip(1));
    }

    [Fact]
    public async Task Export_writes_all_saved_memory_as_a_versioned_json_document()
    {
        await Run("simulate", Write("meeting.txt", Meeting), "--db", Db);
        var status = await Run("status", "--db", Db);
        var path = Path.Combine(_directory, "memory.json");
        var before = File.ReadAllBytes(Db);

        var result = await Run("export", path, "--db", Db);

        Assert.Equal(CliApp.Ok, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.DoesNotContain("\\u002B", File.ReadAllText(path), StringComparison.Ordinal); // timestamps stay readable: +00:00
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(T0.ToString("O", CultureInfo.InvariantCulture), root.GetProperty("exportedAt").GetString());
        Assert.Equal("Osdy", root.GetProperty("identity").GetProperty("name").GetString());
        Assert.Equal(3, root.GetProperty("traits").GetArrayLength());
        var curiosity = root.GetProperty("traits").EnumerateArray().Single(t => t.GetProperty("kind").GetString() == "Curiosity");
        Assert.Equal(0.7, curiosity.GetProperty("baseline").GetDouble());
        Assert.True(curiosity.GetProperty("value").GetDouble() > 0.7);
        Assert.Equal("Idle", root.GetProperty("runtime").GetProperty("state").GetString());
        Assert.True(root.GetProperty("runtime").GetProperty("energy").GetDouble() > 0);
        Assert.Equal(T0.AddMinutes(5).ToString("O", CultureInfo.InvariantCulture),
            root.GetProperty("runtime").GetProperty("lastUpdatedAt").GetString());
        var events = root.GetProperty("events");
        var decisions = root.GetProperty("decisions");
        var experiences = root.GetProperty("experiences");
        var changes = root.GetProperty("traitChanges");
        Assert.Contains($"[MEMORY] eventos {events.GetArrayLength()}, decisiones {decisions.GetArrayLength()}, experiencias {experiences.GetArrayLength()}, cambios de rasgos {changes.GetArrayLength()}",
            status.Output, StringComparison.Ordinal);
        var count = events.GetArrayLength() + decisions.GetArrayLength() + experiences.GetArrayLength() + changes.GetArrayLength();
        Assert.Equal($"[EXPORT] {count} registros exportados a {path}{Environment.NewLine}", result.Output);
        Assert.Equal("HumanPresenceDetected", events[0].GetProperty("type").GetString());
        Assert.Equal("PositiveInteraction", experiences[0].GetProperty("kind").GetString());
        Assert.Equal("positive-interaction", changes[0].GetProperty("rule").GetString());
        var interaction = decisions.EnumerateArray().Single(d => d.GetProperty("rule").GetString() == "human-interaction");
        Assert.Contains("Energy sufficient: 1.00 >= 0.30",
            interaction.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));
        foreach (var rows in new[] { events, decisions, experiences, changes })
        {
            var dates = rows.EnumerateArray().Select(r => DateTimeOffset.ParseExact(
                r.GetProperty("occurredAt").GetString()!, "O", CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(dates.Order(), dates);
        }
        Assert.Equal(before, File.ReadAllBytes(Db));
    }

    [Fact]
    public async Task Export_refuses_an_existing_output_file_without_overwriting_it()
    {
        await Run("simulate", Write("meeting.txt", Meeting), "--db", Db);
        var path = Write("existing.json", "keep this file");

        var result = await Run("export", path, "--db", Db);

        Assert.Equal(CliApp.Failure, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal($"[ERROR] El archivo {path} ya existe; elegí otro nombre para el export.", result.Error.TrimEnd());
        Assert.Equal("keep this file", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_of_a_missing_or_empty_database_fails_without_writing(bool empty)
    {
        if (empty)
        {
            Write("brain.db", "");
        }
        var path = Path.Combine(_directory, "memory.json");

        var result = await Run("export", path, "--db", Db);

        Assert.Equal(CliApp.Failure, result.ExitCode);
        Assert.Empty(result.Output);
        var message = empty ? $"No hay ningún cerebro guardado en {Db}" : $"No existe la base de datos {Db}";
        Assert.Equal($"[ERROR] {message}{Environment.NewLine}", result.Error);
        Assert.False(File.Exists(path));
        if (empty)
        {
            Assert.Equal(0, new FileInfo(Db).Length);
        }
        else
        {
            Assert.False(File.Exists(Db));
        }
    }

    [Fact]
    public async Task Run_processes_console_keys_and_saves_state_on_quit()
    {
        var result = await Run(["run", "--db", Db], input: "h\n+\nx\ns\nq\n");

        Assert.Equal(CliApp.Ok, result.ExitCode);
        AssertInOrder(result.Output,
            "[IDENTITY] Identidad creada: Osdy",
            "Tecla desconocida 'x'",
            "[BRAIN] Detenido. Estado guardado.");
        Assert.Contains("[DECISION] ObserveAndEngage", result.Output, StringComparison.Ordinal);
        Assert.Contains("[EXPERIENCE] Saved (PositiveInteraction", result.Output, StringComparison.Ordinal);

        var status = await Run("status", "--db", Db);
        Assert.Contains("[MEMORY] eventos 2, decisiones 2, experiencias 1, cambios de rasgos 2", status.Output, StringComparison.Ordinal);
        Assert.Contains("[STATE] Interacting", status.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_refuses_a_database_saved_ahead_of_the_current_time()
    {
        await Run("simulate", Write("day.txt", "00:00:05 HumanPresenceDetected\n1.00:00:00 wait\n"), "--db", Db);
        var before = await Run("status", "--db", Db);

        var result = await Run(["run", "--db", Db], input: "h\nq\n");

        Assert.Equal(CliApp.Failure, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(
            $"[ERROR] La última actividad guardada en {Db} (2026-01-02 08:00:00Z) es posterior a la hora actual "
            + "(2026-01-01 08:00:00Z), probablemente por 'simulate --db'. Usá otra base para 'run' o esperá a esa hora.",
            result.Error.TrimEnd());
        Assert.Equal(before.Output, (await Run("status", "--db", Db)).Output);
    }

    [Fact]
    public async Task Boot_after_restart_recovery_prints_the_resumed_state_once()
    {
        await Run(["run", "--db", Db], input: "h\nq\n");

        var restart = await Run(["run", "--db", Db], input: "q\n");

        var boot = restart.Output[..restart.Output.IndexOf("Teclas:", StringComparison.Ordinal)];
        AssertInOrder(boot,
            "[IDENTITY] Identidad cargada: Osdy",
            "[DECISION] RecoverAfterRestart (restart-recovery)",
            "[REASON] State Interacting is transient; resuming as Idle",
            "[STATE] Idle",
            "[ENERGY] ",
            "[TRAIT] Curiosity: ");
        Assert.Single(boot.Split('\n'), line => line.StartsWith("[STATE]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_stops_waiting_for_input_when_the_brain_stops_after_repeated_failures()
    {
        // An interactive terminal: once its lines are read it blocks until cancelled instead of ending.
        var input = new InteractiveInput(
            onFirstRead: () => Sql(Db, "DROP TABLE events;"), // every event commit fails from now on
            "h", "l", "h", "l", "h");

        var result = await Run(["run", "--db", Db], input);

        Assert.Equal(CliApp.Failure, result.ExitCode);
        Assert.Equal("[ERROR] Brain stopped after 5 consecutive failures.", result.Error.TrimEnd());
        Assert.Equal(5, result.Output.Split('\n').Count(l => l.StartsWith("[ERROR] SqliteException", StringComparison.Ordinal)));
        Assert.DoesNotContain("[BRAIN] Detenido", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "dance" }, "Comando no válido: dance")]
    [InlineData(new[] { "export" }, "Comando no válido: export")]
    [InlineData(new string[0], "Falta el comando.")]
    [InlineData(new[] { "status", "--db" }, "Falta el valor de --db.")]
    [InlineData(new[] { "status", "--count", "0" }, "--count debe ser un entero positivo.")]
    [InlineData(new[] { "status", "--verbose" }, "Opción desconocida: --verbose")]
    public async Task Usage_errors_exit_with_code_2_and_print_usage(string[] args, string message)
    {
        var result = await Run(args);

        Assert.Equal(CliApp.Usage, result.ExitCode);
        Assert.StartsWith($"[ERROR] {message}", result.Error, StringComparison.Ordinal);
        Assert.Contains("Uso:", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_config_is_a_usage_error_with_the_validation_message()
    {
        var config = Write("bad.json", """{ "personality": { "initialCuriosity": 2 } }""");

        var result = await Run("simulate", Write("meeting.txt", Meeting), "--config", config);

        Assert.Equal(CliApp.Usage, result.ExitCode);
        Assert.Contains("Initial Curiosity (2)", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Config_overrides_are_applied()
    {
        var config = Write("pip.json", """{ "robotName": "Pip", "personality": { "initialSociability": 0.6 } }""");

        var result = await Run("simulate", Write("meeting.txt", Meeting), "--config", config);

        Assert.Contains("[IDENTITY] Identidad creada: Pip", result.Output, StringComparison.Ordinal);
        Assert.Contains("[TRAIT] Sociability: 0.60", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_errors_exit_with_code_1()
    {
        var missingScenario = await Run("simulate", Path.Combine(_directory, "missing.txt"));
        var badScenario = await Run("simulate", Write("bad.txt", "00:00:05 Dance"));
        var missingDb = await Run("status", "--db", Path.Combine(_directory, "none.db"));

        Assert.Equal((CliApp.Failure, CliApp.Failure, CliApp.Failure), (missingScenario.ExitCode, badScenario.ExitCode, missingDb.ExitCode));
        Assert.StartsWith("[ERROR] Scenario line 1: unknown event 'Dance'", badScenario.Error, StringComparison.Ordinal);
        Assert.StartsWith("[ERROR] No existe la base de datos", missingDb.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_and_export_never_write_to_an_empty_database_file()
    {
        var empty = Write("empty.db", "");

        var status = await Run("status", "--db", empty);
        var export = await Run("events", "export", Path.Combine(_directory, "out.txt"), "--db", empty);
        SqliteConnection.ClearAllPools();

        Assert.Equal((CliApp.Failure, CliApp.Failure), (status.ExitCode, export.ExitCode));
        Assert.Equal($"[ERROR] No hay ningún cerebro guardado en {empty}", status.Error.TrimEnd());
        Assert.Equal("[ERROR] No hay eventos guardados para exportar", export.Error.TrimEnd());
        Assert.Empty(status.Output + export.Output);
        Assert.Equal(0, new FileInfo(empty).Length);
        Assert.False(File.Exists(Path.Combine(_directory, "out.txt")));
    }

    [Fact]
    public async Task A_file_that_is_not_a_database_is_reported_instead_of_crashing()
    {
        var junk = Write("junk.db", "this is not sqlite, just text that is long enough to have a header....");

        var result = await Run("status", "--db", junk);

        Assert.Equal(CliApp.Failure, result.ExitCode);
        Assert.StartsWith("[ERROR] ", result.Error, StringComparison.Ordinal);
        Assert.Contains("not a database", result.Error, StringComparison.Ordinal);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private Task<CliResult> Run(params string[] args) => Run(args, input: "");

    private Task<CliResult> Run(string[] args, string input) => Run(args, new StringReader(input));

    private async Task<CliResult> Run(string[] args, TextReader input)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = _directory; // keeps the default database path inside the temp folder
        try
        {
            var app = new CliApp(input, TextWriter.Synchronized(output), TextWriter.Synchronized(error), new FakeTimeProvider(T0));
            var code = await app.RunAsync(args, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
            return new CliResult(code, output.ToString(), error.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    /// <summary>Each event with the decision it caused.</summary>
    private static List<string> Reactions(string output)
    {
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        return
        [
            .. lines.Select((line, i) => (line, i))
                .Where(x => x.line.StartsWith("[EVENT]", StringComparison.Ordinal))
                .Select(x => $"{x.line} => {lines[x.i + 1]}"),
        ];
    }

    private static void AssertInOrder(string output, params string[] fragments)
    {
        var position = 0;
        foreach (var fragment in fragments)
        {
            var index = output.IndexOf(fragment, position, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Missing (in order) '{fragment}' in:\n{output}");
            position = index + fragment.Length;
        }
    }

    private static void Sql(string database, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class InteractiveInput(Action onFirstRead, params string[] lines) : TextReader
    {
        private readonly Queue<string> _lines = new(lines);
        private bool _started;

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (!_started)
            {
                _started = true;
                onFirstRead();
            }

            if (_lines.TryDequeue(out var line))
            {
                return line;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);
}
