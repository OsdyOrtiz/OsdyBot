using Osdy.Core.States;

namespace Osdy.Core.Vitals;

/// <summary>Energy is a vital, not an emotion: it drains or recovers linearly by state.</summary>
public static class Energy
{
    public static double Advance(double energy, BrainState state, TimeSpan elapsed, VitalsOptions options)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return energy;
        }

        return Math.Clamp(energy + (options.EnergyPerHour[state] * elapsed.TotalHours), 0, 1);
    }
}
