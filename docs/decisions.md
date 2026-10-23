# Decisions, risks and open questions

## Architecture decision records

| ADR | Decision | Why | Rejected alternatives |
|-----|----------|-----|-----------------------|
| 001 | C# on **.NET 10 (LTS)** | Requested stack; LTS support window; first-class linux-arm64 | .NET 8 (shorter remaining support), Python (weaker typing for a long-lived core), Rust (slower iteration for this team) |
| 002 | **Modular monolith** with ports and adapters, three production projects | One process is enough on a single device; ports isolate hardware and storage | Microservices, actor frameworks (Orleans, Akka.NET), agent frameworks (Semantic Kernel) — too much machinery for v0.1 |
| 003 | **Deterministic step function** + single-consumer `Channel` loop | Replayable, lock-free, testable without waiting | Multi-threaded handlers with locks, Rx pipelines, event bus libraries |
| 004 | **Ordered decision rules** with explicit reasons | Simple to explain and test; first match wins | Behavior trees, GOAP, utility AI (candidate for v0.3), reinforcement learning |
| 005 | **SQLite via Microsoft.Data.Sqlite and plain SQL**, schema version in `PRAGMA user_version` | Small dependency, portable file, no ORM startup cost on embedded hardware | EF Core (heavier, migrations machinery not needed yet), LiteDB, JSON files (no transactions) |
| 006 | **Bounded learning**: soft headroom + per-experience cap + window budget + change log | Gradual, bounded, explainable, reversible | Random drift, unbounded moving averages, neural networks |
| 007 | **BCL `TimeProvider`** for time; time passed into the brain explicitly | No custom clock abstraction; fake time in tests | Custom `IClock`, wall-clock reads inside rules |
| 008 | Console output in Spanish following the brief's example; code and docs in English | The example output is part of the requirement; artifacts default to English | Fully English console output |
| 009 | **Events (facts) and experiences (interpretations) in separate tables** | Re-interpretation later without touching facts; privacy controls per layer | Single "memories" table |
| 010 | **No randomness in v0.1** | Determinism is an acceptance criterion; spontaneity comes from time + personality | Seeded randomness now (added later only if behavior feels mechanical) |
| 011 | **No mean reversion in v0.1** | Every trait change must be caused by an experience | Time-based pull toward baseline (re-evaluate in v0.2) |
| 012 | Energy is a **vital**, not an emotion | Needed to make Resting meaningful in v0.1 without building the Emotional State Engine | Implementing simulated emotions in v0.1 |
| 013 | **Hand-written CLI argument parsing** in `CliApp`, testable with fake I/O and time | Four commands do not justify a dependency | System.CommandLine, Spectre.Console.Cli |
| 014 | **JSON configuration** with defaults in code; unknown properties rejected | Typos in tuning files must fail loudly | Microsoft.Extensions.Configuration (silent on typos, more machinery) |

## Risks

| ID | Risk | Impact | Mitigation |
|----|------|--------|------------|
| R1 | Tuning feels flat or too fast | Personality evolution not convincing | All parameters configurable; long simulations measure trajectories before tuning |
| R2 | Rule set grows into spaghetti | Hard to reason about behavior | One class per rule, priority order documented, utility scoring planned for v0.3 |
| R3 | LLM integration bypasses explainability | Unexplainable behavior | LLM only via a port, outputs stored as *inferred*, rules keep final say |
| R4 | Storage wear on SD/eMMC | Data loss on embedded target | Commit only meaningful steps, checkpoint interval, WAL; measure in v0.6 soak test |
| R5 | Clock jumps on devices without RTC | Wrong energy/budget windows | Brain ignores negative elapsed time; RTC availability on target is *to verify* |
| R6 | Personal data in memories (camera/voice later) | Privacy harm | Recording policy, deletion, export in v0.2; encryption decision before v0.5 |
| R7 | Engagement-maximizing behavior emerges | Manipulative pet | No attention reward signal; cooldowns; reviewed in each phase DoD |
| R8 | Scope creep | MVP never ships | Phase scopes in roadmap; anything else goes to the backlog |

## Assumptions

- A1: One robot per process and one SQLite file per robot.
- A2: A single household/owner; no per-person recognition in v0.1.
- A3: Simulated events contain no personal data.
- A4: The robot performs no external actions in v0.1 (only console output), so no permission system is needed yet.
- A5: Default parameter values are educated guesses, not validated psychology.
- A6: Raspberry Pi CM5 (or similar linux-arm64 board) is a plausible target; not yet verified with benchmarks.

## Pending decisions

| ID | Question | Needed by |
|----|----------|-----------|
| P1 | Mean reversion toward baseline: yes/no, and rate | v0.2 |
| P2 | Per-trait learning parameters | v0.2 |
| P3 | Encryption at rest (e.g. SQLCipher) for personal data | before v0.5 |
| P4 | Semantic memory and embeddings approach (SQLite extension vs separate index) | v0.4 |
| P5 | First LLM provider and local-model fallback | v0.4 |
| P6 | Vision/audio stack for perception adapters | v0.5 |
| P7 | Exact target hardware | v0.6 |
| P8 | Sliding (instead of fixed) trait budget window, removing the 2× boundary burst | v0.2 |
