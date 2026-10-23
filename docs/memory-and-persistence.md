# Memory and persistence

## Memory types

| Memory | Meaning | v0.1 storage | Later |
|--------|---------|--------------|-------|
| Episodic | What happened and how it was read | `events` (facts) + `experiences` (interpretations) | Consolidation and LLM summaries (v0.4), marked as inferred |
| Behavioral | What the brain did, why, and what it learned | `decisions` + `trait_changes` | Action-outcome scores for preferences and habits (v0.3) |
| Semantic | Persistent knowledge ("the owner likes being greeted") | — | `facts` table + optional embeddings (v0.4, pending decision) |
| Working | Context for the current decision | In-process `RuntimeState` (state, energy, last interaction) | Bounded recent-events buffer (v0.2) |

Every experience stores context (state, action), importance, date, source and links to its event and decision. Trait changes link to their experience, which makes any adaptation explainable by a join.

## SQLite schema (version 1)

```sql
identity      (id TEXT PRIMARY KEY, name TEXT, created_at TEXT)
traits        (kind TEXT PRIMARY KEY, value REAL, baseline REAL,
               budget_window_start TEXT, budget_used REAL)
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
- Schema version lives in `PRAGMA user_version`. An unknown newer version refuses to open instead of corrupting data.
- WAL journal mode for crash safety.
- Inspection commands (`osdy status`, `osdy events export`) open the file read-only: they never create a schema, change the journal mode or write anything, even to an empty file.

## Write strategy

- One transaction per `StepResult`: snapshot upserts + appended events, decisions, experiences and trait changes. Either all of a step is saved or none of it.
- A step is committed when it produced any record (event, decision, experience, trait change). Steps that only drained energy are committed as a checkpoint at most every `CheckpointInterval` (default 30 s). On restart, at most that much energy drift is lost, which is acceptable.
- Rationale: reduce write amplification on SD/eMMC storage later (risk R4).

## Privacy and control (design; implementation v0.2)

| Need | Mechanism |
|------|-----------|
| Control what is stored | Per-event-type recording policy in options (allowlist) |
| Inspect | `osdy memories list/show` |
| Correct | Mark an experience as invalid; its trait changes are reverted by replaying the change log |
| Export | `osdy export` → JSON (identity, traits, experiences, decisions, changes) |
| Delete | `osdy forget <id>` deletes the experience and its event; trait changes keep a tombstone reference |
| Protect | File permissions `0600`; encryption at rest is a pending decision before any camera/voice data (v0.5) |

v0.1 stores only simulated, non-personal events, which is why these are deferred.
