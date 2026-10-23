# Backlog

Priority: **P1** = v0.1 (Osdy Core), **P2** = v0.2, **P3** = later. Stories are written from the point of view of the owner/developer ("as the owner").

## v0.1 — Osdy Core

| ID | Story | Acceptance criteria |
|----|-------|---------------------|
| US-01 (P1) | As the owner, I want the robot to have a persistent identity so it is the same creature after every restart. | First run creates `Id`, `Name`, `CreatedAt`; later runs load the same values; console prints `[IDENTITY] Identidad cargada` (or `creada`). |
| US-02 (P1) | As the owner, I want three initial traits (curiosity, sociability, confidence) with configurable starting values. | Defaults 0.70/0.35/0.40; values outside bounds are rejected by options validation; console prints `[TRAIT] Curiosity: 0.70`. |
| US-03 (P1) | As a developer, I want five states with an explicit transition table so invalid behavior is impossible. | Only table transitions succeed; an invalid one throws; every state is reachable in tests. |
| US-04 (P1) | As the owner, I want the brain to run on its own loop so it behaves without being prompted. | Runner ticks every `TickInterval`; with no input it eventually explores, rests and wakes; tests use a fake clock. |
| US-05 (P1) | As a developer, I want to feed simulated events and replay them so I can debug behavior. | Console keys and scenario files produce events; events are persisted; `events export` + `simulate` reproduce the decision trace. |
| US-06 (P1) | As the owner, I want rule-based decisions that depend on personality and energy. | Each of the 9 rules has a test; a low-sociability robot observes cautiously, a sociable one engages. |
| US-07 (P1) | As the owner, I want experiences saved in SQLite so they are not lost. | Positive/negative interactions and explored novelty create experiences linked to event and decision; they survive restart. |
| US-08 (P1) | As the owner, I want personality to change slowly from experiences. | Model in [personality-model.md](personality-model.md); bounded, gradual and rate-limited properties tested with ≥ 1 000 experiences. |
| US-09 (P1) | As a developer, I want every significant decision explained. | Each decision stores rule, action, from/to state and human-readable reasons; `osdy status` shows them. |
| US-10 (P1) | As the owner, I want the robot to resume correctly after a restart or crash. | Identity, traits, experiences, state and energy are restored; transient states recover to Idle with a recorded `RecoverAfterRestart`. |
| US-11 (P1) | As the owner, I want a console app to run, simulate and inspect the brain. | `osdy run`, `osdy simulate <file>`, `osdy status`, `osdy events export <file>`; output follows the example format. |
| US-12 (P1) | As a developer, I want automated tests that are fast and deterministic. | `dotnet test` green; no `Thread.Sleep`, no `Task.Delay` with real time, no `Random`. |

## v0.2 — Memory & control (next)

| ID | Story | Acceptance criteria |
|----|-------|---------------------|
| US-13 (P2) | As the owner, I want to list, inspect and delete memories. | `forget` removes event + experience everywhere; export no longer contains it. |
| US-14 (P2) | As the owner, I want to undo unwanted learning. | Rollback to a change ID restores exact previous trait values and logs the rollback. |
| US-15 (P2) | As the owner, I want to choose which events are stored. | Event types outside the recording policy are processed but not persisted. |
| US-16 (P2) | As the owner, I want the full trait set. | Shyness, behavioral energy, playfulness, initiative added with migrations from schema v1. |
| US-17 (P2) | As the owner, I want to export everything the robot knows about me. | JSON export of all tables, documented format. |

## Later (P3)

Emotional State Engine, preferences and habits, LLM provider port, semantic memory, perception/expression adapters, embedded deployment — see [roadmap.md](roadmap.md).
