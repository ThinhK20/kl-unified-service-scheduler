using Scheduler.Application.Abstractions;

namespace Scheduler.IntegrationTests.Infrastructure;

/// <summary>A clock that always returns the same time. Test bookings are in 2030, after <see cref="DefaultNow"/>.</summary>
public sealed class FixedClock(DateTime utcNow) : IClock
{
    public static readonly DateTime DefaultNow = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public FixedClock()
        : this(DefaultNow)
    {
    }

    public DateTime UtcNow { get; set; } = utcNow;
}
