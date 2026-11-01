namespace Scheduler.Application.Catalog.Dtos;

/// <summary>A dealership. Open and close times are local, "HH:mm".</summary>
public sealed record DealershipDto(int Id, string Name, string TimeZoneId, string OpenTime, string CloseTime);

/// <summary>A bookable service type.</summary>
public sealed record ServiceTypeDto(
    int Id,
    int DealershipId,
    string Name,
    int DurationMinutes,
    string RequiredSkill,
    string MinimumTier,
    string RequiredBayType);

/// <summary>A customer vehicle.</summary>
public sealed record VehicleDto(int Id, int CustomerId, string Vin, string Make, string Model, int Year);
