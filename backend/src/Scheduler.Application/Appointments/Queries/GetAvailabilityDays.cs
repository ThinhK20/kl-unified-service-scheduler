using FluentValidation;
using Scheduler.Application.Abstractions;
using Scheduler.Application.Allocation;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;

namespace Scheduler.Application.Appointments.Queries;

/// <summary>
/// Per-day availability for dealership-local dates <paramref name="From"/> to <paramref name="To"/> (inclusive,
/// "YYYY-MM-DD"). <paramref name="VehicleId"/> is the raw optional query value: null = ignore the car (SPEC section 6).
/// </summary>
public sealed record GetAvailabilityDaysQuery(int DealershipId, int ServiceTypeId, string? From, string? To, string? VehicleId = null);

/// <summary>Checks the days query shape (SPEC section 6).</summary>
public sealed class GetAvailabilityDaysValidator : AbstractValidator<GetAvailabilityDaysQuery>
{
    /// <summary>Longest range, in days (to - from + 1). Six calendar weeks: one month view.</summary>
    public const int MaxDays = 42;

    public GetAvailabilityDaysValidator()
    {
        RuleFor(x => x.DealershipId).GreaterThan(0).WithMessage("dealershipId is required.").OverridePropertyName("dealershipId");
        RuleFor(x => x.ServiceTypeId).GreaterThan(0).WithMessage("serviceTypeId is required.").OverridePropertyName("serviceTypeId");
        RuleFor(x => x.From).LocalDate("from");
        RuleFor(x => x.To)
            .LocalDate("to")
            // Checked only when both dates are valid; a bad "from" is reported on its own field.
            .Must((query, _) => !TryParseRange(query, out var from, out var to) || to >= from)
            .WithMessage("to must be on or after from.")
            .Must((query, _) => !TryParseRange(query, out var from, out var to) || to.DayNumber - from.DayNumber + 1 <= MaxDays)
            .WithMessage($"The range can be at most {MaxDays} days.");
        RuleFor(x => x.VehicleId).OptionalVehicleId();
    }

    private static bool TryParseRange(GetAvailabilityDaysQuery query, out DateOnly from, out DateOnly to)
    {
        to = default;
        return IsoDateTime.TryParseDate(query.From, out from)
            && SupportedDates.Contains(from)
            && IsoDateTime.TryParseDate(query.To, out to)
            && SupportedDates.Contains(to);
    }
}

/// <summary>
/// Returns one entry per local date with the number of Available start times by exactly the rules of the day
/// endpoint (SPEC section 6), so a calendar can disable past and fully booked days. Taken slots of the technicians and
/// bays, and of the vehicle, are each loaded with one query for the whole range; each day is computed in memory.
/// It reserves nothing.
/// </summary>
public sealed class GetAvailabilityDaysHandler
{
    private readonly IValidator<GetAvailabilityDaysQuery> _validator;
    private readonly IDealershipRepository _dealerships;
    private readonly IServiceTypeRepository _serviceTypes;
    private readonly IVehicleRepository _vehicles;
    private readonly IAppointmentRepository _appointments;
    private readonly AvailabilityService _availability;
    private readonly IClock _clock;

    public GetAvailabilityDaysHandler(
        IValidator<GetAvailabilityDaysQuery> validator,
        IDealershipRepository dealerships,
        IServiceTypeRepository serviceTypes,
        IVehicleRepository vehicles,
        IAppointmentRepository appointments,
        AvailabilityService availability,
        IClock clock)
    {
        _validator = validator;
        _dealerships = dealerships;
        _serviceTypes = serviceTypes;
        _vehicles = vehicles;
        _appointments = appointments;
        _availability = availability;
        _clock = clock;
    }

    public async Task<Result<AvailabilityDaysDto>> HandleAsync(GetAvailabilityDaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validation = await _validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationErrors.ToError(validation);
        }

        IsoDateTime.TryParseDate(query.From, out var from);
        IsoDateTime.TryParseDate(query.To, out var to);
        AvailabilityRequest.TryParseVehicleId(query.VehicleId, out var vehicleId);

        var target = await AvailabilityRequest.LoadAsync(
            _dealerships, _serviceTypes, _vehicles, query.DealershipId, query.ServiceTypeId, vehicleId, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var (dealership, serviceType) = target.Value;
        var nowUtc = _clock.UtcNow;
        var today = DateOnly.FromDateTime(dealership.ToLocal(nowUtc));

        // Days before today are Past without loading anything: every start on them is <= now.
        var availableCounts = new Dictionary<DateOnly, int>();
        var firstLoaded = from > today ? from : today;
        if (firstLoaded <= to)
        {
            var range = await _availability.LoadRangeAsync(dealership, serviceType, firstLoaded, to, cancellationToken);
            var vehicleSlots = vehicleId is { } id
                ? await _appointments.GetVehicleSlotStartsAsync(id, range.WindowUtc, cancellationToken)
                : null;

            foreach (var day in range.Days)
            {
                availableCounts[day.LocalDate] = day.Schedule
                    .ClassifyStartTimes(nowUtc, vehicleSlots)
                    .Count(time => time.Status == StartTimeStatus.Available);
            }
        }

        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(from.AddDays)
            .Select(date =>
            {
                var count = date < today ? 0 : availableCounts.GetValueOrDefault(date);
                var status = date < today
                    ? DayAvailabilityStatus.Past
                    : count == 0 ? DayAvailabilityStatus.FullyBooked : DayAvailabilityStatus.Available;
                return new AvailabilityDayDto(AvailabilityRequest.Format(date), count, status);
            })
            .ToList();

        return new AvailabilityDaysDto(
            dealership.Id,
            serviceType.Id,
            AvailabilityRequest.Format(from),
            AvailabilityRequest.Format(to),
            dealership.TimeZoneId,
            serviceType.DurationMinutes,
            days);
    }
}
