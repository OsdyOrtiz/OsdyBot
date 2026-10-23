# v0.2 Memory & control — feature document

Objective: deliver the v0.2 backlog stories (docs/backlog.md) in dependency order, one task per story. Branch: `feat/v0-2-memory-control`, stacked on `feat/osdy-core-v0-1` (PR #1); rebase onto `main` once PR #1 is merged. Push after every task; merge to `main` is the user's decision.
Test runner: `dotnet test` (xUnit). Rules: `CLAUDE.md` / `AGENTS.md`.

## Specs

- S1 — US-17 (docs/backlog.md, verbatim): "As the owner, I want to export everything the robot knows about me." / acceptance: "JSON export of all tables, documented format." Design (docs/memory-and-persistence.md, verbatim): "`osdy export` → JSON (identity, traits, experiences, decisions, changes)". All tables means events too. Command shape follows `osdy events export`: `osdy export <archivo.json> [--db <archivo>]`, opens the database read-only (never writes to it), Spanish console message, English JSON property names, culture-invariant values, ISO 8601 round-trip timestamps, a `schemaVersion` field; the format is documented in docs/memory-and-persistence.md and the command in the CLI usage text and README.
- S2 — US-13 (verbatim): "As the owner, I want to list, inspect and delete memories." / "`forget` removes event + experience everywhere; export no longer contains it." Design (verbatim): "`osdy memories list/show`", "`osdy forget <id>` deletes the experience and its event; trait changes keep a tombstone reference". Roadmap DoD (verbatim): "Deleting a memory removes it from every table and export".
- S3 — US-14 (verbatim): "As the owner, I want to undo unwanted learning." / "Rollback to a change ID restores exact previous trait values and logs the rollback." User boundary (L4, verbatim): "Antes del cambio, todos los rasgos — recomendado" / "Deshace ese cambio y todos los posteriores de la personalidad completa. Conserva el historial y registra la corrección."
- S4 — US-15 (verbatim): "As the owner, I want to choose which events are stored." / "Event types outside the recording policy are processed but not persisted."
- S5 — US-16 (verbatim): "As the owner, I want the full trait set." / "Shyness, behavioral energy, playfulness, initiative added with migrations from schema v1." (schema is v2 since P8 → migration from v2.)

## Tasks

| ID | Specs | Route | Status | Commit |
|----|-------|-------|--------|--------|
| T1 | S1 | worker (context backstop) | done — `osdy export` JSON; RED: 3 behavior cases failed (expected exit 0/1, actual 2), 6 usage cases passed. GREEN: 10 focused cases; full suite 129 passed (96 Core, 10 SQLite, 23 CLI); build 0 warnings/errors. | 797d5e5 |
| T2 | S2 | worker + independent verifier (Risk: item 1, stored-data deletion) | done — independent verifier PASS S2/L3; 147/147 tests, 29 adversarial CLI checks, status/export differential unchanged. memories list/show + atomic forget, retaining decisions and learned trait-change tombstones (L3, ADR-017). RED: 4 CLI behavior cases failed before implementation (commands returned usage instead of success/failure); 25 CLI cases passed. GREEN: 22 focused CLI cases, 2 store atomicity/restart-budget cases; full suite 147 passed (96 Core, 12 SQLite, 39 CLI); build 0 warnings/errors. | |
| T3 | S3 | — | pending — learning rollback | |
| T4 | S4 | — | pending — recording policy | |
| T5 | S5 | — | pending — full trait set, schema v3 | |

## Log

- L1 (2026-10-23, user, verbatim): "ok dale vamos con eso" — accepting: update PR #1 to `main`, then start v0.2 with US-17, order US-17 → US-13 → US-14 → US-15 → US-16.
- L2: parent review of T1 — export refusing an existing file kept (protects personal data and the DB itself) but the error is now Spanish (`[ERROR] El archivo <path> ya existe; elegí otro nombre para el export.`); JSON written with relaxed escaping so `+00:00` and accents stay readable. RED observed for both, GREEN 129/129.
- L3 (2026-10-23, user decision for T2, verbatim): "Olvida el hecho, conserva lo aprendido" — "Se borran evento y experiencia; los cambios de rasgos quedan sin referencia (tombstone). Los rasgos no cambian y la ventana de budget sigue correcta. Deshacer aprendizaje es US-14". Rejected: "Olvida todo, revierte el aprendizaje".
- L4 (user decision for T3, verbatim): "Antes del cambio, todos los rasgos — recomendado" / "Deshace ese cambio y todos los posteriores de la personalidad completa. Conserva el historial y registra la corrección." Restore the recorded profile immediately before the target, using persistence order (timestamps may tie); append corrections instead of deleting history. Core invariants still apply: exact restoration is triggered by an experience and passes through TraitEvolution.Apply. T2 verification released shared edit surfaces: PASS S2/L3, full suite 147/147, 29 isolated adversarial CLI invocations; status/export differential against 797d5e5 found no unrequested changes. Crash/power-loss and concurrent writers not exercised; CodeGraph/LSP/MCP unavailable.
