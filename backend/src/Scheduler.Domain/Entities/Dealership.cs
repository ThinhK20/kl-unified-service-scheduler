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

    /// <summary>
    /// The dealership time zone. A stored id that is unknown is broken server data, not a client mistake,
    /// so it throws <see cref="InvalidOperationException"/> (the API answers 500).
    /// </summary>
    public TimeZoneInfo TimeZone => _timeZone ??= TimeZones.TryFind(TimeZoneId, out var timeZone)
        ? timeZone
        : throw new InvalidOperationException($"Dealership {Id} has an unknown time zone id '{TimeZoneId}'. Fix the stored data.");

    private DailyWindow OpeningHours => new(OpenTime, CloseTime);

    public static Dealership Create(string name, string timeZoneId, TimeOnly openTime, TimeOnly closeTime)
    {
        _ = new DailyWindow(openTime, closeTime);
        var id = Guard.NotBlank(timeZoneId, nameof(TimeZoneId));
        _ = TimeZones.Find(id); // input check: an unknown id is the caller's mistake
        var dealership = new Dealership
        {
            Name = Guard.NotBlank(name, nameof(Name)),
            TimeZoneId = id,
            OpenTime = openTime,
            CloseTime = closeTime,
            IsActive = true,
        };
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
