# Osdy Brain

The autonomous, evolving brain of a small robotic pet. **Osdy Core** is its runtime nucleus: it perceives events, keeps persistent internal state, decides on its own, remembers experiences and lets its personality change slowly and explainably.

v0.1 runs on a regular computer with simulated events: no hardware, no LLM, no Internet, no API keys.

## Quick start

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet test                                                        # 108 tests: unit, simulation, SQLite, CLI
dotnet run --project src/Osdy.Cli -- simulate scenarios/first-meeting.txt
```

Expected output (abridged):

```text
[BRAIN] Iniciando...
[IDENTITY] Identidad creada: Osdy (01a1…)
[MEMORY] Experiencias recuperadas: 0 (eventos 0, decisiones 0, cambios de rasgos 0)
[STATE] Idle
[TRAIT] Curiosity: 0.70
[TRAIT] Sociability: 0.35
[TRAIT] Confidence: 0.40
[TIME] +00:00:10
[EVENT] HumanInteractionDetected
[DECISION] ObserveAndEngage (human-interaction)
[REASON] Engagement 0.47 >= 0.45 (curiosity 0.70, sociability 0.35, confidence 0.40)
[REASON] Energy sufficient: 1.00 >= 0.30
[STATE] Interacting
[TIME] +00:00:20
[EVENT] PositiveInteraction
[DECISION] EnjoyInteraction (interaction-feedback)
[EXPERIENCE] Saved (PositiveInteraction, valence +1.00, importance 0.50)
[LEARNING] Sociability +0.003 (positive-interaction)
[LEARNING] Confidence +0.002 (positive-interaction)
```

## Commands

| Command | What it does |
|---------|--------------|
| `osdy run [--db <file>] [--config <file.json>]` | Runs the brain in real time. Type a key + Enter: `p` presence, `l` left, `h` interaction, `+` positive, `-` negative, `e` interaction ended, `n` novelty, `s` status, `q` quit (also Ctrl+C or end of input). |
| `osdy simulate <scenario.txt> [--db <file>] [--config <file.json>]` | Replays a scenario in virtual time (seconds per day of behavior). Without `--db` nothing is saved. A `recording.eventTypes` config list chooses which event types are stored and learned from ([recording policy](docs/memory-and-persistence.md#recording-policy-adr-019)). |
| `osdy status [--db <file>] [--count <n>]` | Identity, traits vs. baseline, state, energy, and the latest decisions (with reasons), experiences and trait changes. |
| `osdy memories list [--db <file>] [--count <n>]` | Lists the latest experiences (default 10), oldest first, without modifying the database. |
| `osdy memories show <id> [--db <file>]` | Inspects an experience, its event, decision and trait changes, read-only. |
| `osdy forget <id> [--db <file>]` | Atomically deletes an experience and its event; keeps decisions and learned trait changes as tombstones. Does not undo learning (see `rollback`). |
| `osdy rollback <change-id> [--db <file>] [--config <file.json>]` | Undoes that trait change and every later one across all traits, restoring the exact values from just before it. Keeps all history and records the correction; learning budget unchanged. `events export` refuses databases with rollbacks (use `osdy export`). |
| `osdy events export <scenario.txt> [--db <file>]` | Exports stored events to the scenario format so they can be replayed. |
| `osdy export <file.json> [--db <file>]` | Exports all saved memory as [versioned JSON](docs/memory-and-persistence.md#json-export-format-version-1), opening the database read-only. Requires a new output file. |

During development, prefix with `dotnet run --project src/Osdy.Cli --`. The default database is `data/osdy-brain.db` (git-ignored). Exit codes: `0` ok, `1` runtime error, `2` usage error.

### Restart survival in 3 steps

```bash
dotnet run --project src/Osdy.Cli -- simulate scenarios/first-meeting.txt --db data/demo.db
dotnet run --project src/Osdy.Cli -- simulate scenarios/rough-afternoon.txt --db data/demo.db   # "Identidad cargada"
dotnet run --project src/Osdy.Cli -- status --db data/demo.db
```

> Simulations run in virtual time, so `simulate --db` moves that database's clock ahead of the real one. Keep simulation databases separate from the one used by `osdy run`; otherwise timed behaviors pause until real time catches up (known v0.1 limitation).

### Scenarios

One entry per line, offset from the start, `#` for comments:

```text
00:00:05 HumanPresenceDetected
00:00:10 HumanInteractionDetected
00:00:12.5 PositiveInteraction   # offsets may carry a fraction of a second
1.02:00:00 wait          # D.HH:MM:SS for multi-day scenarios; "wait" only advances time
```

Events: `HumanPresenceDetected`, `HumanLeft`, `HumanInteractionDetected`, `PositiveInteraction`, `NegativeInteraction`, `InteractionEnded`, `NoveltyDetected`.

### Configuration

Every parameter has a documented default ([personality model](docs/personality-model.md), [execution loop](docs/execution-loop.md)). Override any subset with JSON; unknown properties are rejected so typos do not pass silently:

```json
{
  "robotName": "Pip",
  "personality": { "initialSociability": 0.6 },
  "learning": { "maxStep": 0.004 },
  "decisions": { "minTimeBetweenSpontaneous": "00:10:00" }
}
```

## Project layout

```text
src/Osdy.Core                  Domain + Osdy Core runtime (no I/O, no hardware, BCL only)
src/Osdy.Persistence.Sqlite    IBrainStore over SQLite (Microsoft.Data.Sqlite)
src/Osdy.Cli                   Console host (assembly: osdy)
tests/*                        xUnit: unit, simulation, SQLite integration, CLI
scenarios/                     Sample scenarios
docs/                          Vision, architecture, models, roadmap, backlog, decisions
```

## Documentation

Start at [docs/README.md](docs/README.md). Key decisions and open questions live in [docs/decisions.md](docs/decisions.md).
