using System.Text.Json;
using System.Text.Json.Serialization;

namespace Osdy.Core;

/// <summary>
/// Reads and writes <see cref="BrainOptions"/> as JSON (camelCase, enums as names). Omitted properties keep their
/// defaults; dictionaries and lists given in JSON replace the defaults entirely. Unknown properties are rejected.
/// </summary>
public static class BrainOptionsJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    /// <exception cref="JsonException">Malformed JSON or unknown property.</exception>
    /// <exception cref="ArgumentException">The resulting options are invalid.</exception>
    public static BrainOptions Parse(string json) =>
        (JsonSerializer.Deserialize<BrainOptions>(json, SerializerOptions) ?? new BrainOptions()).EnsureValid();

    public static string Serialize(BrainOptions options) => JsonSerializer.Serialize(options, SerializerOptions);
}
