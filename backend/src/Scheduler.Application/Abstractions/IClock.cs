namespace Scheduler.Application.Abstractions;

/// <summary>Gives the current time. Swap in a fake clock in tests.</summary>
public interface IClock
{
    /// <summary>Current time in UTC (<see cref="DateTimeKind.Utc"/>).</summary>
    DateTime UtcNow { get; }
}
