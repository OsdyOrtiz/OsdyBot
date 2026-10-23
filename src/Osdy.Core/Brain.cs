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
        var personality = PersonalityProfile.Restore(snapshot.Personality.Traits, options.Personality);
        var runtime = snapshot.Runtime with { Energy = Math.Clamp(snapshot.Runtime.Energy, 0, 1) };
        return new Brain(options, snapshot with { Personality = personality, Runtime = runtime }, engine ?? DecisionEngine.Default);
    }

    /// <summary>
    /// Resumes after a (re)start: downtime counts as rest, and transient states are not resumed.
    /// </summary>
    public StepResult Start(DateTimeOffset now)
    {
        var downtime = now - Runtime.LastUpdatedAt;
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
        var now = evt.OccurredAt;
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

    public StepResult Tick(DateTimeOffset now)
    {
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

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
