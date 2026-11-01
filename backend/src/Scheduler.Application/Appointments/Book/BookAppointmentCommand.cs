namespace Scheduler.Application.Appointments.Book;

/// <summary>
/// Book a service. <see cref="StartTime"/> is the raw ISO-8601 string from the request and must have an offset.
/// <see cref="IdempotencyKey"/> is the Idempotency-Key header value; the API passes it through as is
/// (null when missing) and the validator rejects missing or too long keys.
/// </summary>
public sealed record BookAppointmentCommand(
    string? IdempotencyKey,
    int CustomerId,
    int VehicleId,
    int DealershipId,
    int ServiceTypeId,
    string? StartTime);
