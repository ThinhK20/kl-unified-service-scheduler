using Scheduler.Application.Abstractions;
using Scheduler.Application.Common;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Application.Allocation;

/// <summary>
/// An in-memory snapshot of one local day for one service: candidate resources and taken slots.
/// It may be stale; the unique slot index in the database is the final judge.
/// </summary>
public sealed class DaySchedule
{
    private readonly Dealership _dealership;
    private readonly ServiceType _serviceType;
    private readonly IReadOnlyList<Technician> _technicians;
    private readonly IReadOnlyList<ServiceBay> _serviceBays;
    private readonly HashSet<(ResourceType Type, int Id, DateTime SlotStartUtc)> _taken;
    private readonly IReadOnlyDictionary<int, int> _confirmedCounts;
    private readonly IReadOnlyList<Technician> _dealershipTechnicians;
    private readonly IReadOnlyList<ServiceType> _activeServiceTypes;
    private readonly IResourceAllocationStrategy _strategy;

    internal DaySchedule(
        Dealership dealership,
        ServiceType serviceType,
        TimeRange openingHoursUtc,
        IReadOnlyList<Technician> technicians,
        IReadOnlyList<ServiceBay> serviceBays,
        IReadOnlyList<TakenSlot> takenSlots,
        IReadOnlyDictionary<int, int> confirmedCounts,
        IReadOnlyList<Technician> dealershipTechnicians,
        IReadOnlyList<ServiceType> activeServiceTypes,
        IResourceAllocationStrategy strategy)
    {
        _dealership = dealership;
        _serviceType = serviceType;
        OpeningHoursUtc = openingHoursUtc;
        _technicians = technicians;
        _serviceBays = serviceBays;
        _taken = takenSlots
            .Select(s => (s.ResourceType, s.ResourceId, UtcTime.Normalize(s.SlotStartUtc)))
            .ToHashSet();
        _confirmedCounts = confirmedCounts;
        _dealershipTechnicians = dealershipTechnicians;
        _activeServiceTypes = activeServiceTypes;
        _strategy = strategy;
    }

    /// <summary>Opening hours of this day, UTC.</summary>
    public TimeRange OpeningHoursUtc { get; }

    /// <summary>
    /// Free (technician, bay) pairs for the whole range, in strategy order. Empty when the range is not
    /// inside this day's opening hours or nobody is free.
    /// </summary>
    public IReadOnlyList<ResourcePair> FindPairs(TimeRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!OpeningHoursUtc.Contains(range) || !SlotGrid.IsOnGrid(range.StartUtc) || !SlotGrid.IsOnGrid(range.EndUtc))
        {
            return [];
        }

        var slots = SlotGrid.Split(range);
        var technicians = _technicians
            .Where(t => t.ShiftCovers(range, _dealership.TimeZone) && IsFree(ResourceType.Technician, t.Id, slots))
            .ToList();
        if (technicians.Count == 0)
        {
            return [];
        }

        var bays = _serviceBays.Where(b => IsFree(ResourceType.Bay, b.Id, slots)).ToList();
        if (bays.Count == 0)
        {
            return [];
        }

        return _strategy.Order(new AllocationRequest(
            _serviceType, technicians, bays, _confirmedCounts, _dealershipTechnicians, _activeServiceTypes));
    }

    /// <summary>
    /// Every grid start time in opening hours, after <paramref name="afterUtc"/>, where at least one pair is free
    /// for the full duration. Ascending.
    /// </summary>
    public IReadOnlyList<TimeRange> FreeRanges(DateTime afterUtc) =>
        ClassifyStartTimes(afterUtc)
            .Where(time => time.Status == StartTimeStatus.Available)
            .Select(time => time.Range)
            .ToList();

    /// <summary>
    /// Every grid start time in opening hours where the whole job fits before closing (the same candidates as
    /// <see cref="FreeRanges"/>), ascending, each with its status (SPEC section 6). First match wins:
    /// <see cref="StartTimeStatus.Past"/> (start &lt;= <paramref name="afterUtc"/>),
    /// <see cref="StartTimeStatus.VehicleBooked"/> (only when <paramref name="vehicleSlotStartsUtc"/> is given, see
    /// <see cref="IsVehicleBusy"/>), <see cref="StartTimeStatus.Booked"/> (<see cref="FindPairs"/> is empty),
    /// else <see cref="StartTimeStatus.Available"/>. These are the checks booking makes, in booking's order
    /// (SPEC section 5), so the screen and the booking endpoint agree on the same snapshot.
    /// </summary>
    /// <param name="afterUtc">Usually "now": starts at or before it are past.</param>
    /// <param name="vehicleSlotStartsUtc">The car's slot starts (any dealership), or null to ignore the car.</param>
    public IReadOnlyList<ClassifiedStartTime> ClassifyStartTimes(
        DateTime afterUtc,
        IReadOnlyCollection<DateTime>? vehicleSlotStartsUtc = null)
    {
        var after = UtcTime.Normalize(afterUtc);
        var vehicleSlots = vehicleSlotStartsUtc?.Select(UtcTime.Normalize).ToList();
        return CandidateRanges()
            .Select(range => new ClassifiedStartTime(range, Classify(range, after, vehicleSlots)))
            .ToList();
    }

    /// <summary>
    /// In-memory twin of <see cref="IAppointmentRepository.IsVehicleBookedAsync"/> (the check booking makes):
    /// true when any of the vehicle's slot starts is in the half-open <paramref name="range"/> (&gt;= start and &lt; end).
    /// </summary>
    public static bool IsVehicleBusy(IEnumerable<DateTime> vehicleSlotStartsUtc, TimeRange range)
    {
        ArgumentNullException.ThrowIfNull(vehicleSlotStartsUtc);
        ArgumentNullException.ThrowIfNull(range);
        return vehicleSlotStartsUtc.Any(slot => range.Contains(UtcTime.Normalize(slot)));
    }

    /// <summary>
    /// Up to <paramref name="max"/> free start times nearest to <paramref name="requestedStartUtc"/> (an earlier time
    /// wins a tie), excluding the requested time itself, returned in ascending order.
    /// Ranges that touch any of <paramref name="busySlotStartsUtc"/> (for example the customer's car is booked
    /// elsewhere) are skipped.
    /// </summary>
    public IReadOnlyList<DateTime> SuggestStartTimes(
        DateTime requestedStartUtc,
        DateTime afterUtc,
        int max,
        IReadOnlyCollection<DateTime>? busySlotStartsUtc = null)
    {
        var requested = UtcTime.Normalize(requestedStartUtc);
        var busy = (busySlotStartsUtc ?? []).Select(UtcTime.Normalize).ToHashSet();
        return FreeRanges(afterUtc)
            .Where(range => busy.Count == 0 || !SlotGrid.Split(range).Any(busy.Contains))
            .Select(range => range.StartUtc)
            .Where(start => start != requested)
            .OrderBy(start => (start - requested).Duration())
            .ThenBy(start => start)
            .Take(max)
            .Order()
            .ToList();
    }

    private IEnumerable<TimeRange> CandidateRanges()
    {
        var duration = _serviceType.Duration;
        var slotTicks = SlotGrid.SlotLength.Ticks;
        var firstTicks = (OpeningHoursUtc.StartUtc.Ticks + slotTicks - 1) / slotTicks * slotTicks;
        for (var start = new DateTime(firstTicks, DateTimeKind.Utc);
             start + duration <= OpeningHoursUtc.EndUtc;
             start += SlotGrid.SlotLength)
        {
            yield return TimeRange.FromStart(start, duration);
        }
    }

    private StartTimeStatus Classify(TimeRange range, DateTime afterUtc, IReadOnlyList<DateTime>? vehicleSlotStartsUtc)
    {
        // SPEC section 6, same order as booking (SPEC section 5): future start, then the car, then a free pair.
        if (range.StartUtc <= afterUtc)
        {
            return StartTimeStatus.Past;
        }

        if (vehicleSlotStartsUtc is not null && IsVehicleBusy(vehicleSlotStartsUtc, range))
        {
            return StartTimeStatus.VehicleBooked;
        }

        return FindPairs(range).Count > 0 ? StartTimeStatus.Available : StartTimeStatus.Booked;
    }

    private bool IsFree(ResourceType type, int id, IReadOnlyList<DateTime> slots) =>
        slots.All(slot => !_taken.Contains((type, id, slot)));
}
