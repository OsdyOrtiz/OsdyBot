using Osdy.Core.Decisions;
using Osdy.Core.Events;
using Osdy.Core.Personality;

namespace Osdy.Core.Experiences;

public static class LearningEngine
{
    /// <summary>Turns an event and the decision it caused into an experience, when the event is meaningful.</summary>
    public static Experience? Interpret(BrainEvent evt, Decision decision, ExperienceOptions options)
    {
        ExperienceKind? kind = evt.Type switch
        {
            EventType.PositiveInteraction => ExperienceKind.PositiveInteraction,
            EventType.NegativeInteraction => ExperienceKind.NegativeInteraction,
            EventType.NoveltyDetected when decision.Action == BehaviorAction.Investigate => ExperienceKind.NoveltyExplored,
            _ => null,
        };

        if (kind is null)
        {
            return null;
        }

        var appraisal = options.Appraisals[kind.Value];
        return new Experience(
            Guid.CreateVersion7(evt.OccurredAt),
            evt.OccurredAt,
            evt.Id,
            decision.Id,
            kind.Value,
            decision.FromState,
            decision.Action,
            appraisal.Valence,
            appraisal.Importance,
            $"{decision.Action} -> {decision.ToState}",
            evt.Source);
    }

    /// <summary>Applies every learning rule for the experience through the bounded trait model.</summary>
    public static (PersonalityProfile Personality, IReadOnlyList<TraitChange> Changes) Learn(
        Experience experience,
        PersonalityProfile personality,
        BrainOptions options)
    {
        var changes = new List<TraitChange>();
        foreach (var rule in options.Experiences.Rules.Where(r => r.Experience == experience.Kind))
        {
            foreach (var (trait, gain) in rule.Gains)
            {
                var before = personality[trait];
                var update = TraitEvolution.Apply(
                    before,
                    gain * experience.Valence,
                    experience.Importance,
                    experience.OccurredAt,
                    options.Personality,
                    options.Learning);
                if (update.Delta == 0)
                {
                    continue;
                }

                personality = personality.With(update.Trait);
                changes.Add(new TraitChange(
                    Guid.CreateVersion7(experience.OccurredAt),
                    experience.OccurredAt,
                    trait,
                    before.Value,
                    update.Trait.Value,
                    update.Delta,
                    experience.Id,
                    rule.Name));
            }
        }

        return (personality, changes);
    }
}
