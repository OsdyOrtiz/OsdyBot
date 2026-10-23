using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Osdy.Core;
using Osdy.Core.Decisions;
using Osdy.Core.Events;
using Osdy.Core.Experiences;
using Osdy.Core.Identity;
using Osdy.Core.Personality;
using Osdy.Core.Runtime;
using Osdy.Core.States;

namespace Osdy.Persistence.Sqlite;

/// <summary>
/// <see cref="IBrainStore"/> over a single SQLite file. Not thread-safe: the runtime uses it from one consumer loop.
/// </summary>
public sealed class SqliteBrainStore : IBrainStore, IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteBrainStore(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _connection.Open();
        try
        {
            Initialize();
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    public void Dispose() => _connection.Dispose();

    public BrainSnapshot? Load()
    {
        using var identityCommand = Command("SELECT id, name, created_at FROM identity LIMIT 1");
        using var identityReader = identityCommand.ExecuteReader();
        if (!identityReader.Read())
        {
            return null;
        }

        var identity = new RobotIdentity(
            Guid.Parse(identityReader.GetString(0)),
            identityReader.GetString(1),
            Time(identityReader.GetString(2)));

        var traits = new List<TraitState>();
        using (var traitsCommand = Command("SELECT kind, value, baseline, budget_window_start, budget_used FROM traits"))
        using (var reader = traitsCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                traits.Add(new TraitState(
                    Enum.Parse<TraitKind>(reader.GetString(0)),
                    reader.GetDouble(1),
                    reader.GetDouble(2),
                    Time(reader.GetString(3)),
                    reader.GetDouble(4)));
            }
        }

        using var runtimeCommand = Command(
            "SELECT state, state_entered_at, energy, last_updated_at, last_spontaneous_at, last_interaction_at FROM runtime_state WHERE id = 1");
        using var runtimeReader = runtimeCommand.ExecuteReader();
        if (!runtimeReader.Read())
        {
            throw new InvalidOperationException("Database has an identity but no runtime state; it may be corrupted.");
        }

        var runtime = new RuntimeState(
            Enum.Parse<BrainState>(runtimeReader.GetString(0)),
            Time(runtimeReader.GetString(1)),
            runtimeReader.GetDouble(2),
            Time(runtimeReader.GetString(3)),
            OptionalTime(runtimeReader, 4),
            OptionalTime(runtimeReader, 5));

        return new BrainSnapshot(identity, PersonalityProfile.Restore(traits), runtime);
    }

    public void Commit(StepResult result)
    {
        using var transaction = _connection.BeginTransaction();
        var snapshot = result.Snapshot;

        Execute(
            "INSERT OR IGNORE INTO identity (id, name, created_at) VALUES ($id, $name, $created)",
            ("$id", snapshot.Identity.Id.ToString()),
            ("$name", snapshot.Identity.Name),
            ("$created", Text(snapshot.Identity.CreatedAt)));

        foreach (var trait in snapshot.Personality.Traits)
        {
            Execute(
                """
                INSERT INTO traits (kind, value, baseline, budget_window_start, budget_used)
                VALUES ($kind, $value, $baseline, $start, $used)
                ON CONFLICT (kind) DO UPDATE SET value = $value, baseline = $baseline,
                    budget_window_start = $start, budget_used = $used
                """,
                ("$kind", trait.Kind.ToString()),
                ("$value", trait.Value),
                ("$baseline", trait.Baseline),
                ("$start", Text(trait.BudgetWindowStart)),
                ("$used", trait.BudgetUsed));
        }

        var runtime = snapshot.Runtime;
        Execute(
            """
            INSERT INTO runtime_state (id, state, state_entered_at, energy, last_updated_at, last_spontaneous_at, last_interaction_at)
            VALUES (1, $state, $entered, $energy, $updated, $spontaneous, $interaction)
            ON CONFLICT (id) DO UPDATE SET state = $state, state_entered_at = $entered, energy = $energy,
                last_updated_at = $updated, last_spontaneous_at = $spontaneous, last_interaction_at = $interaction
            """,
            ("$state", runtime.State.ToString()),
            ("$entered", Text(runtime.StateEnteredAt)),
            ("$energy", runtime.Energy),
            ("$updated", Text(runtime.LastUpdatedAt)),
            ("$spontaneous", OptionalText(runtime.LastSpontaneousAt)),
            ("$interaction", OptionalText(runtime.LastInteractionAt)));

        if (result.Event is { } evt)
        {
            Execute(
                "INSERT INTO events (id, type, occurred_at, source) VALUES ($id, $type, $at, $source)",
                ("$id", evt.Id.ToString()),
                ("$type", evt.Type.ToString()),
                ("$at", Text(evt.OccurredAt)),
                ("$source", evt.Source));
        }

        if (result.Decision is { } d)
        {
            Execute(
                """
                INSERT INTO decisions (id, occurred_at, trigger, event_id, kind, rule, action, from_state, to_state, reasons)
                VALUES ($id, $at, $trigger, $event, $kind, $rule, $action, $from, $to, $reasons)
                """,
                ("$id", d.Id.ToString()),
                ("$at", Text(d.OccurredAt)),
                ("$trigger", d.Trigger.ToString()),
                ("$event", d.EventId?.ToString()),
                ("$kind", d.Kind.ToString()),
                ("$rule", d.Rule),
                ("$action", d.Action.ToString()),
                ("$from", d.FromState.ToString()),
                ("$to", d.ToState.ToString()),
                ("$reasons", JsonSerializer.Serialize(d.Reasons)));
        }

        if (result.Experience is { } e)
        {
            Execute(
                """
                INSERT INTO experiences (id, occurred_at, event_id, decision_id, kind, state, action, valence, importance, outcome, source)
                VALUES ($id, $at, $event, $decision, $kind, $state, $action, $valence, $importance, $outcome, $source)
                """,
                ("$id", e.Id.ToString()),
                ("$at", Text(e.OccurredAt)),
                ("$event", e.EventId.ToString()),
                ("$decision", e.DecisionId.ToString()),
                ("$kind", e.Kind.ToString()),
                ("$state", e.State.ToString()),
                ("$action", e.Action.ToString()),
                ("$valence", e.Valence),
                ("$importance", e.Importance),
                ("$outcome", e.Outcome),
                ("$source", e.Source));
        }

        foreach (var c in result.TraitChanges)
        {
            Execute(
                """
                INSERT INTO trait_changes (id, occurred_at, trait, old_value, new_value, delta, experience_id, rule)
                VALUES ($id, $at, $trait, $old, $new, $delta, $experience, $rule)
                """,
                ("$id", c.Id.ToString()),
                ("$at", Text(c.OccurredAt)),
                ("$trait", c.Trait.ToString()),
                ("$old", c.OldValue),
                ("$new", c.NewValue),
                ("$delta", c.Delta),
                ("$experience", c.ExperienceId.ToString()),
                ("$rule", c.Rule));
        }

        transaction.Commit();
    }

    public IReadOnlyList<Decision> GetRecentDecisions(int count) => Recent(
        "decisions",
        "id, occurred_at, trigger, event_id, kind, rule, action, from_state, to_state, reasons",
        count,
        r => new Decision(
            Guid.Parse(r.GetString(0)),
            Time(r.GetString(1)),
            Enum.Parse<DecisionTrigger>(r.GetString(2)),
            r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)),
            Enum.Parse<DecisionKind>(r.GetString(4)),
            r.GetString(5),
            Enum.Parse<BehaviorAction>(r.GetString(6)),
            Enum.Parse<BrainState>(r.GetString(7)),
            Enum.Parse<BrainState>(r.GetString(8)),
            JsonSerializer.Deserialize<List<string>>(r.GetString(9)) ?? []));

    public IReadOnlyList<Experience> GetRecentExperiences(int count) => Recent(
        "experiences",
        "id, occurred_at, event_id, decision_id, kind, state, action, valence, importance, outcome, source",
        count,
        r => new Experience(
            Guid.Parse(r.GetString(0)),
            Time(r.GetString(1)),
            Guid.Parse(r.GetString(2)),
            Guid.Parse(r.GetString(3)),
            Enum.Parse<ExperienceKind>(r.GetString(4)),
            Enum.Parse<BrainState>(r.GetString(5)),
            Enum.Parse<BehaviorAction>(r.GetString(6)),
            r.GetDouble(7),
            r.GetDouble(8),
            r.GetString(9),
            r.GetString(10)));

    public IReadOnlyList<TraitChange> GetRecentTraitChanges(int count) => Recent(
        "trait_changes",
        "id, occurred_at, trait, old_value, new_value, delta, experience_id, rule",
        count,
        r => new TraitChange(
            Guid.Parse(r.GetString(0)),
            Time(r.GetString(1)),
            Enum.Parse<TraitKind>(r.GetString(2)),
            r.GetDouble(3),
            r.GetDouble(4),
            r.GetDouble(5),
            Guid.Parse(r.GetString(6)),
            r.GetString(7)));

    public IReadOnlyList<BrainEvent> GetEvents() => Recent(
        "events",
        "id, type, occurred_at, source",
        int.MaxValue,
        r => new BrainEvent(Guid.Parse(r.GetString(0)), Enum.Parse<EventType>(r.GetString(1)), Time(r.GetString(2)), r.GetString(3)));

    public MemoryStats GetStats()
    {
        using var command = Command(
            """
            SELECT (SELECT COUNT(*) FROM events), (SELECT COUNT(*) FROM decisions),
                   (SELECT COUNT(*) FROM experiences), (SELECT COUNT(*) FROM trait_changes)
            """);
        using var reader = command.ExecuteReader();
        reader.Read();
        return new MemoryStats(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    private void Initialize()
    {
        using (var wal = Command("PRAGMA journal_mode = WAL;"))
        {
            wal.ExecuteNonQuery();
        }

        using var versionCommand = Command("PRAGMA user_version;");
        var version = Convert.ToInt32(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version == Schema.Version)
        {
            return;
        }

        if (version != 0)
        {
            throw new InvalidOperationException(
                $"Database schema version {version} is not supported (expected {Schema.Version}). Refusing to open it.");
        }

        using var transaction = _connection.BeginTransaction();
        Execute(Schema.Create);
        Execute($"PRAGMA user_version = {Schema.Version};");
        transaction.Commit();
    }

    /// <summary>Selects the last <paramref name="count"/> rows by insertion order and returns them oldest first.</summary>
    private List<T> Recent<T>(string table, string columns, int count, Func<SqliteDataReader, T> map)
    {
        using var command = Command($"SELECT * FROM (SELECT {columns}, seq FROM {table} ORDER BY seq DESC LIMIT $count) ORDER BY seq ASC");
        command.Parameters.AddWithValue("$count", count);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    private SqliteCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private static string Text(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static string? OptionalText(DateTimeOffset? value) => value is { } v ? Text(v) : null;

    private static DateTimeOffset Time(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static DateTimeOffset? OptionalTime(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Time(reader.GetString(ordinal));
}
