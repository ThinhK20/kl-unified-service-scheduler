using Scheduler.Application.Abstractions;
using Scheduler.Application.Common;
using Scheduler.Domain.Entities;

namespace Scheduler.Application.Allocation;

/// <summary>
/// Loads everything needed to answer "who is free when" for one service on one local day.
/// The booking handler (candidates, suggestions) and the availability query both use it,
/// so the rules exist once.
/// </summary>
public sealed class AvailabilityService
{
    private readonly ITechnicianRepository _technicians;
    private readonly IServiceBayRepository _serviceBays;
    private readonly IAppointmentRepository _appointments;
    private readonly IServiceTypeRepository _serviceTypes;
    private readonly IResourceAllocationStrategy _strategy;

    public AvailabilityService(
        ITechnicianRepository technicians,
        IServiceBayRepository serviceBays,
        IAppointmentRepository appointments,
        IServiceTypeRepository serviceTypes,
        IResourceAllocationStrategy strategy)
    {
        _technicians = technicians;
        _serviceBays = serviceBays;
        _appointments = appointments;
        _serviceTypes = serviceTypes;
        _strategy = strategy;
    }

    /// <summary>
    /// Loads the day: active qualified technicians and active matching bays of the dealership, their taken slots
    /// during opening hours, and confirmed appointment counts for the local day. Also all active technicians and
    /// active service types of the dealership, which the strategy may use to protect scarce skills. Nothing is reserved.
    /// </summary>
    public async Task<DaySchedule> LoadDayAsync(
        Dealership dealership,
        ServiceType serviceType,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dealership);
        ArgumentNullException.ThrowIfNull(serviceType);

        var openingHours = dealership.OpeningHoursUtc(localDate);

        var activeTechnicians = (await _technicians.GetByDealershipWithSkillsAsync(dealership.Id, cancellationToken))
            .Where(t => t.IsActive && t.DealershipId == dealership.Id)
            .ToList();
        var technicians = activeTechnicians
            .Where(t => t.IsQualifiedFor(serviceType.RequiredSkill, serviceType.MinimumTier))
            .ToList();

        var bays = (await _serviceBays.GetByDealershipAsync(dealership.Id, cancellationToken))
            .Where(b => b.CanHost(serviceType, dealership.Id))
            .ToList();

        if (technicians.Count == 0 || bays.Count == 0)
        {
            return new DaySchedule(dealership, serviceType, openingHours, [], [], [], new Dictionary<int, int>(), [], [], _strategy);
        }

        var activeServiceTypes = await _serviceTypes.GetActiveAsync(dealership.Id, cancellationToken);

        var technicianIds = technicians.Select(t => t.Id).ToList();
        var bayIds = bays.Select(b => b.Id).ToList();

        var taken = await _appointments.GetTakenSlotsAsync(technicianIds, bayIds, openingHours, cancellationToken);
        var counts = await _appointments.CountConfirmedByTechnicianAsync(
            technicianIds,
            LocalDay.ToUtcRange(localDate, dealership.TimeZone),
            cancellationToken);

        return new DaySchedule(
            dealership, serviceType, openingHours, technicians, bays, taken, counts, activeTechnicians, activeServiceTypes, _strategy);
    }
}
