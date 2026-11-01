using Scheduler.Application.Abstractions;

namespace Scheduler.Infrastructure.Time;

/// <summary>The real clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
