namespace Osdy.Core.Personality;

/// <summary>Immutable set of all traits. Changes produce a new profile.</summary>
public sealed class PersonalityProfile
{
    private readonly IReadOnlyDictionary<TraitKind, TraitState> _traits;

    private PersonalityProfile(IReadOnlyDictionary<TraitKind, TraitState> traits) => _traits = traits;

    public IReadOnlyCollection<TraitState> Traits => [.. Enum.GetValues<TraitKind>().Select(k => _traits[k])];

    public TraitState this[TraitKind kind] => _traits[kind];

    public static PersonalityProfile Initial(PersonalityOptions options, DateTimeOffset now) =>
        new(Enum.GetValues<TraitKind>().ToDictionary(
            kind => kind,
            kind => new TraitState(kind, options.Initial(kind), options.Initial(kind), now, 0)));

    /// <summary>Rebuilds a profile from stored traits; with options, values are clamped into the current bounds.</summary>
    public static PersonalityProfile Restore(IEnumerable<TraitState> stored, PersonalityOptions? options = null)
    {
        options ??= new PersonalityOptions { TraitMin = 0, TraitMax = 1 };
        var byKind = stored.ToDictionary(t => t.Kind);
        var missing = Enum.GetValues<TraitKind>().Where(k => !byKind.ContainsKey(k)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Stored personality is missing traits: {string.Join(", ", missing)}.");
        }

        return new(byKind.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with { Value = Math.Clamp(pair.Value.Value, options.TraitMin, options.TraitMax) }));
    }

    public PersonalityProfile With(TraitState trait)
    {
        var copy = new Dictionary<TraitKind, TraitState>(_traits) { [trait.Kind] = trait };
        return new PersonalityProfile(copy);
    }
}
