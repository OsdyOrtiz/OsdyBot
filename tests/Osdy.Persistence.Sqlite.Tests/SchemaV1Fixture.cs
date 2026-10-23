namespace Osdy.Persistence.Sqlite.Tests;

/// <summary>Frozen schema from v1, independent of the production schema.</summary>
internal static class SchemaV1Fixture
{
    public const string Sql = """
        CREATE TABLE identity (id TEXT PRIMARY KEY, name TEXT NOT NULL, created_at TEXT NOT NULL);
        CREATE TABLE traits (kind TEXT PRIMARY KEY, value REAL NOT NULL, baseline REAL NOT NULL,
            budget_window_start TEXT NOT NULL, budget_used REAL NOT NULL);
        CREATE TABLE runtime_state (id INTEGER PRIMARY KEY CHECK (id = 1), state TEXT NOT NULL,
            state_entered_at TEXT NOT NULL, energy REAL NOT NULL, last_updated_at TEXT NOT NULL,
            last_spontaneous_at TEXT, last_interaction_at TEXT);
        CREATE TABLE events (seq INTEGER PRIMARY KEY, id TEXT NOT NULL UNIQUE, type TEXT NOT NULL,
            occurred_at TEXT NOT NULL, source TEXT NOT NULL);
        CREATE TABLE decisions (seq INTEGER PRIMARY KEY, id TEXT NOT NULL UNIQUE, occurred_at TEXT NOT NULL,
            trigger TEXT NOT NULL, event_id TEXT, kind TEXT NOT NULL, rule TEXT NOT NULL, action TEXT NOT NULL,
            from_state TEXT NOT NULL, to_state TEXT NOT NULL, reasons TEXT NOT NULL);
        CREATE TABLE experiences (seq INTEGER PRIMARY KEY, id TEXT NOT NULL UNIQUE, occurred_at TEXT NOT NULL,
            event_id TEXT NOT NULL, decision_id TEXT NOT NULL, kind TEXT NOT NULL, state TEXT NOT NULL,
            action TEXT NOT NULL, valence REAL NOT NULL, importance REAL NOT NULL, outcome TEXT NOT NULL, source TEXT NOT NULL);
        CREATE TABLE trait_changes (seq INTEGER PRIMARY KEY, id TEXT NOT NULL UNIQUE, occurred_at TEXT NOT NULL,
            trait TEXT NOT NULL, old_value REAL NOT NULL, new_value REAL NOT NULL, delta REAL NOT NULL,
            experience_id TEXT NOT NULL, rule TEXT NOT NULL);
        CREATE INDEX ix_trait_changes_experience ON trait_changes (experience_id);
        CREATE INDEX ix_experiences_event ON experiences (event_id);
        PRAGMA user_version = 1;

        INSERT INTO identity VALUES ('00000000-0000-0000-0000-000000000001', 'Legacy', '2026-01-01T08:00:00.0000000+00:00');
        INSERT INTO runtime_state VALUES (1, 'Idle', '2026-01-01T08:00:00.0000000+00:00', 0.7,
            '2026-01-01T09:00:00.0000000+00:00', NULL, '2026-01-01T08:30:00.0000000+00:00');
        INSERT INTO events VALUES (7, '00000000-0000-0000-0000-000000000002', 'PositiveInteraction',
            '2026-01-01T08:30:00.0000000+00:00', 'fixture');
        INSERT INTO decisions VALUES (8, '00000000-0000-0000-0000-000000000003', '2026-01-01T08:30:00.0000000+00:00',
            'Event', '00000000-0000-0000-0000-000000000002', 'System', 'legacy-rule', 'RecoverAfterRestart', 'Idle', 'Idle', '["legacy reason"]');
        INSERT INTO experiences VALUES (9, '00000000-0000-0000-0000-000000000004', '2026-01-01T08:30:00.0000000+00:00',
            '00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000003',
            'PositiveInteraction', 'Idle', 'RecoverAfterRestart', 0.8, 0.6, 'legacy outcome', 'fixture');
        INSERT INTO trait_changes VALUES (10, '00000000-0000-0000-0000-000000000005', '2026-01-01T08:30:00.0000000+00:00',
            'Sociability', 0.5, 0.495, -0.005, '00000000-0000-0000-0000-000000000004', 'legacy-learning');
        """;
}
