namespace Osdy.Persistence.Sqlite;

/// <summary>Schema version 3. See docs/memory-and-persistence.md.</summary>
internal static class Schema
{
    public const int Version = 3;

    public const string MigrateV1ToV2 = """
        ALTER TABLE traits DROP COLUMN budget_window_start;
        ALTER TABLE traits DROP COLUMN budget_used;
        """;

    /// <summary>Adds rollback metadata; every pre-existing trait change is learning by definition.</summary>
    public const string MigrateV2ToV3 = """
        ALTER TABLE events ADD COLUMN target_id TEXT;
        ALTER TABLE trait_changes ADD COLUMN kind TEXT NOT NULL DEFAULT 'Learning';
        ALTER TABLE trait_changes ADD COLUMN rollback_target_id TEXT;
        """;

    public const string Create = """
        CREATE TABLE identity (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            created_at TEXT NOT NULL
        );

        CREATE TABLE traits (
            kind TEXT PRIMARY KEY,
            value REAL NOT NULL,
            baseline REAL NOT NULL
        );

        CREATE TABLE runtime_state (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            state TEXT NOT NULL,
            state_entered_at TEXT NOT NULL,
            energy REAL NOT NULL,
            last_updated_at TEXT NOT NULL,
            last_spontaneous_at TEXT,
            last_interaction_at TEXT
        );

        CREATE TABLE events (
            seq INTEGER PRIMARY KEY,
            id TEXT NOT NULL UNIQUE,
            type TEXT NOT NULL,
            occurred_at TEXT NOT NULL,
            source TEXT NOT NULL,
            target_id TEXT
        );

        CREATE TABLE decisions (
            seq INTEGER PRIMARY KEY,
            id TEXT NOT NULL UNIQUE,
            occurred_at TEXT NOT NULL,
            trigger TEXT NOT NULL,
            event_id TEXT,
            kind TEXT NOT NULL,
            rule TEXT NOT NULL,
            action TEXT NOT NULL,
            from_state TEXT NOT NULL,
            to_state TEXT NOT NULL,
            reasons TEXT NOT NULL
        );

        CREATE TABLE experiences (
            seq INTEGER PRIMARY KEY,
            id TEXT NOT NULL UNIQUE,
            occurred_at TEXT NOT NULL,
            event_id TEXT NOT NULL,
            decision_id TEXT NOT NULL,
            kind TEXT NOT NULL,
            state TEXT NOT NULL,
            action TEXT NOT NULL,
            valence REAL NOT NULL,
            importance REAL NOT NULL,
            outcome TEXT NOT NULL,
            source TEXT NOT NULL
        );

        CREATE TABLE trait_changes (
            seq INTEGER PRIMARY KEY,
            id TEXT NOT NULL UNIQUE,
            occurred_at TEXT NOT NULL,
            trait TEXT NOT NULL,
            old_value REAL NOT NULL,
            new_value REAL NOT NULL,
            delta REAL NOT NULL,
            experience_id TEXT NOT NULL,
            rule TEXT NOT NULL,
            kind TEXT NOT NULL DEFAULT 'Learning',
            rollback_target_id TEXT
        );

        CREATE INDEX ix_trait_changes_experience ON trait_changes (experience_id);
        CREATE INDEX ix_experiences_event ON experiences (event_id);
        """;
}
