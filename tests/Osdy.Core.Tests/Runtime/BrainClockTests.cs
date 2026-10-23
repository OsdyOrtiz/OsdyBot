using Osdy.Core.Decisions;
using Osdy.Core.Events;
using Osdy.Core.States;
using static Osdy.Core.Tests.TestBrains;

namespace Osdy.Core.Tests.Runtime;

public class BrainClockTests
{
    [Fact]
    public void Inputs_stamped_before_the_last_step_are_handled_at_the_last_step_time()
    {
        var brain = New();
        brain.Tick(S(10));

        var engage = brain.Handle(EventType.HumanInteractionDetected, S(9));
        var praise = brain.Handle(EventType.PositiveInteraction, S(9.5));
        var tick = brain.Tick(S(5));

        Assert.Equal(T0 + S(9), engage.Event!.OccurredAt); // the observed fact keeps its own time
        Assert.Equal(T0 + S(10), engage.At);
        Assert.Equal(T0 + S(10), engage.Decision!.OccurredAt);
        Assert.Equal(BehaviorAction.ObserveAndEngage, engage.Decision.Action);
        Assert.Equal(T0 + S(9.5), praise.Event!.OccurredAt);
        Assert.Equal(T0 + S(10), praise.Decision!.OccurredAt);
        Assert.Equal(T0 + S(10), praise.Experience!.OccurredAt);
        Assert.NotEmpty(praise.TraitChanges);
        Assert.All(praise.TraitChanges, c => Assert.Equal(T0 + S(10), c.OccurredAt));
        Assert.Equal(T0 + S(10), tick.At);
        Assert.Equal(BrainState.Interacting, brain.Runtime.State);
        Assert.Equal(T0 + S(10), brain.Runtime.StateEnteredAt);
        Assert.Equal(T0 + S(10), brain.Runtime.LastInteractionAt);
        Assert.Equal(T0 + S(10), brain.Runtime.LastUpdatedAt);
    }
}
