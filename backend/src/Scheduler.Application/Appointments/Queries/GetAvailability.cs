using FluentValidation;
using Scheduler.Application.Abstractions;
using Scheduler.Application.Allocation;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;

namespace Scheduler.Application.Appointments.Queries;

/// <summary>
/// Start times for a service on a dealership-local date ("YYYY-MM-DD"). <paramref name="VehicleId"/> is the raw
/// optional query value: null = ignore the car (SPEC section 6).
/// </summary>
public sealed record GetAvailabilityQuery(int DealershipId, int ServiceTypeId, string? Date, string? VehicleId = null);

/// <summary>Checks the availability query shape.</summary>
public sealed class GetAvailabilityValidator : AbstractValidator<GetAvailabilityQuery>
{
    public GetAvailabilityValidator()
    {
        RuleFor(x => x.DealershipId).GreaterThan(0).WithMessage("dealershipId is required.").OverridePropertyName("dealershipId");
        RuleFor(x => x.ServiceTypeId).GreaterThan(0).WithMessage("serviceTypeId is required.").OverridePropertyName("serviceTypeId");
        RuleFor(x => x.Date).LocalDate("date");
        RuleFor(x => x.VehicleId).OptionalVehicleId();
    }
}

/// <summary>
/// Returns every candidate grid start time of that local day with its status (SPEC section 6): Past, VehicleBooked
/// (only when a vehicle is given), Booked, Available. <c>slots</c> are exactly the Available ones. It reserves nothing.
/// </summary>
public sealed class GetAvailabilityHandler
{
    private readonly IValidator<GetAvailabilityQuery> _validator;
    private readonly IDealershipRepository _dealerships;
    private readonly IServiceTypeRepository _serviceTypes;
    private readonly IVehicleRepository _vehicles;
    private readonly IAppointmentRepository _appointments;
    private readonly AvailabilityService _availability;
    private readonly IClock _clock;

    public GetAvailabilityHandler(
        IValidator<GetAvailabilityQuery> validator,
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

    public async Task<Result<AvailabilityDto>> HandleAsync(GetAvailabilityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validation = await _validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationErrors.ToError(validation);
        }

        IsoDateTime.TryParseDate(query.Date, out var localDate);
        AvailabilityRequest.TryParseVehicleId(query.VehicleId, out var vehicleId);

        // Same rules as booking, so the page never offers a time that booking would reject.
        var target = await AvailabilityRequest.LoadAsync(
            _dealerships, _serviceTypes, _vehicles, query.DealershipId, query.ServiceTypeId, vehicleId, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var (dealership, serviceType) = target.Value;
        var day = await _availability.LoadDayAsync(dealership, serviceType, localDate, cancellationToken);
        var vehicleSlots = vehicleId is { } id
            ? await _appointments.GetVehicleSlotStartsAsync(id, day.OpeningHoursUtc, cancellationToken)
            : null;

        var times = day.ClassifyStartTimes(_clock.UtcNow, vehicleSlots);
        var slots = times
            .Where(time => time.Status == StartTimeStatus.Available)
            .Select(time => new TimeSlotDto(time.Range.StartUtc, time.Range.EndUtc))
            .ToList();

        return new AvailabilityDto(
            dealership.Id,
            serviceType.Id,
            AvailabilityRequest.Format(localDate),
            dealership.TimeZoneId,
            serviceType.DurationMinutes,
            slots,
            times.Select(time => new StartTimeDto(time.Range.StartUtc, time.Range.EndUtc, time.Status)).ToList());
    }
}
