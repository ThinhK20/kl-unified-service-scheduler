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

/// <summary>A customer who books services (SPEC section 6, GET /api/customers).</summary>
public sealed record CustomerDto(int Id, string Name, string Email, string Phone);

/// <summary>
/// Everything a dealership is set up with (SPEC section 6, GET /api/dealerships/{id}/setup): its hours, ALL its service
/// types, technicians and bays (inactive ones too), and for each service who and which bays can do it now.
/// </summary>
public sealed record DealershipSetupDto(
    DealershipSetupInfoDto Dealership,
    IReadOnlyList<SetupServiceTypeDto> ServiceTypes,
    IReadOnlyList<SetupTechnicianDto> Technicians,
    IReadOnlyList<SetupServiceBayDto> ServiceBays);

/// <summary>A dealership with local open and close times ("HH:mm") and whether it takes bookings.</summary>
public sealed record DealershipSetupInfoDto(int Id, string Name, string TimeZoneId, string OpenTime, string CloseTime, bool IsActive);

/// <summary>A service type with the ACTIVE technicians qualified for it and the ACTIVE bays of the right type.</summary>
public sealed record SetupServiceTypeDto(
    int Id,
    string Name,
    int DurationMinutes,
    string RequiredSkill,
    string MinimumTier,
    string RequiredBayType,
    bool IsActive,
    IReadOnlyList<int> QualifiedTechnicianIds,
    IReadOnlyList<int> MatchingBayIds);

/// <summary>A technician with the local shift ("HH:mm") and skills.</summary>
public sealed record SetupTechnicianDto(int Id, string Name, string ShiftStart, string ShiftEnd, bool IsActive, IReadOnlyList<SetupSkillDto> Skills);

/// <summary>One skill of a technician and the tier in it.</summary>
public sealed record SetupSkillDto(string Skill, string Tier);

/// <summary>A service bay.</summary>
public sealed record SetupServiceBayDto(int Id, string Name, string BayType, bool IsActive);
