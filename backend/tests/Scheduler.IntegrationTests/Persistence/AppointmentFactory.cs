using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Entities;
using Scheduler.Infrastructure.Persistence;
using Scheduler.IntegrationTests.Infrastructure;

namespace Scheduler.IntegrationTests.Persistence;

/// <summary>Loads seeded entities and builds an appointment with the domain factory.</summary>
internal static class AppointmentFactory
{
    /// <summary>Ho Chi Minh City is UTC+7 all year (no daylight saving).</summary>
    public static DateTime LocalToUtc(DateOnly date, int hour, int minute = 0) =>
        new DateTime(date, new TimeOnly(hour, minute), DateTimeKind.Utc).AddHours(-7);

    public static async Task<Appointment> CreateAsync(
        SchedulerDbContext db,
        int serviceTypeId,
        int technicianId,
        int bayId,
        DateTime startUtc,
        int vehicleId = 1,
        int customerId = 1)
    {
        var serviceType = await db.ServiceTypes.SingleAsync(s => s.Id == serviceTypeId);
        var dealership = await db.Dealerships.SingleAsync(d => d.Id == serviceType.DealershipId);
        var vehicle = await db.Vehicles.SingleAsync(v => v.Id == vehicleId);
        var technician = await db.Technicians.Include(t => t.Skills).SingleAsync(t => t.Id == technicianId);
        var bay = await db.ServiceBays.SingleAsync(b => b.Id == bayId);

        return Appointment.Create(customerId, vehicle, dealership, serviceType, technician, bay, startUtc, FixedClock.DefaultNow);
    }
}
