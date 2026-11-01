namespace Scheduler.Application.Appointments.Dtos;

/// <summary>An appointment as the API returns it (SPEC section 6). Times are UTC.</summary>
public sealed record AppointmentDto(
    int Id,
    string Status,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime CreatedUtc,
    int CustomerId,
    AppointmentVehicleDto Vehicle,
    AppointmentDealershipDto Dealership,
    AppointmentServiceTypeDto ServiceType,
    AppointmentTechnicianDto Technician,
    AppointmentServiceBayDto ServiceBay);

public sealed record AppointmentVehicleDto(int Id, string Make, string Model, int Year, string Vin);

public sealed record AppointmentDealershipDto(int Id, string Name, string TimeZoneId);

public sealed record AppointmentServiceTypeDto(int Id, string Name, int DurationMinutes);

public sealed record AppointmentTechnicianDto(int Id, string Name);

public sealed record AppointmentServiceBayDto(int Id, string Name, string BayType);
