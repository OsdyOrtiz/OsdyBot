using System.Threading.Channels;
using Osdy.Core.Events;

namespace Osdy.Core.Runtime;

/// <summary>Output port: receives every step that produced records, after it was committed.</summary>
public interface IBrainObserver
{
    void OnStep(StepResult result);

    void OnError(Exception exception);
}

/// <summary>
/// The autonomous loop. A timer and any number of event producers write into one channel; a single consumer steps
/// the brain, commits the result and only then notifies the observer. The brain is never touched concurrently.
/// </summary>
public sealed class BrainRunner
{
    private const int Capacity = 256;

    private readonly IBrainStore _store;
    private readonly IBrainObserver _observer;
    private readonly TimeProvider _time;
    private readonly Channel<BrainEvent?> _inputs = Channel.CreateBounded<BrainEvent?>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    private DateTimeOffset _lastCommit;

    public BrainRunner(Brain brain, IBrainStore store, IBrainObserver observer, TimeProvider time)
    {
        Brain = brain;
        _store = store;
        _observer = observer;
        _time = time;
        _lastCommit = brain.Runtime.LastUpdatedAt;
    }

    /// <summary>The current brain. It is replaced by the last committed snapshot after a failed step.</summary>
    public Brain Brain { get; private set; }

    public ValueTask PostAsync(EventType type, string source, CancellationToken cancellationToken = default) =>
        _inputs.Writer.WriteAsync(BrainEvent.Create(type, _time.GetUtcNow(), source), cancellationToken);

    /// <summary>
    /// Runs until cancelled. On shutdown, events already posted are still processed, then a final checkpoint is
    /// saved. Throws after too many consecutive failures.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Created before the first await so a fake TimeProvider sees the timer immediately.
        using var timer = new PeriodicTimer(Brain.Options.Runtime.TickInterval, _time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ticks = ProduceTicksAsync(timer, stop.Token);
        var failures = 0;

        void Consume(BrainEvent? evt)
        {
            if (Process(evt))
            {
                failures = 0;
            }
            else if (++failures >= Brain.Options.Runtime.MaxConsecutiveFailures)
            {
                throw new InvalidOperationException($"Brain stopped after {failures} consecutive failures.");
            }
        }

        try
        {
            while (await _inputs.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_inputs.Reader.TryRead(out var evt))
                {
                    Consume(evt);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await ticks.ConfigureAwait(false);
        }

        // Drain events that were posted before shutdown; pending ticks are skipped.
        while (_inputs.Reader.TryRead(out var pending))
        {
            if (pending is not null)
            {
                Consume(pending);
            }
        }

        // Final checkpoint so energy and state are saved exactly at shutdown.
        var final = Brain.Tick(_time.GetUtcNow());
        _store.Commit(final);
        if (final.HasRecords)
        {
            _observer.OnStep(final);
        }
    }

    private async Task ProduceTicksAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                // A null input means "tick". If the channel is full, skipping a tick is harmless.
                _inputs.Writer.TryWrite(null);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    /// <returns>False when the step failed and the brain was reloaded from the store.</returns>
    private bool Process(BrainEvent? evt)
    {
        try
        {
            var step = evt is null ? Brain.Tick(_time.GetUtcNow()) : Brain.Handle(evt);
            var checkpointDue = step.At - _lastCommit >= Brain.Options.Runtime.CheckpointInterval;
            if (step.HasRecords || checkpointDue)
            {
                _store.Commit(step);
                _lastCommit = step.At;
            }

            if (step.HasRecords)
            {
                _observer.OnStep(step);
            }

            return true;
        }
#pragma warning disable CA1031 // The loop must survive any step failure; it is reported and the brain reloaded.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _observer.OnError(error);
            Reload();
            return false;
        }
    }

    private void Reload()
    {
        try
        {
            if (_store.Load() is { } snapshot)
            {
                Brain = Brain.Reload(snapshot);
            }
        }
#pragma warning disable CA1031 // A broken store is reported; the in-memory brain keeps running.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _observer.OnError(error);
        }
    }
}
