namespace Osdy.Core.Runtime;

public sealed record BootResult(Brain Brain, bool Created, StepResult Startup);

public static class BrainBootstrap
{
    /// <summary>
    /// Loads the robot from the store (or creates it on first run), resumes it, and commits the startup step so a
    /// new identity is persisted immediately.
    /// </summary>
    public static BootResult Boot(IBrainStore store, BrainOptions options, DateTimeOffset now)
    {
        var snapshot = store.Load();
        var brain = snapshot is null ? Brain.CreateNew(options, now) : Brain.Restore(snapshot, options);
        var startup = brain.Start(now);
        store.Commit(startup);
        return new BootResult(brain, snapshot is null, startup);
    }
}
