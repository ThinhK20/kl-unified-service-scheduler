using Scheduler.Application.Common;
using Scheduler.Domain.Entities;

namespace Scheduler.Application.Appointments.Dtos;

/// <summary>Maps appointments to DTOs by hand.</summary>
public static class AppointmentMapper
{
    /// <summary>Maps an appointment whose navigation properties are loaded.</summary>
    public static AppointmentDto ToDto(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        return ToDto(
            appointment,
            appointment.Vehicle ?? throw NotLoaded(nameof(Appointment.Vehicle)),
            appointment.Dealership ?? throw NotLoaded(nameof(Appointment.Dealership)),
            appointment.ServiceType ?? throw NotLoaded(nameof(Appointment.ServiceType)),
            appointment.Technician ?? throw NotLoaded(nameof(Appointment.Technician)),
            appointment.ServiceBay ?? throw NotLoaded(nameof(Appointment.ServiceBay)));
    }

    /// <summary>Maps an appointment with the related entities given separately (used right after booking).</summary>
    public static AppointmentDto ToDto(
        Appointment appointment,
        Vehicle vehicle,
        Dealership dealership,
        ServiceType serviceType,
        Technician technician,
        ServiceBay serviceBay)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        return new AppointmentDto(
            appointment.Id,
            appointment.Status.ToString(),
            UtcTime.Normalize(appointment.StartUtc),
            UtcTime.Normalize(appointment.EndUtc),
            UtcTime.Normalize(appointment.CreatedUtc),
            appointment.CustomerId,
            new AppointmentVehicleDto(vehicle.Id, vehicle.Make, vehicle.Model, vehicle.Year, vehicle.Vin),
            new AppointmentDealershipDto(dealership.Id, dealership.Name, dealership.TimeZoneId),
            // Duration comes from the booking-time snapshot, not the current service rules.
            new AppointmentServiceTypeDto(serviceType.Id, serviceType.Name, appointment.DurationSnapshot),
            new AppointmentTechnicianDto(technician.Id, technician.Name),
            new AppointmentServiceBayDto(serviceBay.Id, serviceBay.Name, serviceBay.BayType.ToString()));
    }

    private static InvalidOperationException NotLoaded(string navigation) =>
        new($"Appointment.{navigation} is not loaded. Use a repository method that loads details.");
}
