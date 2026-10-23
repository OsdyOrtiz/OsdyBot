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
window    : if now − BudgetWindowStart ≥ BudgetWindow → BudgetWindowStart = now, BudgetUsed = 0
Δ₃        = sign(Δ₂) · min(|Δ₂|, BudgetPerWindow − BudgetUsed)   (window budget)
v'        = clamp(v + Δ₃, min, max)
BudgetUsed += |v' − v|
```

A change with `|v' − v| < 1e-6` is discarded (no record).

### Why each term exists

| Term | Prevents |
|------|----------|
| `headroom` | Hitting the bounds abruptly; changes slow down near extremes (diminishing returns) |
| `MaxStep` | A single dramatic experience rewriting the personality |
| Window budget | A burst (100 interactions in an hour) moving a trait more than one budget per window (at most 2 × budget in any span of one window length, see below); also caps oscillation amplitude because movement in *both* directions consumes budget |
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
3. **Rate-limited:** total movement per trait within one budget window ≤ `BudgetPerWindow`. The window is *fixed*, anchored at the first change after the previous window expired — not a sliding window. Worst case, a burst straddling a window boundary moves a trait up to `2 × BudgetPerWindow` within a short span (0.06 with defaults). A true sliding window needs per-trait change history in the brain; it is a v0.2 candidate (pending decision P8).
4. **Explainable:** each change references its experience and rule.
5. **Monotone under consistent experience:** only positive experiences never decrease a positively-gained trait.
6. **Divergence without identity loss:** two instances with the same start but different histories diverge, and each stays within `elapsed windows × BudgetPerWindow` of its baseline.

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
