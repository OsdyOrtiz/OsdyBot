# Roadmap

Each phase ends with something runnable, tested and documented. Later phases are a plan, not a commitment: they are revisited after each release.

| Phase | Theme | Main deliverables |
|-------|-------|-------------------|
| **v0.1** | Osdy Core | Identity, 3 traits, 5 states, rule decisions, energy, experiences + learning, SQLite, runner, CLI simulator, tests |
| **v0.2** | Memory & control | Memory list/show/forget/export, trait history and rollback, recording policy, remaining traits (shyness, behavioral energy, playfulness, initiative), working-memory buffer, mean-reversion decision |
| **v0.3** | Affect & habits | Emotional State Engine (intensity, decay, transitions), utility scoring for decisions, behavioral memory (action-outcome preferences), occasional spontaneous interaction with strict limits |
| **v0.4** | AI integration | `ILanguageModel` port, first provider, conversation, LLM-assisted experience interpretation stored as *inferred*, offline fallback, cost/latency budgets, semantic memory |
| **v0.5** | Desktop body | Perception adapters (webcam presence, microphone activity), expression adapters (animated eyes window, TTS), permission model for external actions, encryption decision for personal data |
| **v0.6** | Embedded | linux-arm64 build, systemd service, watchdog, storage wear and RTC strategy, real head/screen hardware adapters |
| **v1.0** | Living with it | Months-long run, evolution metrics dashboard, stability and privacy review |

## Definition of Done

### Every phase

- [ ] Builds and all tests pass with `dotnet test` on macOS and Linux.
- [ ] No test depends on real time or randomness.
- [ ] README and affected docs updated; new decisions recorded in [decisions.md](decisions.md).
- [ ] Scope items demonstrated with a scenario or a recorded CLI run.
- [ ] Nothing outside the phase scope was added.

### Phase specifics

| Phase | Additional DoD |
|-------|----------------|
| v0.1 | All 12 MVP items and acceptance criteria verified; restart survives with a real SQLite file; 1 000+ experience simulation keeps traits in bounds |
| v0.2 | Deleting a memory removes it from every table and export; rollback restores exact trait values; recording policy honored by tests |
| v0.3 | Affect never writes traits (tested); spontaneous interactions respect hourly caps under a 30-day simulation |
| v0.4 | Brain passes all v0.3 tests with the LLM disabled; provider swap needs no core change; no secrets in logs |
| v0.5 | Core project still has no device references; adapters can be unplugged at runtime without crashing the brain |
| v0.6 | 72-hour soak test on target hardware within agreed CPU/RAM/write budgets; recovers from power loss with a valid state |
| v1.0 | 3-month evolution report: trait trajectories explainable from the experience log |
