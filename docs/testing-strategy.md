# Testing and simulation strategy

## Rules

- **No real time.** The brain receives time as an argument; the runner uses `TimeProvider`, faked in tests.
- **No randomness.** v0.1 behavior is fully deterministic. If randomness is added later, it will come from an injected, seeded source.
- **Public interface first.** Tests drive `Brain.Start/Handle/Tick`, `ScenarioSimulator`, `IBrainStore` and the CLI, not private helpers.
- **Every rule has a test** that asserts all its observable effects: action, resulting state, reasons, records.

## Test layers

| Layer | Project | Examples |
|-------|---------|----------|
| Unit | `Osdy.Core.Tests` | Trait math, state transition table, each decision rule, energy, options validation |
| Simulation | `Osdy.Core.Tests` | Hundreds/thousands of experiences; Robot A vs Robot B divergence; scenario determinism |
| Integration | `Osdy.Persistence.Sqlite.Tests` | Commit/load round-trip on a temp file; restart recovery; schema version guard |
| Functional | manual CLI run, recorded in the feature log | `osdy simulate scenarios/first-meeting.txt` matches the expected trace shape |

## Simulation

`ScenarioSimulator` replays a scenario in **virtual time**: between two events it ticks at `TickInterval`, so spontaneous behavior (boredom, fatigue, timeouts) happens exactly as in a live run.

Scenario format (plain text, one entry per line, offset from scenario start):

```text
# first meeting
00:00:05 HumanPresenceDetected
00:00:10 HumanInteractionDetected
00:00:20 PositiveInteraction
00:01:00 InteractionEnded
00:10:00 wait
```

`wait` only advances time. Live event logs can be exported to this format (`osdy events export`) and replayed.

## Key properties and how they are proven

| Property | Test |
|----------|------|
| Traits stay in bounds | 10 000 random-free mixed experiences; assert `min ≤ v ≤ max` after each |
| Gradual change | assert every `TraitChange.Delta ≤ MaxStep` |
| Rate limit | 1 000 positive interactions inside one window move each trait ≤ `BudgetPerWindow` |
| No oscillation blow-up | alternating ± experiences keep `|v − v₀| ≤ BudgetPerWindow` per window |
| Divergence | Robot A (daily positive interactions, 30 days) vs Robot B (rare interactions): A's sociability > B's; both within budget of baseline |
| Behavior follows personality | same `HumanInteractionDetected` gives `ObserveCautiously` for a low-sociability robot and `ObserveAndEngage` after enough positive history |
| Replay determinism | same scenario twice → identical decision traces (ignoring generated IDs) |
| Restart | commit, reopen the SQLite file, load → identical identity, traits, experiences; transient state recovers to Idle |

## Commands

```bash
dotnet test                       # all tests (unit, simulation, integration)
```
