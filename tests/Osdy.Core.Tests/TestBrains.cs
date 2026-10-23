using Osdy.Core.Events;
using Osdy.Core.Personality;

namespace Osdy.Core.Tests;

internal static class TestBrains
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    public static Brain New(BrainOptions? options = null) => Brain.CreateNew(options ?? new BrainOptions(), T0);

    public static BrainOptions WithTraits(double curiosity, double sociability, double confidence) => new()
    {
        Personality = new PersonalityOptions
        {
            InitialCuriosity = curiosity,
            InitialSociability = sociability,
            InitialConfidence = confidence,
        },
    };

    public static BrainOptions WithEnergy(double energy) => new() { Vitals = new() { InitialEnergy = energy } };

    public static StepResult Handle(this Brain brain, EventType type, TimeSpan at) =>
        brain.Handle(BrainEvent.Create(type, T0 + at, "test"));

    public static StepResult Tick(this Brain brain, TimeSpan at) => brain.Tick(T0 + at);

    public static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);
}
