using Scheduler.Application.Allocation;

namespace Scheduler.Application.Appointments.Dtos;

/// <summary>Start times for one service on one dealership-local date (SPEC section 6).</summary>
/// <param name="Slots">The bookable starts: exactly the <paramref name="Times"/> with status Available.</param>
/// <param name="Times">Every candidate start of the day, ascending, each with its status.</param>
public sealed record AvailabilityDto(
    int DealershipId,
    int ServiceTypeId,
    string Date,
    string TimeZoneId,
    int DurationMinutes,
    IReadOnlyList<TimeSlotDto> Slots,
    IReadOnlyList<StartTimeDto> Times);

/// <summary>One free start time and the matching end time, UTC.</summary>
public sealed record TimeSlotDto(DateTime StartUtc, DateTime EndUtc);

/// <summary>One candidate start time, the matching end time (UTC) and whether it can be booked.</summary>
public sealed record StartTimeDto(DateTime StartUtc, DateTime EndUtc, StartTimeStatus Status);

/// <summary>Per-day availability for a range of dealership-local dates (SPEC section 6, days endpoint).</summary>
/// <param name="From">First local date, "YYYY-MM-DD".</param>
/// <param name="To">Last local date (inclusive), "YYYY-MM-DD".</param>
/// <param name="Days">One entry per date from <paramref name="From"/> to <paramref name="To"/>, ascending.</param>
public sealed record AvailabilityDaysDto(
    int DealershipId,
    int ServiceTypeId,
    string From,
    string To,
    string TimeZoneId,
    int DurationMinutes,
    IReadOnlyList<AvailabilityDayDto> Days);

/// <summary>One local date: how many start times are Available (same rules as the day endpoint) and a summary.</summary>
public sealed record AvailabilityDayDto(string Date, int AvailableCount, DayAvailabilityStatus Status);

/// <summary>Summary of one local date (SPEC section 6, days endpoint).</summary>
public enum DayAvailabilityStatus
{
    /// <summary>The date is before today in the dealership time zone (AvailableCount is 0).</summary>
    Past,

    /// <summary>Today or later, but no start time is Available (includes today when every remaining time has passed).</summary>
    FullyBooked,

    /// <summary>At least one start time is Available.</summary>
    Available,
}
