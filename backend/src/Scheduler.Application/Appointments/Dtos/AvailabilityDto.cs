namespace Scheduler.Application.Appointments.Dtos;

/// <summary>Free start times for one service on one dealership-local date (SPEC section 6).</summary>
public sealed record AvailabilityDto(
    int DealershipId,
    int ServiceTypeId,
    string Date,
    string TimeZoneId,
    int DurationMinutes,
    IReadOnlyList<TimeSlotDto> Slots);

/// <summary>One free start time and the matching end time, UTC.</summary>
public sealed record TimeSlotDto(DateTime StartUtc, DateTime EndUtc);
