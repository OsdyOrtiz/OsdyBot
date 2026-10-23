# Memory and persistence

## Memory types

| Memory | Meaning | v0.1 storage | Later |
|--------|---------|--------------|-------|
| Episodic | What happened and how it was read | `events` (facts) + `experiences` (interpretations) | Consolidation and LLM summaries (v0.4), marked as inferred |
| Behavioral | What the brain did, why, and what it learned | `decisions` + `trait_changes` | Action-outcome scores for preferences and habits (v0.3) |
| Semantic | Persistent knowledge ("the owner likes being greeted") | — | `facts` table + optional embeddings (v0.4, pending decision) |
| Working | Context for the current decision | In-process `RuntimeState` (state, energy, last interaction) | Bounded recent-events buffer (v0.2) |

Every experience stores context (state, action), importance, date, source and links to its event and decision. Trait changes link to their experience, which makes any adaptation explainable by a join while the experience exists; after forgetting, that ID remains as a tombstone (ADR-017).

## SQLite schema (version 2)

```sql
identity      (id TEXT PRIMARY KEY, name TEXT, created_at TEXT)
traits        (kind TEXT PRIMARY KEY, value REAL, baseline REAL)
runtime_state (id INTEGER PRIMARY KEY CHECK (id = 1), state TEXT, state_entered_at TEXT,
               energy REAL, last_updated_at TEXT, last_spontaneous_at TEXT, last_interaction_at TEXT)
events        (seq INTEGER PRIMARY KEY, id TEXT UNIQUE, type TEXT, occurred_at TEXT, source TEXT)
decisions     (seq INTEGER PRIMARY KEY, id TEXT UNIQUE, occurred_at TEXT, trigger TEXT, event_id TEXT,
               kind TEXT, rule TEXT, action TEXT, from_state TEXT, to_state TEXT, reasons TEXT)
experiences   (seq INTEGER PRIMARY KEY, id TEXT UNIQUE, occurred_at TEXT, event_id TEXT, decision_id TEXT,
               kind TEXT, state TEXT, action TEXT, valence REAL, importance REAL, outcome TEXT, source TEXT)
trait_changes (seq INTEGER PRIMARY KEY, id TEXT UNIQUE, occurred_at TEXT, trait TEXT, old_value REAL,
               new_value REAL, delta REAL, experience_id TEXT, rule TEXT)
```

- Enums are stored as names, timestamps as ISO 8601 round-trip strings, `reasons` as a JSON array.
- `seq` gives a stable insertion order independent of clock precision.
- Schema version lives in `PRAGMA user_version`. Versions are checked before any write, including journal-mode changes; unknown newer versions are refused untouched.
- Opening v1 read-write explicitly migrates to v2: both obsolete `traits.budget_window_start` and `traits.budget_used` columns are dropped and `user_version` is advanced in one transaction. All rows and remaining values are preserved.
- `trait_changes` is the single source of truth for budget history. `Load()` rebuilds per-trait entries from `occurred_at` and `ABS(delta)` up to the stored runtime `last_updated_at`. Storage has no learning options, so it loads the recorded history; `Brain.Restore` retains only entries in `(last_updated_at - BudgetWindow, last_updated_at]` using the caller's options. Learning prunes again at each experience's time.
- WAL journal mode for crash safety.
- Inspection commands (`osdy status`, `osdy memories list/show`, `osdy events export`, `osdy export`) open the file read-only: they never create a schema, change the journal mode or write anything, even to an empty file. A v1 file is refused with guidance to open it read-write first for migration; no legacy read path is maintained.

## Write strategy

- One transaction per `StepResult`: snapshot upserts + appended events, decisions, experiences and trait changes. Either all of a step is saved or none of it.
- A step is committed when it produced any record (event, decision, experience, trait change). Steps that only drained energy are committed as a checkpoint at most every `CheckpointInterval` (default 30 s). On restart, at most that much energy drift is lost, which is acceptable.
- Rationale: reduce write amplification on SD/eMMC storage later (risk R4).

## JSON export format (version 1)

`osdy export <file.json> [--db <file>]` exports the saved snapshot and every history row, without starting the brain or modifying the database. The destination must not already exist (preventing accidental overwrites, including the database itself). Missing databases or databases without a saved brain fail with exit code 1 and no output file. Success prints `[EXPORT] <n> registros exportados a <path>`, where `n` counts events, decisions, experiences and trait changes, and exits with code 0.

The output is one UTF-8 JSON document. Property names are English camelCase; enums are names, numbers are culture-invariant JSON numbers, IDs are UUID strings and timestamps use ISO 8601 round-trip format (`O`, including an offset and seven fractional digits). Nullable timestamps and links remain JSON `null`. History arrays are oldest first by stable insertion order, including rows sharing a timestamp. SQLite-only sequence keys and the singleton runtime row ID are not exported.

| Field | Contents |
|-------|----------|
| `schemaVersion` | Export format version, currently `1`; independent of SQLite schema version |
| `exportedAt` | UTC export time supplied by the host's `TimeProvider`, not the simulation time |
| `identity` | `id`, `name`, `createdAt` |
| `traits` | Array of `kind`, `value`, `baseline` |
| `runtime` | `state`, `energy`, `stateEnteredAt`, `lastUpdatedAt`, nullable `lastSpontaneousAt` and `lastInteractionAt` |
| `events` | Array of `id`, `type`, `occurredAt`, `source` |
| `decisions` | Array of `id`, `occurredAt`, `trigger`, nullable `eventId`, `kind`, `rule`, `action`, `fromState`, `toState`, `reasons` (string array) |
| `experiences` | Array of `id`, `occurredAt`, `eventId`, `decisionId`, `kind`, `state`, `action`, `valence`, `importance`, `outcome`, `source` |
| `traitChanges` | Array of `id`, `occurredAt`, `trait`, `oldValue`, `newValue`, `delta`, `experienceId`, `rule` |

Shape example with empty history arrays:

```json
{
  "schemaVersion": 1,
  "exportedAt": "2026-01-01T08:00:00.0000000+00:00",
  "identity": {
    "id": "00000000-0000-0000-0000-000000000001",
    "name": "Osdy",
    "createdAt": "2026-01-01T08:00:00.0000000+00:00"
  },
  "traits": [
    { "kind": "Curiosity", "value": 0.7, "baseline": 0.7 },
    { "kind": "Sociability", "value": 0.35, "baseline": 0.35 },
    { "kind": "Confidence", "value": 0.4, "baseline": 0.4 }
  ],
  "runtime": {
    "state": "Idle",
    "energy": 1.0,
    "stateEnteredAt": "2026-01-01T08:00:00.0000000+00:00",
    "lastUpdatedAt": "2026-01-01T08:00:00.0000000+00:00",
    "lastSpontaneousAt": null,
    "lastInteractionAt": null
  },
  "events": [],
  "decisions": [],
  "experiences": [],
  "traitChanges": []
}
```

## Privacy and control (v0.2; inspection, export and deletion implemented)

| Need | Mechanism |
|------|-----------|
| Control what is stored | Per-event-type recording policy in options (allowlist) |
| Inspect | `osdy memories list [--db <file>] [--count <n>]` lists the latest experiences (default 10), oldest first, with ID, time, kind, valence and outcome. `osdy memories show <id> [--db <file>]` includes the event, decision reasons and trait changes. Both open read-only. |
| Correct | Mark an experience as invalid; its trait changes are reverted by replaying the change log |
| Export | `osdy export <file.json> [--db <file>]` → JSON snapshot and all history rows (format above) |
| Delete | `osdy forget <id> [--db <file>]` deletes the experience and its event in one transaction. Decisions remain with `event_id = NULL`; trait changes retain `experience_id` as a tombstone. Trait values and restart budget history do not change (ADR-017). |
| Protect | File permissions `0600`; encryption at rest is a pending decision before any camera/voice data (v0.5) |

Unknown experience IDs fail with exit code 1 and `[ERROR] No existe la experiencia <id>` without changing stored data. Successful deletion prints `[MEMORY] Experiencia <id> y evento <event-id> eliminados; aprendizaje conservado.` and exits with code 0. `status` marks missing experience links as `experiencia <id> (olvidada)`; inspection uses the same trait-change rendering. Forgotten experiences cannot be shown. Export omits the deleted fact and interpretation, retaining decisions and tombstone change records. Forgetting is not learning rollback (US-14).

Deletion is logical: it does not promise forensic erasure from SQLite free pages, WAL files, backups or previously written exports. v0.1 stores only simulated, non-personal events; recording policy, correction and further privacy protections remain deferred.
