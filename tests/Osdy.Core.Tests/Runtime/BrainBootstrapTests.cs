using Osdy.Core.Decisions;
using Osdy.Core.Events;
using Osdy.Core.Runtime;
using Osdy.Core.States;
using static Osdy.Core.Tests.TestBrains;

namespace Osdy.Core.Tests.Runtime;

public class BrainBootstrapTests
{
    [Fact]
    public void First_boot_creates_and_persists_a_new_identity()
    {
        var store = new InMemoryBrainStore();

        var boot = BrainBootstrap.Boot(store, new BrainOptions(), T0);

        Assert.True(boot.Created);
        Assert.Equal("Osdy", boot.Brain.Identity.Name);
        Assert.Equal(T0, boot.Brain.Identity.CreatedAt);
        Assert.Equal(boot.Brain.Identity, store.Load()!.Identity);
        Assert.Null(boot.Startup.Decision);
    }

    [Fact]
    public void Later_boots_load_the_same_identity_personality_and_recover_transient_state()
    {
        var store = new InMemoryBrainStore();
        var first = BrainBootstrap.Boot(store, new BrainOptions(), T0).Brain;
        store.Commit(first.Handle(EventType.HumanInteractionDetected, S(5)));
        store.Commit(first.Handle(EventType.PositiveInteraction, S(10)));

        var second = BrainBootstrap.Boot(store, new BrainOptions(), T0 + TimeSpan.FromMinutes(10));

        Assert.False(second.Created);
        Assert.Equal(first.Identity, second.Brain.Identity);
        Assert.Equal(first.Personality.Traits, second.Brain.Personality.Traits);
        Assert.Equal(BehaviorAction.RecoverAfterRestart, second.Startup.Decision!.Action);
        Assert.Equal(BrainState.Idle, store.Load()!.Runtime.State);
        Assert.Equal(new MemoryStats(Events: 2, Decisions: 3, Experiences: 1, TraitChanges: 2), store.GetStats());
    }
}
