using System.Globalization;
using Scheduler.Domain.Entities;

namespace Scheduler.Application.Catalog.Dtos;

/// <summary>Maps catalog entities to DTOs by hand.</summary>
public static class CatalogMapper
{
    public static DealershipDto ToDto(Dealership dealership) => new(
        dealership.Id,
        dealership.Name,
        dealership.TimeZoneId,
        dealership.OpenTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        dealership.CloseTime.ToString("HH:mm", CultureInfo.InvariantCulture));

    public static ServiceTypeDto ToDto(ServiceType serviceType) => new(
        serviceType.Id,
        serviceType.DealershipId,
        serviceType.Name,
        serviceType.DurationMinutes,
        serviceType.RequiredSkill.ToString(),
        serviceType.MinimumTier.ToString(),
        serviceType.RequiredBayType.ToString());

    public static VehicleDto ToDto(Vehicle vehicle) => new(
        vehicle.Id,
        vehicle.CustomerId,
        vehicle.Vin,
        vehicle.Make,
        vehicle.Model,
        vehicle.Year);
}
