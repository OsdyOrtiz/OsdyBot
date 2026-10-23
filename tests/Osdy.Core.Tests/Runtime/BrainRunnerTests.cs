using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using Osdy.Core.Decisions;
using Osdy.Core.Events;
using Osdy.Core.Runtime;
using Osdy.Core.States;
using static Osdy.Core.Tests.TestBrains;

namespace Osdy.Core.Tests.Runtime;

public class BrainRunnerTests
{
    // Safety net only: tests never wait for behavior by time, they wait for a signal and fail if it never comes.
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Timer_ticks_drive_spontaneous_behavior_on_a_fake_clock()
    {
        var time = new FakeTimeProvider(T0);
        var store = new InMemoryBrainStore();
        var observer = new RecordingObserver(s => s.Decision?.Action == BehaviorAction.ExploreSpontaneously);
        var runner = new BrainRunner(New(), store, observer, time);
        using var cts = new CancellationTokenSource();

        var run = runner.RunAsync(cts.Token);
        time.Advance(S(97));
        var step = await observer.Matched.WaitAsync(Guard);
        await cts.CancelAsync();
        await run.WaitAsync(Guard);

        Assert.Equal(T0 + S(97), step.At);
        Assert.Equal(BrainState.Curious, runner.Brain.Runtime.State);
        Assert.Contains(store.GetRecentDecisions(10), d => d.Action == BehaviorAction.ExploreSpontaneously);
    }

    [Fact]
    public async Task Posted_events_are_committed_before_the_observer_is_notified()
    {
        var time = new FakeTimeProvider(T0);
        var store = new InMemoryBrainStore();
        MemoryStats? statsSeenByObserver = null;
        var observer = new RecordingObserver(s => s.Event is not null, onStep: _ => statsSeenByObserver = store.GetStats());
        var runner = new BrainRunner(New(), store, observer, time);
        using var cts = new CancellationTokenSource();

        var run = runner.RunAsync(cts.Token);
        await runner.PostAsync(EventType.HumanInteractionDetected, "test");
        var step = await observer.Matched.WaitAsync(Guard);
        await cts.CancelAsync();
        await run.WaitAsync(Guard);

        Assert.Equal(BehaviorAction.ObserveAndEngage, step.Decision!.Action);
        Assert.Equal(1, statsSeenByObserver!.Events);
        Assert.Equal("test", store.GetEvents().Single().Source);
    }

    [Fact]
    public async Task Shutdown_saves_a_final_checkpoint()
    {
        var time = new FakeTimeProvider(T0);
        var store = new InMemoryBrainStore();
        var runner = new BrainRunner(New(), store, new RecordingObserver(_ => false), time);
        using var cts = new CancellationTokenSource();

        var run = runner.RunAsync(cts.Token);
        time.SetUtcNow(T0 + S(10));
        await cts.CancelAsync();
        await run.WaitAsync(Guard);

        Assert.Equal(T0 + S(10), store.Load()!.Runtime.LastUpdatedAt);
    }

    [Fact]
    public async Task Events_posted_before_shutdown_are_still_processed()
    {
        var time = new FakeTimeProvider(T0);
        var store = new InMemoryBrainStore();
        var runner = new BrainRunner(New(), store, new RecordingObserver(_ => false), time);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await runner.PostAsync(EventType.HumanInteractionDetected, "test");
        await runner.PostAsync(EventType.PositiveInteraction, "test");
        await runner.RunAsync(cts.Token).WaitAsync(Guard);

        Assert.Equal(2, store.GetStats().Events);
        Assert.Equal(1, store.GetStats().Experiences);
    }

    [Fact]
    public async Task A_failed_commit_is_reported_and_the_brain_reloads_the_last_committed_state()
    {
        var time = new FakeTimeProvider(T0);
        var store = new FlakyStore(failures: 1);
        var brain = New();
        store.Inner.Commit(brain.Start(T0));
        var observer = new RecordingObserver(s => s.Event?.Type == EventType.NoveltyDetected);
        var runner = new BrainRunner(brain, store, observer, time);
        using var cts = new CancellationTokenSource();

        var run = runner.RunAsync(cts.Token);
        await runner.PostAsync(EventType.HumanInteractionDetected, "test"); // commit fails → reload (Idle)
        await runner.PostAsync(EventType.NoveltyDetected, "test");          // succeeds from the reloaded state
        var step = await observer.Matched.WaitAsync(Guard);
        await cts.CancelAsync();
        await run.WaitAsync(Guard);

        Assert.IsType<IOException>(Assert.Single(observer.Errors));
        Assert.Equal(BrainState.Idle, step.Decision!.FromState);
        Assert.Equal(BehaviorAction.Investigate, step.Decision.Action);
        Assert.Equal([EventType.NoveltyDetected], store.Inner.GetEvents().Select(e => e.Type));
    }

    [Fact]
    public async Task Reloading_after_a_failed_commit_keeps_the_brains_decision_engine()
    {
        var time = new FakeTimeProvider(T0);
        var store = new FlakyStore(failures: 1);
        var brain = Brain.CreateNew(new BrainOptions(), T0, new DecisionEngine([new IgnoreEventsRule()]));
        store.Inner.Commit(brain.Start(T0));
        var observer = new RecordingObserver(s => s.Event?.Type == EventType.NoveltyDetected);
        var runner = new BrainRunner(brain, store, observer, time);
        using var cts = new CancellationTokenSource();

        var run = runner.RunAsync(cts.Token);
        await runner.PostAsync(EventType.HumanInteractionDetected, "test"); // commit fails → reload
        await runner.PostAsync(EventType.NoveltyDetected, "test");
        var step = await observer.Matched.WaitAsync(Guard);
        await cts.CancelAsync();
        await run.WaitAsync(Guard);

        Assert.IsType<IOException>(Assert.Single(observer.Errors));
        Assert.Equal(IgnoreEventsRule.RuleName, step.Decision!.Rule);
        Assert.Equal(BehaviorAction.NoAction, step.Decision.Action);
        Assert.Equal(BrainState.Idle, runner.Brain.Runtime.State);
    }

    [Fact]
    public async Task Too_many_consecutive_failures_stop_the_loop()
    {
        var time = new FakeTimeProvider(T0);
        var store = new FlakyStore(failures: int.MaxValue);
        var observer = new RecordingObserver(_ => false);
        var runner = new BrainRunner(New(), store, observer, time);

        var run = runner.RunAsync(CancellationToken.None);
        for (var i = 0; i < 5; i++)
        {
            await runner.PostAsync(EventType.HumanLeft, "test");
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(Guard));
        Assert.Equal("Brain stopped after 5 consecutive failures.", error.Message);
    }

    private sealed class RecordingObserver(Func<StepResult, bool> match, Action<StepResult>? onStep = null) : IBrainObserver
    {
        private readonly TaskCompletionSource<StepResult> _matched = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<StepResult> Matched => _matched.Task;

        public ConcurrentQueue<Exception> Errors { get; } = new();

        public void OnStep(StepResult result)
        {
            onStep?.Invoke(result);
            if (match(result))
            {
                _matched.TrySetResult(result);
            }
        }

        public void OnError(Exception exception) => Errors.Enqueue(exception);
    }

    private sealed class IgnoreEventsRule : IDecisionRule
    {
        public const string RuleName = "ignore-events";

        public string Name => RuleName;

        public Choice? Evaluate(DecisionContext context) => context.Event is null
            ? null
            : new Choice(DecisionKind.Reactive, BehaviorAction.NoAction, context.State, ["Custom engine ignores events"]);
    }

    private sealed class FlakyStore(int failures) : IBrainStore
    {
        private int _remaining = failures;

        public InMemoryBrainStore Inner { get; } = new();

        public void Commit(StepResult result)
        {
            if (_remaining > 0)
            {
                _remaining--;
                throw new IOException("disk unavailable");
            }

            Inner.Commit(result);
        }

        public BrainSnapshot? Load() => Inner.Load();

        public IReadOnlyList<Decision> GetRecentDecisions(int count) => Inner.GetRecentDecisions(count);

        public IReadOnlyList<Osdy.Core.Experiences.Experience> GetRecentExperiences(int count) => Inner.GetRecentExperiences(count);

        public IReadOnlyList<Osdy.Core.Experiences.TraitChange> GetRecentTraitChanges(int count) => Inner.GetRecentTraitChanges(count);

        public IReadOnlyList<BrainEvent> GetEvents() => Inner.GetEvents();

        public MemoryStats GetStats() => Inner.GetStats();
    }
}
