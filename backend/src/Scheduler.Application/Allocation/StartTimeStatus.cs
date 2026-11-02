using Scheduler.Domain.ValueObjects;

namespace Scheduler.Application.Allocation;

/// <summary>
/// Why a candidate start time can or cannot be booked (SPEC section 6, availability <c>times</c>).
/// The first matching rule wins, in this order.
/// </summary>
public enum StartTimeStatus
{
    /// <summary>The start is not after now (booking needs startUtc &gt; now).</summary>
    Past,

    /// <summary>The given vehicle already has a slot in [start, start + duration) (booking: VEHICLE_ALREADY_BOOKED).</summary>
    VehicleBooked,

    /// <summary>No valid (technician, bay) pair is free for the whole range (booking: NO_AVAILABILITY).</summary>
    Booked,

    /// <summary>Bookable now (the booking endpoint stays the final authority).</summary>
    Available,
}

/// <summary>One candidate start time of a day (UTC range) and its <see cref="StartTimeStatus"/>.</summary>
public sealed record ClassifiedStartTime(TimeRange Range, StartTimeStatus Status);
