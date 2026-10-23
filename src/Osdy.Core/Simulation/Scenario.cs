using System.Globalization;
using Osdy.Core.Events;

namespace Osdy.Core.Simulation;

/// <summary>One scenario line: at <see cref="Offset"/> from the start, either an event or (when null) just "wait".</summary>
public sealed record ScenarioEntry(TimeSpan Offset, EventType? Event);

/// <summary>
/// Plain-text scenario format: <c>HH:MM:SS EventType</c> or <c>HH:MM:SS wait</c>, one per line, <c>#</c> comments.
/// Offsets are relative to the scenario start, may carry a fraction of a second (<c>HH:MM:SS.fff</c>) and must not
/// decrease.
/// </summary>
public static class ScenarioParser
{
    public const string WaitKeyword = "wait";

    private static readonly string[] OffsetFormats =
        [@"hh\:mm\:ss", @"hh\:mm\:ss\.FFFFFFF", @"d\.hh\:mm\:ss", @"d\.hh\:mm\:ss\.FFFFFFF"];

    public static IReadOnlyList<ScenarioEntry> Parse(string text)
    {
        var entries = new List<ScenarioEntry>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw Error(i, $"expected '<offset> <EventType|wait>' but got '{line}'");
            }

            if (!TimeSpan.TryParseExact(parts[0], OffsetFormats, CultureInfo.InvariantCulture, out var offset))
            {
                throw Error(i, $"invalid offset '{parts[0]}' (use HH:MM:SS, D.HH:MM:SS or either with .fff)");
            }

            EventType? evt = null;
            if (!parts[1].Equals(WaitKeyword, StringComparison.OrdinalIgnoreCase))
            {
                if (!Enum.TryParse<EventType>(parts[1], ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
                {
                    throw Error(i, $"unknown event '{parts[1]}'. Valid: {string.Join(", ", Enum.GetNames<EventType>().Where(n => n != nameof(EventType.OwnerRollbackRequested)))}, {WaitKeyword}");
                }

                if (parsed == EventType.OwnerRollbackRequested)
                {
                    throw Error(i, $"'{parts[1]}' needs a stored target and cannot be replayed from a scenario");
                }

                evt = parsed;
            }

            if (entries.Count > 0 && offset < entries[^1].Offset)
            {
                throw Error(i, $"offset {parts[0]} is earlier than the previous line");
            }

            entries.Add(new ScenarioEntry(offset, evt));
        }

        return entries;
    }

    public static string Format(IEnumerable<ScenarioEntry> entries) => string.Concat(entries.Select(e =>
        $"{FormatOffset(e.Offset)} {e.Event?.ToString() ?? WaitKeyword}\n"));

    private static string FormatOffset(TimeSpan offset)
    {
        var format = offset.Days > 0 ? @"d\.hh\:mm\:ss" : @"hh\:mm\:ss";
        if (offset.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            format += @"\.FFFFFFF";
        }

        return offset.ToString(format, CultureInfo.InvariantCulture);
    }

    private static FormatException Error(int index, string message) => new($"Scenario line {index + 1}: {message}");
}
