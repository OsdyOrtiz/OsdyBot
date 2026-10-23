# Personality evolution model

Personality changes **only** through experiences, in small bounded steps, and every change is recorded with its cause. No random drift.

## Three time scales

| Layer | Example | Changes | Persisted as |
|-------|---------|---------|--------------|
| Traits (stable) | Confidence 0.40 | Hours to months, bounded budget | `traits`, `trait_changes` |
| Vitals / states (temporary) | Energy 0.35, Interacting | Seconds to hours | `runtime_state` |
| Affect (temporary, v0.3) | Joy 0.6 decaying | Seconds to minutes | not in v0.1 |

Temporary layers never write traits directly. Only `LearningEngine`, from an `Experience`, can.

## Update rule

For an experience with valence `s ∈ [−1, 1]` and importance `w ∈ [0, 1]`, and a learning rule with gain `g` for trait `T` (current value `v`, bounds `[min, max]`):

```
raw       = η · g · s · w                                   (η = LearningRate)
headroom  = (max − v)/(max − min)  if raw > 0
            (v − min)/(max − min)  if raw < 0               (soft bound, in [0, 1])
Δ₁        = raw · headroom
Δ₂        = clamp(Δ₁, −MaxStep, +MaxStep)                   (per-experience cap)
recent    = changes with time in (now − BudgetWindow, now]  (prune expired entries)
used      = sum(|Δ| for each recent change)
Δ₃        = sign(Δ₂) · min(|Δ₂|, max(0, BudgetPerWindow − used)) (sliding budget)
v'        = clamp(v + Δ₃, min, max)
recent   += (now, |v' − v|)                                 (only if not discarded)
```

A change with `|v' − v| < 1e-6` is discarded (no record).

### Why each term exists

| Term | Prevents |
|------|----------|
| `headroom` | Hitting the bounds abruptly; changes slow down near extremes (diminishing returns) |
| `MaxStep` | A single dramatic experience rewriting the personality |
| Window budget | A burst (100 interactions in an hour) moving a trait more than 1 × budget in any span of one window length; also caps oscillation amplitude because movement in *both* directions consumes budget |
| `clamp` | Floating-point overshoot |
| `TraitChange` log | Unexplainable changes; enables rollback (v0.2) by reverting deltas after a point |

### Defaults (configurable, *tuning assumptions*)

| Parameter | Default |
|-----------|---------|
| Bounds `[min, max]` | `[0.05, 0.95]` |
| Initial values | Curiosity 0.70, Sociability 0.35, Confidence 0.40 |
| `LearningRate η` | 0.01 |
| `MaxStep` | 0.005 |
| `BudgetPerWindow` / `BudgetWindow` | 0.03 per trait / 24 h |

### Learning rules (v0.1)

| Rule | Experience | Gains |
|------|------------|-------|
| `positive-interaction` | `PositiveInteraction` | Sociability 1.0, Confidence 0.6 |
| `negative-interaction` | `NegativeInteraction` | Confidence 1.0, Sociability 0.5 |
| `novelty-explored` | `NoveltyExplored` | Curiosity 1.0 |

Worked example (matches the brief): Confidence 0.40, one positive interaction (`s = 1`, `w = 0.5`):
`raw = 0.01·0.6·1·0.5 = 0.003`, `headroom = 0.55/0.90 ≈ 0.611`, `Δ ≈ +0.0018` → printed `Confidence +0.002`.

## Guaranteed properties (tested)

1. **Bounded:** `min ≤ v ≤ max` after any sequence of experiences.
2. **Gradual:** `|Δ| ≤ MaxStep` per experience.
3. **Rate-limited:** total absolute movement per trait in **any** interval `(t − BudgetWindow, t]` is ≤ `1 × BudgetPerWindow` (0.03 with defaults), including bursts across former fixed-window boundaries. `TraitState` keeps recent changes as `(time, |Δ|)`; each change consumes the remaining trailing-window budget. Entries expire exactly at the left edge. Proof: at every change the trailing sum is capped; any window-length interval has no more movement than the trailing window ending at its last change.
4. **Explainable:** each change references its experience and rule.
5. **Monotone under consistent experience:** only positive experiences never decrease a positively-gained trait.
6. **Divergence without identity loss:** two instances with the same start but different histories diverge, while absolute movement in every sliding window stays ≤ `BudgetPerWindow`. Over elapsed time `T ≥ 0` since initialization, displacement from baseline is at most `(floor(T / BudgetWindow) + 1) × BudgetPerWindow`, as well as being bounded by the trait limits. Both movement directions consume the same budget.

## Owner corrections vs learning bounds

`osdy rollback` (ADR-018) is not learning, so properties 1-6 describe `Learning` changes only. A correction restores exact recorded values through `TraitEvolution.Apply` (a separate overload: no headroom, `MaxStep` or budget), still rejects values outside `TraitMin..TraitMax`, and is recorded as a `Restoration` change. Restorations are excluded from the sliding budget: the original learning stays spent and the compensation costs nothing, so a rollback neither refunds budget nor consumes it. The movement bound therefore holds for learning in every window, while an owner correction may move a trait by more than `BudgetPerWindow`, because it only returns it to a value it already held. Affect and vitals still never write traits (invariant 6).

## Measuring evolution

| What we measure | How | Distinguishes |
|-----------------|-----|---------------|
| Personality change | `value − baseline` per trait over time, from `trait_changes` | Long-term identity |
| Temporary state | State/energy time series from `decisions` | Moods/fatigue, which must not leak into traits |
| Learned behavior | Distribution of actions for the same event before/after (e.g. `ObserveCautiously` → `ObserveAndEngage`) | Behavior change caused by trait change |
| Preferences (v0.3) | Action-outcome scores per context | Habits, separate from traits |

## Open questions

- **Mean reversion** toward baseline (a slow pull back when nothing happens). Rejected for v0.1 to keep the model simple and every change experience-driven; reconsider in v0.2 once long simulations show whether traits feel "stuck".
- **Per-trait parameters** (e.g. confidence more plastic than curiosity). The options support only global η/MaxStep/budget in v0.1.
