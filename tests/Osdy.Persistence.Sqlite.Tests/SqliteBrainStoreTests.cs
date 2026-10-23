using Microsoft.Data.Sqlite;
using Osdy.Core;
using Osdy.Core.Decisions;
using Osdy.Core.Events;
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
    public void A_new_database_is_empty_and_creates_its_directory()
    {
        using var store = new SqliteBrainStore(DbPath);

        Assert.Null(store.Load());
        Assert.Equal(new MemoryStats(0, 0, 0, 0), store.GetStats());
        Assert.True(File.Exists(DbPath));
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
        Assert.Equal(saved.Personality.Traits, loaded.Personality.Traits);
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
        SqliteConnection.ClearAllPools();
        var after = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(DbPath)));

        Assert.Contains("schema version 99", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, after);
    }
}
