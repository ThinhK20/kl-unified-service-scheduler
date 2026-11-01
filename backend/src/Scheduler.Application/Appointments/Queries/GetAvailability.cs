using FluentValidation;
using Scheduler.Application.Abstractions;
using Scheduler.Application.Allocation;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;
using Scheduler.Domain.Errors;

namespace Scheduler.Application.Appointments.Queries;

/// <summary>Free start times for a service on a dealership-local date ("YYYY-MM-DD").</summary>
public sealed record GetAvailabilityQuery(int DealershipId, int ServiceTypeId, string? Date);

/// <summary>Checks the availability query shape.</summary>
public sealed class GetAvailabilityValidator : AbstractValidator<GetAvailabilityQuery>
{
    public GetAvailabilityValidator()
    {
        RuleFor(x => x.DealershipId).GreaterThan(0).WithMessage("dealershipId is required.").OverridePropertyName("dealershipId");
        RuleFor(x => x.ServiceTypeId).GreaterThan(0).WithMessage("serviceTypeId is required.").OverridePropertyName("serviceTypeId");
        RuleFor(x => x.Date)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("date is required.")
            .Must(value => IsoDateTime.TryParseDate(value, out _)).WithMessage("date must be in the format YYYY-MM-DD.")
            .OverridePropertyName("date");
    }
}

/// <summary>
/// Returns every grid start time on that local day where at least one valid (technician, bay) pair is free for the
/// full duration and the start is in the future. It reserves nothing.
/// </summary>
public sealed class GetAvailabilityHandler
{
    private readonly IValidator<GetAvailabilityQuery> _validator;
    private readonly IDealershipRepository _dealerships;
    private readonly IServiceTypeRepository _serviceTypes;
    private readonly AvailabilityService _availability;
    private readonly IClock _clock;

    public GetAvailabilityHandler(
        IValidator<GetAvailabilityQuery> validator,
        IDealershipRepository dealerships,
        IServiceTypeRepository serviceTypes,
        AvailabilityService availability,
        IClock clock)
    {
        _validator = validator;
        _dealerships = dealerships;
        _serviceTypes = serviceTypes;
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

        var dealership = await _dealerships.GetByIdAsync(query.DealershipId, cancellationToken);
        if (dealership is null)
        {
            return Error.NotFound("Dealership", query.DealershipId);
        }

        var serviceType = await _serviceTypes.GetByIdAsync(query.ServiceTypeId, cancellationToken);
        if (serviceType is null)
        {
            return Error.NotFound("Service type", query.ServiceTypeId);
        }

        // Same rules as booking, so the page never offers a time that booking would reject.
        if (!dealership.IsActive)
        {
            return Error.FromDomain(DomainErrors.Appointment.DealershipInactive);
        }

        if (!serviceType.IsActive)
        {
            return Error.FromDomain(DomainErrors.Appointment.ServiceTypeInactive);
        }

        if (!serviceType.IsOfferedAt(dealership.Id))
        {
            return Error.FromDomain(DomainErrors.Appointment.ServiceNotOfferedAtDealership);
        }

        var day = await _availability.LoadDayAsync(dealership, serviceType, localDate, cancellationToken);
        var slots = day.FreeRanges(_clock.UtcNow)
            .Select(range => new TimeSlotDto(range.StartUtc, range.EndUtc))
            .ToList();

        return new AvailabilityDto(
            dealership.Id,
            serviceType.Id,
            localDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            dealership.TimeZoneId,
            serviceType.DurationMinutes,
            slots);
    }
}
