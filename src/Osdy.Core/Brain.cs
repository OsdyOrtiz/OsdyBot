using System.Globalization;
using Osdy.Core.Decisions;
using Osdy.Core.Decisions.Rules;
using Osdy.Core.Events;
using Osdy.Core.Experiences;
using Osdy.Core.Identity;
using Osdy.Core.Personality;
using Osdy.Core.States;
using Osdy.Core.Vitals;

namespace Osdy.Core;

/// <summary>
/// Osdy Core: a deterministic step function. Time always comes in as an argument; the brain never reads a clock,
/// performs I/O or touches hardware. Callers persist and publish the returned <see cref="StepResult"/>.
/// </summary>
public sealed class Brain
{
    public const string RollbackRule = "owner-rollback";

    private readonly DecisionEngine _engine;

    private Brain(BrainOptions options, BrainSnapshot snapshot, DecisionEngine engine)
    {
        Options = options;
        Identity = snapshot.Identity;
        Personality = snapshot.Personality;
        Runtime = snapshot.Runtime;
        _engine = engine;
    }

    public BrainOptions Options { get; }

    public RobotIdentity Identity { get; }

    public PersonalityProfile Personality { get; private set; }

    public RuntimeState Runtime { get; private set; }

    public BrainSnapshot Snapshot => new(Identity, Personality, Runtime);

    public static Brain CreateNew(BrainOptions options, DateTimeOffset now, DecisionEngine? engine = null)
    {
        options.EnsureValid();
        var snapshot = new BrainSnapshot(
            RobotIdentity.Create(options.RobotName, now),
            PersonalityProfile.Initial(options.Personality, now),
            new RuntimeState(BrainState.Idle, now, options.Vitals.InitialEnergy, now, null, null));
        return new Brain(options, snapshot, engine ?? DecisionEngine.Default);
    }

    public static Brain Restore(BrainSnapshot snapshot, BrainOptions options, DecisionEngine? engine = null)
    {
        options.EnsureValid();
        var cutoff = snapshot.Runtime.LastUpdatedAt - options.Learning.BudgetWindow;
        var traits = snapshot.Personality.Traits.Select(trait =>
        {
            var recent = trait.RecentChanges
                .Where(change => change.OccurredAt > cutoff && change.OccurredAt <= snapshot.Runtime.LastUpdatedAt)
                .ToArray();
            return recent.Length == trait.RecentChanges.Count
                ? trait
                : trait with { RecentChanges = Array.AsReadOnly(recent) };
        });
        var personality = PersonalityProfile.Restore(traits, options.Personality);
        var runtime = snapshot.Runtime with { Energy = Math.Clamp(snapshot.Runtime.Energy, 0, 1) };
        return new Brain(options, snapshot with { Personality = personality, Runtime = runtime }, engine ?? DecisionEngine.Default);
    }

    /// <summary>A brain restored from <paramref name="snapshot"/> with this brain's options and decision engine.</summary>
    public Brain Reload(BrainSnapshot snapshot) => Restore(snapshot, Options, _engine);

    /// <summary>
    /// Resumes after a (re)start: downtime counts as rest, and transient states are not resumed.
    /// </summary>
    public StepResult Start(DateTimeOffset now)
    {
        var downtime = now - Runtime.LastUpdatedAt;
        now = Monotonic(now);
        var energy = Energy.Advance(Runtime.Energy, BrainState.Resting, downtime, Options.Vitals);
        Runtime = Runtime with { Energy = energy, LastUpdatedAt = Max(now, Runtime.LastUpdatedAt) };

        Decision? decision = null;
        if (StateMachine.IsTransient(Runtime.State))
        {
            var context = Context(now, null);
            var choice = new Choice(
                DecisionKind.System,
                BehaviorAction.RecoverAfterRestart,
                BrainState.Idle,
                [$"State {Runtime.State} is transient; resuming as Idle after {Format.T(downtime < TimeSpan.Zero ? TimeSpan.Zero : downtime)} downtime"]);
            decision = DecisionEngine.Stamp(context, DecisionTrigger.Startup, "restart-recovery", choice);
            Apply(decision);
        }

        return Result(now, DecisionTrigger.Startup, null, decision, null, []);
    }

    public StepResult Handle(BrainEvent evt)
    {
        if (evt.Type == EventType.OwnerRollbackRequested)
        {
            throw new ArgumentException("Owner rollbacks need the stored trait history; use Rollback.", nameof(evt));
        }

        var now = Monotonic(evt.OccurredAt);
        AdvanceVitals(now);
        if (evt.IsInteraction)
        {
            Runtime = Runtime with { LastInteractionAt = now };
        }

        var decision = _engine.Decide(Context(now, evt), DecisionTrigger.Event)!;
        Apply(decision);

        var experience = LearningEngine.Interpret(evt, decision, Options.Experiences);
        IReadOnlyList<TraitChange> changes = [];
        if (experience is not null)
        {
            (Personality, changes) = LearningEngine.Learn(experience, Personality, Options);
        }

        return Result(now, DecisionTrigger.Event, evt, decision, experience, changes);
    }

    /// <summary>
    /// Undoes the target trait change and every later one, across the whole profile, by restoring the values recorded
    /// immediately before it. <paramref name="history"/> is the stored trait changes in persistence order.
    /// </summary>
    public StepResult Rollback(BrainEvent request, IReadOnlyList<TraitChange> history)
    {
        if (request is not { Type: EventType.OwnerRollbackRequested, TargetId: { } target })
        {
            throw new ArgumentException("A rollback needs an OwnerRollbackRequested event that names its target change.", nameof(request));
        }

        // Plan and validate everything first: a rejected rollback leaves the brain untouched.
        var now = Monotonic(request.OccurredAt);
        var restoredValues = PersonalityRollback.ValuesBefore(history, target);
        var experienceId = Guid.CreateVersion7(now);
        var personality = Personality;
        var changes = new List<TraitChange>();
        foreach (var (kind, value) in restoredValues)
        {
            var before = personality[kind];
            var update = TraitEvolution.Apply(before, value, Options.Personality);
            personality = personality.With(update.Trait);
            if (update.Delta != 0)
            {
                changes.Add(new TraitChange(
                    Guid.CreateVersion7(now), now, kind, before.Value, update.Trait.Value, update.Delta,
                    experienceId, RollbackRule, TraitChangeKind.Restoration, target));
            }
        }

        AdvanceVitals(now);
        Personality = personality;

        // The state machine is not involved: the decision keeps the current state and its clock.
        var summary = changes.Count == 0
            ? "Profile was already at those values; nothing to restore"
            : $"Restored {string.Join(", ", changes.Select(c => $"{c.Trait} {c.OldValue.ToString("0.0000", CultureInfo.InvariantCulture)} -> {c.NewValue.ToString("0.0000", CultureInfo.InvariantCulture)}"))}";
        var choice = new Choice(
            DecisionKind.Reactive,
            BehaviorAction.RestorePersonality,
            Runtime.State,
            [$"Owner asked to undo trait change {target} and every later change", summary]);
        var decision = DecisionEngine.Stamp(Context(now, request), DecisionTrigger.Event, RollbackRule, choice);
        var experience = new Experience(
            experienceId, now, request.Id, decision.Id, ExperienceKind.OwnerRollback, Runtime.State,
            decision.Action, 0, 0, $"Rolled back to before trait change {target}", request.Source);
        return Result(now, DecisionTrigger.Event, request, decision, experience, changes);
    }

    public StepResult Tick(DateTimeOffset now)
    {
        now = Monotonic(now);
        AdvanceVitals(now);
        var decision = _engine.Decide(Context(now, null), DecisionTrigger.Tick);
        if (decision is not null)
        {
            Apply(decision);
        }

        return Result(now, DecisionTrigger.Tick, null, decision, null, []);
    }

    private DecisionContext Context(DateTimeOffset now, BrainEvent? evt) => new(now, evt, Runtime, Personality, Options);

    private void AdvanceVitals(DateTimeOffset now)
    {
        var energy = Energy.Advance(Runtime.Energy, Runtime.State, now - Runtime.LastUpdatedAt, Options.Vitals);
        Runtime = Runtime with { Energy = energy, LastUpdatedAt = Max(now, Runtime.LastUpdatedAt) };
    }

    private void Apply(Decision decision)
    {
        StateMachine.EnsureValid(Runtime.State, decision.ToState);

        // A deliberate action re-enters its state (e.g. investigating novelty while already curious restarts the
        // curiosity period); NoAction keeps the current state's clock running.
        if (decision.ChangesState || decision.Action != BehaviorAction.NoAction)
        {
            Runtime = Runtime with { State = decision.ToState, StateEnteredAt = decision.OccurredAt };
        }

        if (decision.Action == BehaviorAction.ExploreSpontaneously)
        {
            Runtime = Runtime with { LastSpontaneousAt = decision.OccurredAt };
        }
    }

    private StepResult Result(
        DateTimeOffset now,
        DecisionTrigger trigger,
        BrainEvent? evt,
        Decision? decision,
        Experience? experience,
        IReadOnlyList<TraitChange> changes) =>
        new(now, trigger, evt, decision, experience, changes, Snapshot);

    /// <summary>
    /// Step time never goes backwards: an input stamped before the last step (an event posted before a tick that was
    /// processed first) is decided at the last step's time. The event itself keeps its observed time.
    /// </summary>
    private DateTimeOffset Monotonic(DateTimeOffset at) => Max(at, Runtime.LastUpdatedAt);

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
