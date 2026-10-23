# CLAUDE.md — Osdy Brain

Autonomous, evolving brain for a robotic pet. C# / .NET 10 LTS, SQLite, xUnit. Human-facing docs: `README.md`, `docs/README.md` (index).

## Commands

```bash
dotnet build                                            # warnings are errors
dotnet test 2>&1 | rg "error|\[FAIL\]|Passed!|Failed!"  # whole suite, filtered output
dotnet test tests/Osdy.Core.Tests --filter "FullyQualifiedName~BrainDecisionTests"
dotnet run --project src/Osdy.Cli -- simulate scenarios/first-meeting.txt
```

## Architecture (dependencies point inward)

```
Osdy.Cli ──▶ Osdy.Persistence.Sqlite ──▶ Osdy.Core (BCL only)
```

| Place | Owns | Never |
|-------|------|-------|
| `Osdy.Core` | Domain, rules, learning, `Brain`, `BrainRunner`, simulator, ports (`IBrainStore`, `IBrainObserver`) | I/O, NuGet packages, hardware, LLM SDKs, `DateTime.Now` |
| `Osdy.Persistence.Sqlite` | `IBrainStore` with plain SQL | Domain logic |
| `Osdy.Cli` | Argument parsing, rendering, composition | Decision or learning logic |

- Core folders are named after the domain (`Personality/`, `Decisions/`, `Experiences/`…); add new concepts the same way, never `Helpers/` or `Utils/`.
- New devices, storage or AI providers enter as adapters behind a port. Add a port only when a second implementation or a test needs it.
- Keep the core a pure step function: `Start/Handle/Tick` receive time and return a `StepResult`; the host persists, then notifies.

## Domain invariants (break one = bug)

1. Determinism: no `Random`, no wall clock, no I/O inside `Brain`. Time comes from the caller or `TimeProvider`.
2. Trait values change only through `TraitEvolution.Apply`, triggered by an `Experience`, and always produce a `TraitChange`.
3. State changes only through `StateMachine`'s table; update `docs/execution-loop.md` when the table changes.
4. Every event gets a `Decision` (explicit `NoAction` included), with a rule name and human-readable reasons.
5. Events are observed facts; experiences are interpretations. Never merge them.
6. Affect/vitals never write traits. No behavior may be rewarded for gaining human attention.
7. Tunable parameters live in `*Options` records with defaults and `Validate()`; fixed formula constants are documented in `docs/`.

## C# conventions

- File-scoped namespaces, `sealed` classes, immutable `record`s with `with` for state, nullable enabled.
- Analyzers run at `latest-recommended` with warnings as errors: fix the cause, suppress only with a justification comment.
- Culture-invariant formatting for anything persisted or printed (`CultureInfo.InvariantCulture`).
- Interface parameter names must avoid VB keywords (`step`, `error` fail CA1716): use `result`, `exception`.
- Code, comments, identifiers and docs in English. Console output follows the brief's example: English tags, Spanish messages (ADR-008).

## Testing

- Write a failing test first, then the minimal fix; one test per rule, asserting every observable effect (action, state, reasons, records).
- Test through public entry points: `Brain`, `ScenarioSimulator`, `IBrainStore`, `CliApp`.
- Async runtime tests use `FakeTimeProvider`; `WaitAsync(guard)` only as a hang guard, never as timing.
- Behavior over long periods: use scenarios in virtual time, not loops of sleeps.
- Test names are sentences with underscores (`CA1707` is disabled for `tests/`).
- SQLite tests use a unique temp directory and call `SqliteConnection.ClearAllPools()` before deleting it.

## Persistence

- Schema version lives in `PRAGMA user_version` (`Schema.Version`). Any schema change bumps it and ships an explicit migration plus a test that opens the previous version.
- One transaction per `StepResult`. Check the version before writing anything to a database file.

## Working with minimum tokens

- Before exploring, read only the relevant doc section via `docs/README.md`; don't read whole docs or the whole tree.
- Locate with `rg -n "<symbol>" src tests` and read files with offset/limit; never reread a file you just edited.
- Filter command output (see Commands); show full test logs only for failures (`rg -A6 "Error Message"`).
- Run the focused test with `--filter` while iterating; run the whole suite once before committing.
- Prefer a small `Edit` over rewriting a file; don't print code back in chat that already exists in the repo.
- Answer with the decision and result; skip restating plans or file lists the diff already shows.

## Scope and delivery

- Current milestone is in `docs/roadmap.md`; anything outside it goes to `docs/backlog.md`, not into code.
- A behavior change ships with its tests and the doc it affects; a decision with alternatives gets an ADR row in `docs/decisions.md`.
- Conventional commits, one work unit per commit (behavior + tests + docs together). Never add AI attribution.
- Feature tracking lives in `odd/tasks/<feature>.md`.
