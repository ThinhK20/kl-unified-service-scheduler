using Scheduler.Application.Allocation;

namespace Scheduler.Application.Appointments.Dtos;

/// <summary>
/// "Who would serve this booking if it were made now" (SPEC section 6, assignment preview). Times are UTC.
/// Advisory only: booking stays the final authority.
/// </summary>
/// <param name="Technician">The expected technician when <paramref name="Status"/> is Assignable, otherwise null.</param>
/// <param name="ServiceBay">The expected bay when <paramref name="Status"/> is Assignable, otherwise null.</param>
/// <param name="Technicians">Every technician of the dealership (also inactive), in display order.</param>
/// <param name="ServiceBays">Every bay of the dealership (also inactive), in display order.</param>
public sealed record AssignmentPreviewDto(
    int DealershipId,
    int ServiceTypeId,
    string TimeZoneId,
    DateTime StartUtc,
    DateTime EndUtc,
    AssignmentPreviewStatus Status,
    AppointmentTechnicianDto? Technician,
    AppointmentServiceBayDto? ServiceBay,
    IReadOnlyList<PreviewTechnicianDto> Technicians,
    IReadOnlyList<PreviewServiceBayDto> ServiceBays);

/// <summary>One technician and its state for the previewed time.</summary>
public sealed record PreviewTechnicianDto(int Id, string Name, TechnicianState State);

/// <summary>One bay and its state for the previewed time.</summary>
public sealed record PreviewServiceBayDto(int Id, string Name, string BayType, ServiceBayState State);

/// <summary>Outcome of the preview; the first match wins, in booking's order (SPEC section 5).</summary>
public enum AssignmentPreviewStatus
{
    /// <summary>The start is not after now (booking: 400 "startTime must be in the future.").</summary>
    Past,

    /// <summary>The given vehicle already has a slot in the range (booking: 409 VEHICLE_ALREADY_BOOKED).</summary>
    VehicleBooked,

    /// <summary>No valid (technician, bay) pair is free for the whole range (booking: 409 NO_AVAILABILITY).</summary>
    NoAvailability,

    /// <summary>Booking now would first try <see cref="AssignmentPreviewDto.Technician"/> with <see cref="AssignmentPreviewDto.ServiceBay"/>.</summary>
    Assignable,
}
