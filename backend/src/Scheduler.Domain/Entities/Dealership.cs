using Scheduler.Domain.Common;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Domain.Entities;

/// <summary>
/// A service centre. Opening hours are local times in the dealership time zone (IANA id),
/// and are the same every day.
/// </summary>
public sealed class Dealership : Entity
{
    private TimeZoneInfo? _timeZone;

    private Dealership()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>IANA time zone id, for example "Asia/Ho_Chi_Minh".</summary>
    public string TimeZoneId { get; private set; } = string.Empty;

    /// <summary>Local opening time.</summary>
    public TimeOnly OpenTime { get; private set; }

    /// <summary>Local closing time. A booking may end exactly at this time.</summary>
    public TimeOnly CloseTime { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>The dealership time zone. Throws a <see cref="Errors.DomainException"/> if the id is unknown.</summary>
    public TimeZoneInfo TimeZone => _timeZone ??= TimeZones.Find(TimeZoneId);

    private DailyWindow OpeningHours => new(OpenTime, CloseTime);

    public static Dealership Create(string name, string timeZoneId, TimeOnly openTime, TimeOnly closeTime)
    {
        _ = new DailyWindow(openTime, closeTime);
        var id = Guard.NotBlank(timeZoneId, nameof(TimeZoneId));
        var dealership = new Dealership
        {
            Name = Guard.NotBlank(name, nameof(Name)),
            TimeZoneId = id,
            OpenTime = openTime,
            CloseTime = closeTime,
            IsActive = true,
        };
        _ = dealership.TimeZone; // fail early on an unknown time zone
        return dealership;
    }

    /// <summary>
    /// True when the whole UTC range is inside opening hours on the local day of its start.
    /// </summary>
    public bool IsOpenFor(TimeRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return OpeningHours.Covers(range, TimeZone);
    }

    /// <summary>Opening hours on a local calendar date, as a UTC range.</summary>
    public TimeRange OpeningHoursUtc(DateOnly localDate) => OpeningHours.ToUtcRange(localDate, TimeZone);

    /// <summary>Converts a UTC time to the dealership local time.</summary>
    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(Guard.Utc(utc), TimeZone);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
