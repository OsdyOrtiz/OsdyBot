# Domain model

All timestamps are UTC `DateTimeOffset`. All scalar traits and vitals are `double` in `[0, 1]`.

## Entities and value objects

| Type | Kind | Fields | Invariants |
|------|------|--------|------------|
| `RobotIdentity` | Entity | `Id`, `Name`, `CreatedAt` | Created once; never regenerated on restart |
| `TraitKind` | Enum | `Curiosity`, `Sociability`, `Confidence` | v0.2 adds the remaining traits |
| `TraitState` | Value | `Kind`, `Value`, `Baseline`, `BudgetWindowStart`, `BudgetUsed` | `Min ≤ Value ≤ Max` (bounds from options); `BudgetUsed ≤ BudgetPerWindow` |
| `Personality` | Aggregate | one `TraitState` per `TraitKind` | Only `TraitEvolution` mutates values |
| `TraitChange` | Record | `Id`, `OccurredAt`, `Trait`, `OldValue`, `NewValue`, `Delta`, `ExperienceId`, `Rule` | Always linked to the experience that caused it |
| `BrainState` | Enum | `Idle`, `Curious`, `Attentive`, `Interacting`, `Resting` | Only table-approved transitions |
| `RuntimeState` | Value | `State`, `StateEnteredAt`, `Energy`, `LastUpdatedAt`, `LastSpontaneousAt?`, `LastInteractionAt?` | `0 ≤ Energy ≤ 1` |
| `BrainEvent` | Record (observed fact) | `Id`, `Type`, `OccurredAt`, `Source` | Immutable; never contains interpretation |
| `EventType` | Enum | `HumanPresenceDetected`, `HumanLeft`, `HumanInteractionDetected`, `PositiveInteraction`, `NegativeInteraction`, `InteractionEnded`, `NoveltyDetected` | — |
| `Decision` | Record | `Id`, `OccurredAt`, `Trigger`, `EventId?`, `Kind`, `Rule`, `Action`, `FromState`, `ToState`, `Reasons` | `FromState → ToState` is valid or equal |
| `DecisionKind` | Enum | `Reactive`, `Spontaneous`, `System` | `Deliberate` reserved for v0.4 |
| `BehaviorAction` | Enum | e.g. `ObserveAndEngage`, `Investigate`, `Rest`, `NoAction` | `NoAction` is an explicit, recorded choice |
| `Experience` | Record (interpretation) | `Id`, `OccurredAt`, `EventId`, `DecisionId`, `Kind`, `State`, `Action`, `Valence`, `Importance`, `Outcome`, `Source` | `-1 ≤ Valence ≤ 1`, `0 ≤ Importance ≤ 1` |
| `BrainSnapshot` | Aggregate root | `Identity`, `Personality`, `Runtime` | What `IBrainStore.Load` returns |
| `StepResult` | Value | events, decisions, experiences, trait changes, resulting snapshot | Unit of persistence and observation |

## Observed vs interpreted

```
BrainEvent (observed)  ──▶  Decision (what the brain chose and why)
        │                          │
        └────────────▶ Experience (how it was interpreted: valence, importance, outcome)
                                   │
                                   └──▶ TraitChange (what it taught, by which rule)
```

An event is what a sensor (or simulator) reported. An experience is the brain's reading of it. Keeping them apart means we can later re-interpret old events with better rules without corrupting the factual record.

## Experience kinds (v0.1)

| Kind | Produced by | Valence | Importance (default) |
|------|-------------|---------|----------------------|
| `PositiveInteraction` | `PositiveInteraction` event | +1.0 | 0.5 |
| `NegativeInteraction` | `NegativeInteraction` event | −1.0 | 0.7 |
| `NoveltyExplored` | `NoveltyDetected` event that led to `Investigate` | +0.5 | 0.3 |

Negative experiences weigh more than positive ones (a mild negativity bias). This is a tuning *assumption*, configurable in `LearningOptions`.
