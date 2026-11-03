using Scheduler.Application.Abstractions;
using Scheduler.Application.Common;
using Scheduler.Domain.Entities;
using Scheduler.Domain.ValueObjects;

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
    /// active service types of the dealership, which the strategy may use to protect scarce skills. Every technician and
    /// bay of the dealership (also inactive ones) is kept for <see cref="DaySchedule.TechnicianStates"/> and
    /// <see cref="DaySchedule.ServiceBayStates"/>. Nothing is reserved.
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

        var allTechnicians = (await _technicians.GetByDealershipWithSkillsAsync(dealership.Id, cancellationToken))
            .Where(t => t.DealershipId == dealership.Id)
            .ToList();
        var activeTechnicians = allTechnicians.Where(t => t.IsActive).ToList();
        var technicians = activeTechnicians
            .Where(t => t.IsQualifiedFor(serviceType.RequiredSkill, serviceType.MinimumTier))
            .ToList();

        var allBays = (await _serviceBays.GetByDealershipAsync(dealership.Id, cancellationToken))
            .Where(b => b.DealershipId == dealership.Id)
            .ToList();
        var bays = allBays
            .Where(b => b.CanHost(serviceType, dealership.Id))
            .ToList();

        if (technicians.Count == 0 && bays.Count == 0)
        {
            return new DaySchedule(
                dealership, serviceType, openingHours, [], [], [], new Dictionary<int, int>(), [], [], _strategy, allTechnicians, allBays);
        }

        // With only one kind present FindPairs still finds nothing, but the taken slots are loaded so the assignment
        // preview can tell Busy from Free for the resources that do exist (SPEC section 6).
        var activeServiceTypes = await _serviceTypes.GetActiveAsync(dealership.Id, cancellationToken);

        var technicianIds = technicians.Select(t => t.Id).ToList();
        var bayIds = bays.Select(b => b.Id).ToList();

        var taken = await _appointments.GetTakenSlotsAsync(technicianIds, bayIds, openingHours, cancellationToken);
        var counts = await _appointments.CountConfirmedByTechnicianAsync(
            technicianIds,
            LocalDay.ToUtcRange(localDate, dealership.TimeZone),
            cancellationToken);

        return new DaySchedule(
            dealership, serviceType, openingHours, technicians, bays, taken, counts, activeTechnicians, activeServiceTypes, _strategy,
            allTechnicians, allBays);
    }

    /// <summary>
    /// Loads every local date from <paramref name="fromLocalDate"/> to <paramref name="toLocalDate"/> (inclusive) for
    /// the days endpoint (SPEC section 6). Same resources and rules as <see cref="LoadDayAsync"/>, but the taken slots
    /// of the whole range come from ONE query, and each day is then built in memory (no per-day round trips).
    /// <para>
    /// Confirmed appointment counts are not loaded: they only change the ORDER of pairs, never whether a pair exists
    /// (the strategy contract returns every pair), so these schedules answer availability exactly but must not be
    /// used to pick a pair for a booking. Booking keeps using <see cref="LoadDayAsync"/>.
    /// </para>
    /// </summary>
    public async Task<ScheduleRange> LoadRangeAsync(
        Dealership dealership,
        ServiceType serviceType,
        DateOnly fromLocalDate,
        DateOnly toLocalDate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dealership);
        ArgumentNullException.ThrowIfNull(serviceType);
        if (toLocalDate < fromLocalDate)
        {
            throw new ArgumentOutOfRangeException(nameof(toLocalDate), "toLocalDate must be on or after fromLocalDate.");
        }

        var dates = Enumerable.Range(0, toLocalDate.DayNumber - fromLocalDate.DayNumber + 1)
            .Select(fromLocalDate.AddDays)
            .ToList();
        var openingHours = dates.Select(dealership.OpeningHoursUtc).ToList();
        var windowUtc = TimeRange.Create(openingHours[0].StartUtc, openingHours[^1].EndUtc);

        var activeTechnicians = (await _technicians.GetByDealershipWithSkillsAsync(dealership.Id, cancellationToken))
            .Where(t => t.IsActive && t.DealershipId == dealership.Id)
            .ToList();
        var technicians = activeTechnicians
            .Where(t => t.IsQualifiedFor(serviceType.RequiredSkill, serviceType.MinimumTier))
            .ToList();

        var bays = (await _serviceBays.GetByDealershipAsync(dealership.Id, cancellationToken))
            .Where(b => b.CanHost(serviceType, dealership.Id))
            .ToList();

        var noCounts = new Dictionary<int, int>();
        if (technicians.Count == 0 || bays.Count == 0)
        {
            return new ScheduleRange(
                windowUtc,
                dates.Select((date, i) => new ScheduledDay(
                    date,
                    new DaySchedule(dealership, serviceType, openingHours[i], [], [], [], noCounts, [], [], _strategy)))
                .ToList());
        }

        var activeServiceTypes = await _serviceTypes.GetActiveAsync(dealership.Id, cancellationToken);
        var taken = await _appointments.GetTakenSlotsAsync(
            technicians.Select(t => t.Id).ToList(),
            bays.Select(b => b.Id).ToList(),
            windowUtc,
            cancellationToken);

        var days = dates.Select((date, i) =>
        {
            var hours = openingHours[i];
            var takenThatDay = taken
                .Where(slot => UtcTime.Normalize(slot.SlotStartUtc) is var start && start >= hours.StartUtc && start < hours.EndUtc)
                .ToList();
            return new ScheduledDay(
                date,
                new DaySchedule(
                    dealership, serviceType, hours, technicians, bays, takenThatDay, noCounts, activeTechnicians, activeServiceTypes, _strategy));
        }).ToList();

        return new ScheduleRange(windowUtc, days);
    }
}

/// <summary>Day schedules for consecutive local dates (see <see cref="AvailabilityService.LoadRangeAsync"/>).</summary>
/// <param name="WindowUtc">From the first day's opening time to the last day's closing time, UTC.</param>
/// <param name="Days">One entry per local date, ascending.</param>
public sealed record ScheduleRange(TimeRange WindowUtc, IReadOnlyList<ScheduledDay> Days);

/// <summary>One local date and its schedule.</summary>
public sealed record ScheduledDay(DateOnly LocalDate, DaySchedule Schedule);
