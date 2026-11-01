using Scheduler.Domain.Errors;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Domain.Common;

/// <summary>
/// A daily window in local time (for example opening hours 08:00-17:00 or a shift).
/// It checks a UTC range against the window on the range's local day.
/// </summary>
internal readonly record struct DailyWindow
{
    public DailyWindow(TimeOnly start, TimeOnly end)
    {
        if (end <= start)
        {
            throw new DomainException(DomainErrors.Time.WindowEndNotAfterStart);
        }

        Start = start;
        End = end;
    }

    public TimeOnly Start { get; }

    public TimeOnly End { get; }

    /// <summary>
    /// True when the whole range is inside the window on the local day of the range start.
    /// The end is exclusive, so a range may end exactly at the window end.
    /// </summary>
    public bool Covers(TimeRange range, TimeZoneInfo timeZone)
    {
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(range.StartUtc, timeZone);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(range.EndUtc, timeZone);
        var day = localStart.Date;

        return localStart >= day + Start.ToTimeSpan()
            && localEnd <= day + End.ToTimeSpan();
    }

    /// <summary>The window on the given local date, as a UTC range.</summary>
    public TimeRange ToUtcRange(DateOnly localDate, TimeZoneInfo timeZone)
    {
        var localStart = localDate.ToDateTime(Start, DateTimeKind.Unspecified);
        var localEnd = localDate.ToDateTime(End, DateTimeKind.Unspecified);
        return TimeRange.Create(
            TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone));
    }
}
