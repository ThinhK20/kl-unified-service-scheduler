using FluentValidation;
using Scheduler.Application.Abstractions;
using Scheduler.Application.Allocation;
using Scheduler.Application.Appointments.Book;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;
using Scheduler.Domain.Errors;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Application.Appointments.Queries;

/// <summary>
/// Which technician and bay booking would pick for this start time right now (SPEC section 6). <paramref name="StartTime"/>
/// is the raw ISO-8601 value with an offset; <paramref name="VehicleId"/> the raw optional query value (null = ignore the car).
/// </summary>
public sealed record GetAssignmentPreviewQuery(int DealershipId, int ServiceTypeId, string? StartTime, string? VehicleId = null);

/// <summary>Checks the preview query shape: the booking request's startTime rule and the availability vehicleId rule.</summary>
public sealed class GetAssignmentPreviewValidator : AbstractValidator<GetAssignmentPreviewQuery>
{
    public GetAssignmentPreviewValidator()
    {
        RuleFor(x => x.DealershipId).GreaterThan(0).WithMessage("dealershipId is required.").OverridePropertyName("dealershipId");
        RuleFor(x => x.ServiceTypeId).GreaterThan(0).WithMessage("serviceTypeId is required.").OverridePropertyName("serviceTypeId");
        RuleFor(x => x.StartTime).BookingStartTime();
        RuleFor(x => x.VehicleId).OptionalVehicleId();
    }
}

/// <summary>
/// Read-only twin of booking (SPEC section 5) for one start time: the same 404/422 checks, the same opening-hours rule,
/// the same order of checks (future start, the car, a free pair), and when a pair is free EXACTLY the first pair
/// booking would try (<see cref="DaySchedule.FindPairs"/>[0] on <see cref="AvailabilityService.LoadDayAsync"/>).
/// Also the state of every technician and bay of the dealership. It reserves nothing.
/// </summary>
public sealed class GetAssignmentPreviewHandler
{
    private readonly IValidator<GetAssignmentPreviewQuery> _validator;
    private readonly IDealershipRepository _dealerships;
    private readonly IServiceTypeRepository _serviceTypes;
    private readonly IVehicleRepository _vehicles;
    private readonly IAppointmentRepository _appointments;
    private readonly AvailabilityService _availability;
    private readonly IClock _clock;

    public GetAssignmentPreviewHandler(
        IValidator<GetAssignmentPreviewQuery> validator,
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

    public async Task<Result<AssignmentPreviewDto>> HandleAsync(GetAssignmentPreviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validation = await _validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationErrors.ToError(validation);
        }

        IsoDateTime.TryParseWithOffsetToUtc(query.StartTime, out var startUtc);
        AvailabilityRequest.TryParseVehicleId(query.VehicleId, out var vehicleId);

        var target = await AvailabilityRequest.LoadAsync(
            _dealerships, _serviceTypes, _vehicles, query.DealershipId, query.ServiceTypeId, vehicleId, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var (dealership, serviceType) = target.Value;

        // Booking's order (SPEC section 5): future start (step 1c) first, so a past start is status Past (200)
        // even outside opening hours; then the whole half-open range must be inside opening hours (step 3, an error).
        var range = TimeRange.FromStart(startUtc, serviceType.Duration);
        var isPast = range.StartUtc <= _clock.UtcNow;
        if (!isPast && !dealership.IsOpenFor(range))
        {
            return Error.FromDomain(DomainErrors.Appointment.OutsideOpeningHours);
        }

        var localDate = DateOnly.FromDateTime(dealership.ToLocal(startUtc));
        var day = await _availability.LoadDayAsync(dealership, serviceType, localDate, cancellationToken);

        // Then the car (section 0c), then the pairs (steps 4-6).
        AssignmentPreviewStatus status;
        ResourcePair? pick = null;
        if (isPast)
        {
            status = AssignmentPreviewStatus.Past;
        }
        else if (vehicleId is { } id && await _appointments.IsVehicleBookedAsync(id, range, cancellationToken))
        {
            status = AssignmentPreviewStatus.VehicleBooked;
        }
        else
        {
            var pairs = day.FindPairs(range);
            pick = pairs.Count > 0 ? pairs[0] : null;
            status = pick is null ? AssignmentPreviewStatus.NoAvailability : AssignmentPreviewStatus.Assignable;
        }

        var technicians = day.TechnicianStates(range, pick)
            .OrderBy(t => t.State)
            .ThenBy(t => t.Technician.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Technician.Id)
            .Select(t => new PreviewTechnicianDto(t.Technician.Id, t.Technician.Name, t.State))
            .ToList();
        var bays = day.ServiceBayStates(range, pick)
            .OrderBy(b => b.State)
            .ThenBy(b => b.ServiceBay.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.ServiceBay.Id)
            .Select(b => new PreviewServiceBayDto(b.ServiceBay.Id, b.ServiceBay.Name, b.ServiceBay.BayType.ToString(), b.State))
            .ToList();

        return new AssignmentPreviewDto(
            dealership.Id,
            serviceType.Id,
            dealership.TimeZoneId,
            range.StartUtc,
            range.EndUtc,
            status,
            pick is null ? null : new AppointmentTechnicianDto(pick.Technician.Id, pick.Technician.Name),
            pick is null ? null : new AppointmentServiceBayDto(pick.ServiceBay.Id, pick.ServiceBay.Name, pick.ServiceBay.BayType.ToString()),
            technicians,
            bays);
    }
}
