using Scheduler.Application.Abstractions;

namespace Scheduler.Application.Tests.TestSupport;

/// <summary>A clock the test controls.</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; set; }
}
