using System.Globalization;
using Scheduler.Application.Abstractions;
using Scheduler.Application.Catalog.Dtos;
using Scheduler.Application.Common;

namespace Scheduler.Application.Catalog.Queries;

/// <summary>The setup of one dealership.</summary>
public sealed record GetDealershipSetupQuery(int DealershipId);

/// <summary>
/// Returns the setup of a dealership (SPEC section 6), or NOT_FOUND for an unknown id. An inactive dealership is still
/// returned: this is information, not booking. Who is qualified and which bays match use the same domain rules as
/// booking (<see cref="Domain.Entities.Technician.IsQualifiedFor"/>, <see cref="Domain.Entities.ServiceBay.CanHost"/>),
/// counting only ACTIVE technicians and bays. Inactive items are still listed, after the active ones.
/// </summary>
public sealed class GetDealershipSetupHandler
{
    private readonly IDealershipRepository _dealerships;
    private readonly IServiceTypeRepository _serviceTypes;
    private readonly ITechnicianRepository _technicians;
    private readonly IServiceBayRepository _serviceBays;

    public GetDealershipSetupHandler(
        IDealershipRepository dealerships,
        IServiceTypeRepository serviceTypes,
        ITechnicianRepository technicians,
        IServiceBayRepository serviceBays)
    {
        _dealerships = dealerships;
        _serviceTypes = serviceTypes;
        _technicians = technicians;
        _serviceBays = serviceBays;
    }

    public async Task<Result<DealershipSetupDto>> HandleAsync(GetDealershipSetupQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var dealership = await _dealerships.GetByIdAsync(query.DealershipId, cancellationToken);
        if (dealership is null)
        {
            return Error.NotFound("Dealership", query.DealershipId);
        }

        var serviceTypes = (await _serviceTypes.GetByDealershipAsync(dealership.Id, cancellationToken))
            .Where(s => s.DealershipId == dealership.Id)
            .OrderBy(s => s.Id)
            .ToList();
        var technicians = (await _technicians.GetByDealershipWithSkillsAsync(dealership.Id, cancellationToken))
            .Where(t => t.DealershipId == dealership.Id)
            .OrderBy(t => t.IsActive ? 0 : 1)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Id)
            .ToList();
        var bays = (await _serviceBays.GetByDealershipAsync(dealership.Id, cancellationToken))
            .Where(b => b.DealershipId == dealership.Id)
            .OrderBy(b => b.IsActive ? 0 : 1)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Id)
            .ToList();

        var serviceTypeDtos = serviceTypes.Select(s => new SetupServiceTypeDto(
                s.Id,
                s.Name,
                s.DurationMinutes,
                s.RequiredSkill.ToString(),
                s.MinimumTier.ToString(),
                s.RequiredBayType.ToString(),
                s.IsActive,
                technicians.Where(t => t.IsActive && t.IsQualifiedFor(s.RequiredSkill, s.MinimumTier)).Select(t => t.Id).Order().ToList(),
                bays.Where(b => b.CanHost(s, dealership.Id)).Select(b => b.Id).Order().ToList()))
            .ToList();

        var technicianDtos = technicians.Select(t => new SetupTechnicianDto(
                t.Id,
                t.Name,
                HoursAndMinutes(t.ShiftStart),
                HoursAndMinutes(t.ShiftEnd),
                t.IsActive,
                t.Skills.OrderBy(k => k.Skill).Select(k => new SetupSkillDto(k.Skill.ToString(), k.Tier.ToString())).ToList()))
            .ToList();

        var bayDtos = bays.Select(b => new SetupServiceBayDto(b.Id, b.Name, b.BayType.ToString(), b.IsActive)).ToList();

        var info = new DealershipSetupInfoDto(
            dealership.Id,
            dealership.Name,
            dealership.TimeZoneId,
            HoursAndMinutes(dealership.OpenTime),
            HoursAndMinutes(dealership.CloseTime),
            dealership.IsActive);

        return new DealershipSetupDto(info, serviceTypeDtos, technicianDtos, bayDtos);
    }

    private static string HoursAndMinutes(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}
