using System.Globalization;
using FluentValidation;
using Scheduler.Application.Abstractions;
using Scheduler.Application.Common;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Errors;

namespace Scheduler.Application.Appointments.Queries;

/// <summary>
/// Rules shared by the day and the days availability queries (SPEC section 6), so both endpoints validate and answer
/// 404 / 422 the same way.
/// </summary>
internal static class AvailabilityRequest
{
    public const string VehicleIdField = "vehicleId";
    public const string VehicleIdMessage = "vehicleId must be a positive number.";

    /// <summary>
    /// Null (parameter absent) is valid and means "ignore the car". Otherwise it must be a plain positive integer
    /// (digits only, no sign or spaces, fits in an int).
    /// </summary>
    public static bool TryParseVehicleId(string? value, out int? vehicleId)
    {
        vehicleId = null;
        if (value is null)
        {
            return true;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            return false;
        }

        vehicleId = id;
        return true;
    }

    /// <summary>"YYYY-MM-DD", required, inside <see cref="SupportedDates"/>. Same messages as the original date rule.</summary>
    public static IRuleBuilderOptions<T, string?> LocalDate<T>(this IRuleBuilderInitial<T, string?> rule, string field) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage($"{field} is required.")
            .Must(value => IsoDateTime.TryParseDate(value, out _)).WithMessage($"{field} must be in the format YYYY-MM-DD.")
            .Must(value => IsoDateTime.TryParseDate(value, out var date) && SupportedDates.Contains(date))
            .WithMessage(SupportedDates.Message(field))
            .OverridePropertyName(field);

    /// <summary>A valid optional vehicleId (see <see cref="TryParseVehicleId"/>).</summary>
    public static IRuleBuilderOptions<T, string?> OptionalVehicleId<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .Must(value => TryParseVehicleId(value, out _)).WithMessage(VehicleIdMessage)
            .OverridePropertyName(VehicleIdField);

    /// <summary>
    /// Loads the dealership and service type and applies the same rules as booking (SPEC section 5, step 1b), so the
    /// page never offers a time that booking would reject: 404 for unknown dealership, service type or vehicle
    /// (all 404s before any 422, as in booking), then 422 for an inactive dealership or service, or a service that is
    /// not offered there. There is no ownership check: availability has no customerId.
    /// </summary>
    public static async Task<Result<AvailabilityTarget>> LoadAsync(
        IDealershipRepository dealerships,
        IServiceTypeRepository serviceTypes,
        IVehicleRepository vehicles,
        int dealershipId,
        int serviceTypeId,
        int? vehicleId,
        CancellationToken cancellationToken)
    {
        var dealership = await dealerships.GetByIdAsync(dealershipId, cancellationToken);
        if (dealership is null)
        {
            return Error.NotFound("Dealership", dealershipId);
        }

        var serviceType = await serviceTypes.GetByIdAsync(serviceTypeId, cancellationToken);
        if (serviceType is null)
        {
            return Error.NotFound("Service type", serviceTypeId);
        }

        if (vehicleId is { } id && await vehicles.GetByIdAsync(id, cancellationToken) is null)
        {
            return Error.NotFound("Vehicle", id);
        }

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

        return new AvailabilityTarget(dealership, serviceType);
    }

    /// <summary>"YYYY-MM-DD".</summary>
    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>The dealership and service type an availability query is about, after the rule checks.</summary>
internal sealed record AvailabilityTarget(Dealership Dealership, ServiceType ServiceType);
