using Osdy.Core.States;

namespace Osdy.Core.Vitals;

public sealed record VitalsOptions
{
    public double InitialEnergy { get; init; } = 1.0;
    public double RestThreshold { get; init; } = 0.20;
    public double WakeThreshold { get; init; } = 0.80;

    /// <summary>Energy change per hour while in each state (negative drains, positive recovers).</summary>
    public IReadOnlyDictionary<BrainState, double> EnergyPerHour { get; init; } = new Dictionary<BrainState, double>
    {
        [BrainState.Idle] = -0.02,
        [BrainState.Curious] = -0.10,
        [BrainState.Attentive] = -0.05,
        [BrainState.Interacting] = -0.15,
        [BrainState.Resting] = 0.30,
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (InitialEnergy is < 0 or > 1)
        {
            errors.Add("InitialEnergy must be within [0, 1].");
        }

        if (RestThreshold < 0 || WakeThreshold > 1 || RestThreshold >= WakeThreshold)
        {
            errors.Add("Energy thresholds must satisfy 0 <= RestThreshold < WakeThreshold <= 1.");
        }

        foreach (var state in Enum.GetValues<BrainState>().Where(s => !EnergyPerHour.ContainsKey(s)))
        {
            errors.Add($"EnergyPerHour is missing a rate for {state}.");
        }

        if (EnergyPerHour.TryGetValue(BrainState.Resting, out var resting) && resting <= 0)
        {
            errors.Add("Resting must recover energy (positive rate).");
        }

        return errors;
    }
}
