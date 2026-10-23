using System.Globalization;

namespace Osdy.Core.Decisions.Rules;

/// <summary>Culture-invariant formatting so reasons read the same on every machine.</summary>
internal static class Format
{
    public static string N(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Whole seconds, constant format: [d.]hh:mm:ss.</summary>
    public static string T(TimeSpan value) =>
        TimeSpan.FromSeconds(Math.Round(value.TotalSeconds)).ToString("c", CultureInfo.InvariantCulture);
}
