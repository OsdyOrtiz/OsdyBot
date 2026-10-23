namespace Osdy.Core.Identity;

/// <summary>Who this robot is. Created once and never regenerated.</summary>
public sealed record RobotIdentity(Guid Id, string Name, DateTimeOffset CreatedAt)
{
    public static RobotIdentity Create(string name, DateTimeOffset now) => new(Guid.CreateVersion7(now), name, now);
}
