using Microsoft.Data.Sqlite;
using Osdy.Core;
using Osdy.Core.Decisions;
using Osdy.Core.Events;
using Osdy.Core.Experiences;
using Osdy.Core.Personality;
using Osdy.Core.Runtime;
using Osdy.Core.Simulation;
using Osdy.Core.States;

namespace Osdy.Persistence.Sqlite.Tests;

public sealed class SqliteBrainStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "osdy-tests-" + Guid.NewGuid().ToString("N"));

    private string DbPath => Path.Combine(_directory, "nested", "brain.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Unrecorded_event_is_not_written_and_its_decision_has_no_event_link()
    {
        var options = new BrainOptions
        {
            Recording = new RecordingOptions { EventTypes = [nameof(EventType.OwnerRollbackRequested)] },
        };
        using var store = new SqliteBrainStore(DbPath);
        var brain = BrainBootstrap.Boot(store, options, T0).Brain;

        var step = brain.Handle(BrainEvent.Create(EventType.PositiveInteraction, T0.AddMinutes(5), "test"));
        store.Commit(step);

        var decision = Assert.Single(store.GetRecentDecisions(10));
        Assert.Equal(step.Decision!.Id, decision.Id);
        Assert.Null(decision.EventId);
        Assert.Empty(store.GetEvents());
        Assert.Equal(new MemoryStats(0, 1, 0, 0), store.GetStats());
        Assert.Equal(brain.Personality.Traits.Select(t => t.Value), store.Load()!.Personality.Traits.Select(t => t.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Forget_is_atomic_and_preserves_actions_learning_and_restart_budget(bool failDelete)
    {
        var options = new BrainOptions
        {
            Learning = new LearningOptions
            {
                LearningRate = 1, MaxStep = 0.005, BudgetPerWindow = 0.01,
                BudgetWindow = TimeSpan.FromHours(1),
            },
        };
        Guid forgotten;
        BrainSnapshot before;
        using (var store = new SqliteBrainStore(DbPath))
        {
            var brain = BrainBootstrap.Boot(store, options, T0).Brain;
            store.Commit(brain.Handle(BrainEvent.Create(EventType.PositiveInteraction, T0.AddMinutes(5), "test")));
            store.Commit(brain.Handle(BrainEvent.Create(EventType.PositiveInteraction, T0.AddMinutes(20), "test")));
            before = store.Load()!;
            var memory = store.GetRecentExperiences(10)[0];
            forgotten = memory.Id;
            var events = store.GetEvents();
            var decisions = store.GetRecentDecisions(10);
            var experiences = store.GetRecentExperiences(10);
            var changes = store.GetRecentTraitChanges(10);
            var stats = store.GetStats();
            Assert.False(store.ForgetExperience(Guid.NewGuid()));
            Assert.Equal(stats, store.GetStats());
            if (failDelete)
            {
                using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
                connection.Open();
                using var command = connection.CreateCommand();
                // Fail at the last deletion, after the decision was detached and the experience deleted.
                command.CommandText = "CREATE TRIGGER refuse_forget BEFORE DELETE ON events BEGIN SELECT RAISE(ABORT, 'injected failure'); END;";
                command.ExecuteNonQuery();
                Assert.Throws<SqliteException>(() => store.ForgetExperience(forgotten));
                Assert.Equal(events, store.GetEvents());
                Assert.Equal(experiences, store.GetRecentExperiences(10));
                Assert.Equal(stats, store.GetStats());
            }
            else
            {
                Assert.True(store.ForgetExperience(forgotten));
                Assert.Equal(events.Where(e => e.Id != memory.EventId), store.GetEvents());
                Assert.Equal(experiences.Skip(1), store.GetRecentExperiences(10));
                Assert.Equal(stats with { Events = stats.Events - 1, Experiences = stats.Experiences - 1 }, store.GetStats());
            }
            var afterDecisions = store.GetRecentDecisions(10);
            for (var i = 0; i < decisions.Count; i++)
            {
                var expected = !failDelete && decisions[i].EventId == memory.EventId
                    ? decisions[i] with { EventId = null } : decisions[i];
                Assert.Equal(expected with { Reasons = [] }, afterDecisions[i] with { Reasons = [] });
                Assert.Equal(expected.Reasons, afterDecisions[i].Reasons);
            }
            Assert.Equal(changes, store.GetRecentTraitChanges(10));
        }
        using var reopened = new SqliteBrainStore(DbPath);
        var loaded = reopened.Load()!;
        Assert.Equal(before.Identity, loaded.Identity);
        Assert.Equal(before.Runtime, loaded.Runtime);
        foreach (var trait in before.Personality.Traits)
        {
            var actual = loaded.Personality[trait.Kind];
            Assert.Equal(trait with { RecentChanges = [] }, actual with { RecentChanges = [] });
            Assert.Equal(trait.RecentChanges, actual.RecentChanges);
        }
        var restored = BrainBootstrap.Boot(reopened, options, T0.AddMinutes(25)).Brain;
        var capped = restored.Handle(BrainEvent.Create(EventType.PositiveInteraction, T0.AddMinutes(30), "test"));
        Assert.DoesNotContain(capped.TraitChanges, c => c.Trait == TraitKind.Sociability);
        var released = restored.Handle(BrainEvent.Create(EventType.PositiveInteraction, T0.AddMinutes(65), "test"));
        Assert.Equal(0.005, Assert.Single(released.TraitChanges, c => c.Trait == TraitKind.Sociability).Delta, 10);
    }

    [Fact]
    public void A_v1_database_migrates_through_v2_to_the_latest_schema_without_losing_any_rows_or_values()
    {
        CreateV1();
        var before = StoredValues();

        using var store = new SqliteBrainStore(DbPath);
        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        Assert.Equal(3L, command.ExecuteScalar());
        command.CommandText = "SELECT name FROM pragma_table_info('traits')";
        using (var reader = command.ExecuteReader())
        {
            var columns = new List<string>();
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }
            Assert.Equal(["kind", "value", "baseline"], columns);
        }

        Assert.Equal(before, StoredValues());
        var loaded = store.Load()!;
        Assert.Equal("Legacy", loaded.Identity.Name);
        Assert.Equal(0.7, loaded.Runtime.Energy);
        Assert.Equal(T0.AddHours(1), loaded.Runtime.LastUpdatedAt);
        Assert.Equal(0.495, loaded.Personality[TraitKind.Sociability].Value);
        Assert.Equal(new TraitBudgetEntry(T0.AddMinutes(30), 0.005),
            Assert.Single(loaded.Personality[TraitKind.Sociability].RecentChanges));
        var shortWindow = Brain.Restore(loaded, new BrainOptions
        {
            Learning = new LearningOptions { BudgetWindow = TimeSpan.FromMinutes(30) },
        });
        Assert.Empty(shortWindow.Personality[TraitKind.Sociability].RecentChanges);
        Assert.Equal(0.495, shortWindow.Personality[TraitKind.Sociability].Value);
        Assert.Equal(new MemoryStats(1, 1, 1, 1), store.GetStats());
        Assert.Equal("fixture", Assert.Single(store.GetEvents()).Source);
        Assert.Equal("legacy reason", Assert.Single(Assert.Single(store.GetRecentDecisions(10)).Reasons));
        Assert.Equal("legacy outcome", Assert.Single(store.GetRecentExperiences(10)).Outcome);
        var legacy = Assert.Single(store.GetRecentTraitChanges(10));
        Assert.Equal(-0.005, legacy.Delta);
        Assert.Equal((TraitChangeKind.Learning, (Guid?)null), (legacy.Kind, legacy.RollbackTargetId));
        Assert.Null(Assert.Single(store.GetEvents()).TargetId);
    }

    [Fact]
    public void A_v2_database_migrates_to_v3_keeping_every_row_and_marking_old_changes_as_learning()
    {
        CreateV1();
        Sql("ALTER TABLE traits DROP COLUMN budget_window_start; ALTER TABLE traits DROP COLUMN budget_used; PRAGMA user_version = 2;");
        var before = StoredValues();

        using var store = new SqliteBrainStore(DbPath);

        Assert.Equal(before, StoredValues());
        Assert.Equal(3L, Scalar("PRAGMA user_version"));
        var legacy = Assert.Single(store.GetRecentTraitChanges(10));
        Assert.Equal((TraitChangeKind.Learning, (Guid?)null), (legacy.Kind, legacy.RollbackTargetId));
        Assert.Null(Assert.Single(store.GetEvents()).TargetId);
        Assert.Equal(new TraitBudgetEntry(T0.AddMinutes(30), 0.005),
            Assert.Single(store.Load()!.Personality[TraitKind.Sociability].RecentChanges));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Read_only_legacy_schemas_are_refused_with_migration_guidance_and_unchanged_bytes(int version)
    {
        CreateV1();
        if (version == 2)
        {
            Sql("ALTER TABLE traits DROP COLUMN budget_window_start; ALTER TABLE traits DROP COLUMN budget_used; PRAGMA user_version = 2;");
        }

        var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(DbPath));

        var error = Assert.Throws<InvalidOperationException>(() => SqliteBrainStore.OpenReadOnly(DbPath));
        SqliteConnection.ClearAllPools();

        Assert.Contains($"schema version {version} requires migration to version 3", error.Message, StringComparison.Ordinal);
        Assert.Contains("read-write", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(DbPath)));
    }

    [Fact]
    public void Rollback_audit_survives_restart_and_compensation_does_not_change_the_learning_budget()
    {
        var options = new BrainOptions
        {
            Learning = new LearningOptions { LearningRate = 1, MaxStep = 0.005, BudgetPerWindow = 0.01, BudgetWindow = TimeSpan.FromHours(1) },
        };
        BrainSnapshot live;
        StepResult rollback;
        using (var store = new SqliteBrainStore(DbPath))
        {
            var brain = BrainBootstrap.Boot(store, options, T0).Brain;
            store.Commit(brain.Handle(BrainEvent.Create(EventType.PositiveInteraction, T0.AddMinutes(5), "test")));
            var target = store.GetRecentTraitChanges(10)[0];
            var request = BrainEvent.Create(EventType.OwnerRollbackRequested, T0.AddMinutes(10), "cli", target.Id);
            rollback = brain.Rollback(request, store.GetRecentTraitChanges(int.MaxValue));
            store.Commit(rollback);
            live = brain.Snapshot;
        }

        using var reopened = new SqliteBrainStore(DbPath);
        var restored = Brain.Restore(reopened.Load()!, options);

        var changes = reopened.GetRecentTraitChanges(int.MaxValue);
        var learning = changes.Where(c => c.Kind == TraitChangeKind.Learning).ToArray();
        var restorations = changes.Where(c => c.Kind == TraitChangeKind.Restoration).ToArray();
        Assert.Equal(rollback.TraitChanges, restorations);
        Assert.NotEmpty(restorations);
        Assert.All(restorations, c => Assert.Equal(learning[0].Id, c.RollbackTargetId));
        Assert.Equal(learning[0].Id, reopened.GetEvents().Single(e => e.Type == EventType.OwnerRollbackRequested).TargetId);
        Assert.Equal(ExperienceKind.OwnerRollback, Assert.Single(reopened.GetRecentExperiences(10), e => e.Kind == ExperienceKind.OwnerRollback).Kind);
        foreach (var trait in live.Personality.Traits)
        {
            // Original learning is still spent; the compensation is not counted, live or after restart.
            Assert.Equal(
                learning.Where(c => c.Trait == trait.Kind).Select(c => new TraitBudgetEntry(c.OccurredAt, Math.Abs(c.Delta))),
                restored.Personality[trait.Kind].RecentChanges);
            Assert.Equal(trait.RecentChanges, restored.Personality[trait.Kind].RecentChanges);
            Assert.Equal(trait.Value, restored.Personality[trait.Kind].Value);
        }
    }

    private void CreateV1()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = SchemaV1Fixture.Sql;
        command.ExecuteNonQuery();
        foreach (var kind in Enum.GetValues<TraitKind>())
        {
            command.CommandText = "INSERT INTO traits VALUES ($kind, $value, 0.5, '2026-01-01T08:00:00.0000000+00:00', 0.02)";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$kind", kind.ToString());
            command.Parameters.AddWithValue("$value", kind == TraitKind.Sociability ? 0.495 : 0.5);
            command.ExecuteNonQuery();
        }
    }

    private List<string> StoredValues()
    {
        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        var rows = new List<string>();
        foreach (var table in new[] { "identity", "traits", "runtime_state", "events", "decisions", "experiences", "trait_changes" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = table switch
            {
                "traits" => "SELECT kind, value, baseline FROM traits ORDER BY kind",
                // Legacy columns only: later schemas add columns that older files cannot have.
                "events" => "SELECT seq, id, type, occurred_at, source FROM events ORDER BY 1",
                "trait_changes" => "SELECT seq, id, occurred_at, trait, old_value, new_value, delta, experience_id, rule FROM trait_changes ORDER BY 1",
                _ => $"SELECT * FROM {table} ORDER BY 1",
            };
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(table + System.Text.Json.JsonSerializer.Serialize(values));
            }
        }
        return rows;
    }

    [Fact]
    public void A_new_database_is_empty_and_creates_its_directory()
    {
        using var store = new SqliteBrainStore(DbPath);

        Assert.Null(store.Load());
        Assert.Equal(new MemoryStats(0, 0, 0, 0), store.GetStats());
        Assert.True(File.Exists(DbPath));
    }

    [Fact]
    public void Sliding_budget_survives_restart_and_releases_each_change_at_its_own_expiry()
    {
        var options = new BrainOptions
        {
            Learning = new LearningOptions
            {
                LearningRate = 1,
                MaxStep = 0.005,
                BudgetPerWindow = 0.01,
                BudgetWindow = TimeSpan.FromHours(1),
            },
        };
        double savedValue;
        using (var store = new SqliteBrainStore(DbPath))
        {
            var brain = BrainBootstrap.Boot(store, options, T0).Brain;
            foreach (var minute in new[] { 5, 20 })
            {
                var result = brain.Handle(BrainEvent.Create(EventType.PositiveInteraction, T0.AddMinutes(minute), "test"));
                Assert.Equal(0.005, Assert.Single(result.TraitChanges, c => c.Trait == TraitKind.Sociability).Delta, 10);
                store.Commit(result);
            }
            savedValue = brain.Personality[TraitKind.Sociability].Value;
        }

        using var reopened = new SqliteBrainStore(DbPath);
        var restored = BrainBootstrap.Boot(reopened, options, T0.AddMinutes(25)).Brain;
        Assert.Equal(2, restored.Personality[TraitKind.Sociability].RecentChanges.Count);
        var capped = restored.Handle(BrainEvent.Create(EventType.PositiveInteraction, T0.AddMinutes(30), "test"));
        reopened.Commit(capped);
        Assert.NotNull(capped.Experience);
        Assert.DoesNotContain(capped.TraitChanges, c => c.Trait == TraitKind.Sociability);
        Assert.Equal(savedValue, restored.Personality[TraitKind.Sociability].Value);

        var released = restored.Handle(BrainEvent.Create(EventType.PositiveInteraction, T0.AddMinutes(65), "test"));
        reopened.Commit(released);
        Assert.Equal(0.005, Assert.Single(released.TraitChanges, c => c.Trait == TraitKind.Sociability).Delta, 10);
        Assert.Equal(savedValue + 0.005, restored.Personality[TraitKind.Sociability].Value, 10);
        Assert.Equal(new[] { T0.AddMinutes(20), T0.AddMinutes(65) },
            restored.Personality[TraitKind.Sociability].RecentChanges.Select(c => c.OccurredAt));
    }

    [Fact]
    public void Identity_personality_and_runtime_survive_a_restart()
    {
        BrainSnapshot saved;
        using (var store = new SqliteBrainStore(DbPath))
        {
            var brain = BrainBootstrap.Boot(store, new BrainOptions(), T0).Brain;
            store.Commit(brain.Handle(BrainEvent.Create(EventType.HumanInteractionDetected, T0.AddSeconds(5), "test")));
            store.Commit(brain.Handle(BrainEvent.Create(EventType.PositiveInteraction, T0.AddSeconds(10), "test")));
            saved = brain.Snapshot;
        }

        using var reopened = new SqliteBrainStore(DbPath);
        var loaded = reopened.Load()!;

        Assert.Equal(saved.Identity, loaded.Identity);
        foreach (var trait in saved.Personality.Traits)
        {
            var restored = loaded.Personality[trait.Kind];
            Assert.Equal(trait with { RecentChanges = [] }, restored with { RecentChanges = [] });
            Assert.Equal(trait.RecentChanges, restored.RecentChanges);
        }
        Assert.Equal(saved.Runtime, loaded.Runtime);
    }

    [Fact]
    public void Experiences_decisions_events_and_trait_changes_survive_a_restart_in_order()
    {
        var committed = new List<StepResult>();
        using (var store = new SqliteBrainStore(DbPath))
        {
            var brain = BrainBootstrap.Boot(store, new BrainOptions(), T0).Brain;
            var scenario = ScenarioParser.Parse("""
                00:00:05 HumanInteractionDetected
                00:00:10 PositiveInteraction
                00:00:20 NegativeInteraction
                00:00:30 InteractionEnded
                00:00:40 NoveltyDetected
                00:05:00 wait
                """);
            new ScenarioSimulator(brain, T0, step =>
            {
                if (step.HasRecords)
                {
                    store.Commit(step);
                    committed.Add(step);
                }
            }).Run(scenario);
        }

        using var reopened = new SqliteBrainStore(DbPath);

        Assert.Equal(committed.Select(s => s.Event).OfType<BrainEvent>(), reopened.GetEvents());
        Assert.Equal(committed.Select(s => s.Experience).OfType<Osdy.Core.Experiences.Experience>(), reopened.GetRecentExperiences(100));
        Assert.Equal(committed.SelectMany(s => s.TraitChanges), reopened.GetRecentTraitChanges(100));

        var expectedDecisions = committed.Select(s => s.Decision).OfType<Decision>().ToList();
        var loadedDecisions = reopened.GetRecentDecisions(100);
        Assert.Equal(expectedDecisions.Count, loadedDecisions.Count);
        for (var i = 0; i < expectedDecisions.Count; i++)
        {
            Assert.Equal(expectedDecisions[i] with { Reasons = [] }, loadedDecisions[i] with { Reasons = [] });
            Assert.Equal(expectedDecisions[i].Reasons, loadedDecisions[i].Reasons);
        }

        Assert.Equal(3, reopened.GetRecentExperiences(3).Count);
        Assert.Equal(expectedDecisions[^1].Id, reopened.GetRecentDecisions(1)[0].Id);
        Assert.Equal(new MemoryStats(5, expectedDecisions.Count, 3, committed.Sum(s => s.TraitChanges.Count)), reopened.GetStats());
    }

    [Fact]
    public void Booting_after_a_crash_mid_interaction_continues_from_a_valid_state()
    {
        using (var store = new SqliteBrainStore(DbPath))
        {
            var brain = BrainBootstrap.Boot(store, new BrainOptions(), T0).Brain;
            store.Commit(brain.Handle(BrainEvent.Create(EventType.HumanInteractionDetected, T0.AddSeconds(5), "test")));
        }

        using (var store = new SqliteBrainStore(DbPath))
        {
            var boot = BrainBootstrap.Boot(store, new BrainOptions(), T0.AddHours(1));
            Assert.False(boot.Created);
            Assert.Equal(BehaviorAction.RecoverAfterRestart, boot.Startup.Decision!.Action);
        }

        using var reopened = new SqliteBrainStore(DbPath);
        Assert.Equal(BrainState.Idle, reopened.Load()!.Runtime.State);
        Assert.Equal(BehaviorAction.RecoverAfterRestart, reopened.GetRecentDecisions(1)[0].Action);
    }

    [Fact]
    public void Opening_an_empty_file_read_only_finds_no_brain_and_leaves_the_file_untouched()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        File.WriteAllBytes(DbPath, []);

        var store = SqliteBrainStore.OpenReadOnly(DbPath);
        SqliteConnection.ClearAllPools();

        Assert.Null(store);
        Assert.Equal(0, new FileInfo(DbPath).Length);
        Assert.Equal(["brain.db"], Directory.GetFiles(Path.GetDirectoryName(DbPath)!).Select(Path.GetFileName));
    }

    [Fact]
    public void A_read_only_store_reads_the_saved_brain_and_refuses_to_write()
    {
        StepResult startup;
        using (var store = new SqliteBrainStore(DbPath))
        {
            startup = BrainBootstrap.Boot(store, new BrainOptions(), T0).Startup;
        }

        using var readOnly = SqliteBrainStore.OpenReadOnly(DbPath)!;

        Assert.Equal(startup.Snapshot.Identity, readOnly.Load()!.Identity);
        Assert.Equal(new MemoryStats(0, 0, 0, 0), readOnly.GetStats());
        Assert.Throws<SqliteException>(() => readOnly.Commit(startup));
        Assert.False(readOnly.ForgetExperience(Guid.NewGuid()));
    }

    [Fact]
    public void A_newer_schema_version_is_refused_instead_of_being_overwritten()
    {
        // A plain (non-WAL) SQLite file written by some newer version of the app.
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        using (var connection = new SqliteConnection($"Data Source={DbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        var before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(DbPath)));
        var error = Assert.Throws<InvalidOperationException>(() => new SqliteBrainStore(DbPath));
        var readOnlyError = Assert.Throws<InvalidOperationException>(() => SqliteBrainStore.OpenReadOnly(DbPath));
        SqliteConnection.ClearAllPools();
        var after = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(DbPath)));

        Assert.Contains("schema version 99", readOnlyError.Message, StringComparison.Ordinal);
        Assert.Contains("schema version 99", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, after);
    }

    private static void Sql(string database, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private void Sql(string sql) => Sql(DbPath, sql);

    private object? Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
