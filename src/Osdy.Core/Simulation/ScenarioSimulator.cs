using Osdy.Core.Events;

namespace Osdy.Core.Simulation;

/// <summary>
/// Replays a scenario in virtual time. Between entries the brain is ticked every tick interval, so spontaneous
/// behavior happens exactly as in a live run, without real waiting.
/// </summary>
public sealed class ScenarioSimulator
{
    public const string Source = "simulator";

    private readonly Brain _brain;
    private readonly Action<StepResult> _onStep;
    private readonly TimeSpan _tickInterval;
    private DateTimeOffset _lastTick;

    public ScenarioSimulator(Brain brain, DateTimeOffset start, Action<StepResult> onStep, TimeSpan? tickInterval = null)
    {
        _brain = brain;
        _onStep = onStep;
        _tickInterval = tickInterval ?? brain.Options.Runtime.TickInterval;
        _lastTick = start;
        Start = start;
    }

    public DateTimeOffset Start { get; }

    public DateTimeOffset Now => _lastTick;

    public void Run(IEnumerable<ScenarioEntry> entries)
    {
        foreach (var entry in entries)
        {
            var at = Start + entry.Offset;
            AdvanceTo(at);
            if (entry.Event is { } type)
            {
                _onStep(_brain.Handle(BrainEvent.Create(type, at, Source)));
            }
        }
    }

    /// <summary>Ticks at every interval up to and including <paramref name="target"/>.</summary>
    public void AdvanceTo(DateTimeOffset target)
    {
        while (_lastTick + _tickInterval <= target)
        {
            _lastTick += _tickInterval;
            _onStep(_brain.Tick(_lastTick));
        }
    }

    public void Post(EventType type, DateTimeOffset at)
    {
        AdvanceTo(at);
        _onStep(_brain.Handle(BrainEvent.Create(type, at, Source)));
    }
}
