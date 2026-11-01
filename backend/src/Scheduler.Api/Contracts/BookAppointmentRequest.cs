namespace Scheduler.Api.Contracts;

/// <summary>Body of POST /api/appointments.</summary>
/// <param name="CustomerId">The customer who books (the demo customer is 1).</param>
/// <param name="VehicleId">One of the customer's vehicles.</param>
/// <param name="DealershipId">Where the service is done.</param>
/// <param name="ServiceTypeId">Which service. It must be offered at the dealership.</param>
/// <param name="StartTime">
/// ISO-8601 date and time WITH an offset, on the 15-minute grid, for example "2026-11-10T10:30:00+07:00".
/// Read as a plain string so a value without an offset is rejected instead of being read as server local time.
/// </param>
public sealed record BookAppointmentRequest(
    int CustomerId,
    int VehicleId,
    int DealershipId,
    int ServiceTypeId,
    string? StartTime);
