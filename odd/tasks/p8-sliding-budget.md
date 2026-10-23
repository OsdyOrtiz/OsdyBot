# P8 — Sliding trait budget window — feature document

Objective: replace the fixed trait budget window with a sliding one and drop the obsolete columns with schema v2. Branch: `feat/osdy-core-v0-1` (pushed per task; merged to `main` at the end by the user).
Test runner: `dotnet test` (xUnit). Rules: `CLAUDE.md` (invariants 2 and 7, Persistence section).

## Specs

- S1 — P8 (docs/decisions.md, verbatim): "Sliding (instead of fixed) trait budget window, removing the 2× boundary burst". Guarantee to prove: for every trait, the total `|Δ|` applied within **any** interval of length `BudgetWindow` is ≤ `BudgetPerWindow`. The worst-case boundary-burst test that today proves the `2 × BudgetPerWindow` bound must now prove ≤ `1 × BudgetPerWindow`.
- S2 — The per-trait history needed by the window lives in the brain (`TraitState`) as the recent changes (time, `|Δ|`) inside the last `BudgetWindow`; entries older than the window are dropped. Trait values still change only through `TraitEvolution.Apply`, each producing a `TraitChange` (invariant 2). `BudgetPerWindow`/`BudgetWindow` stay in `LearningOptions` with their defaults and `Validate()`.
- S3 — User decision (verbatim): "Schema v2 que las borra" — "Migración explícita v1→v2 + test que abre una base v1; schema limpio con una sola fuente de verdad". `traits.budget_window_start` and `traits.budget_used` are dropped; on `Load()` the window history is rebuilt from `trait_changes` (`occurred_at`, `delta`). `Schema.Version` becomes 2; a v1 file is migrated in one transaction, keeping every existing row; a version-2 check still refuses unknown newer versions untouched; `OpenReadOnly` never migrates (a v1 file opened read-only must still be readable or give a clear error, never be written).
- S4 — Docs: `docs/personality-model.md` property 3 and property 6 wording, `docs/memory-and-persistence.md` schema section, `docs/decisions.md` (P8 moves from pending to an ADR row), `docs/roadmap.md` if it lists P8.

## Tasks

| ID | Specs | Route | Status | Commit |
|----|-------|-------|--------|--------|
| T1 | S1, S2, S4 (personality model) | worker (context backstop) | done — core sliding window + tests + personality doc. RED: boundary burst `moved 0.05500000000000005, budget 0.03`. GREEN: 13 focused; full suite 96 Core + 7 SQLite + 18 CLI; build 0 warnings/errors. | awaiting parent commit |
| T2 | S3, S4 | worker (context backstop) | pending — schema v2, migration v1→v2, history rebuilt on load + tests + persistence doc. Risk: item 1 (migration of stored data) → own commit, independent verify | |

## Log

- L1 (2026-10-23, user, verbatim): "ok anda haciendo push por tareas cuando terminemos lo mandamos al main, vamos con p8"
- L2 (2026-10-23, user decision, verbatim): "Schema v2 que las borra" (alternative rejected: "Dejarlas sin uso").
