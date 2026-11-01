using Scheduler.Domain.ValueObjects;

namespace Scheduler.Application.Common;

/// <summary>Converts a local calendar date to its UTC range [local midnight, next local midnight).</summary>
public static class LocalDay
{
    public static TimeRange ToUtcRange(DateOnly localDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var start = TimeZoneInfo.ConvertTimeToUtc(localDate.ToDateTime(TimeOnly.MinValue), timeZone);
        var end = TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone);
        return TimeRange.Create(start, end);
    }
}
