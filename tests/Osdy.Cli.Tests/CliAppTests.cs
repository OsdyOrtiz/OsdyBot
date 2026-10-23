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

    [Theory]
    [InlineData(new[] { "dance" }, "Comando no válido: dance")]
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

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private Task<CliResult> Run(params string[] args) => Run(args, input: "");

    private async Task<CliResult> Run(string[] args, string input)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = _directory; // keeps the default database path inside the temp folder
        try
        {
            var app = new CliApp(new StringReader(input), TextWriter.Synchronized(output), TextWriter.Synchronized(error), new FakeTimeProvider(T0));
            var code = await app.RunAsync(args, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
            return new CliResult(code, output.ToString(), error.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    /// <summary>Each event with the decision it caused. Exports hold only events, so trailing waits are not replayed.</summary>
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

    private sealed record CliResult(int ExitCode, string Output, string Error);
}
