using Scheduler.Application.Abstractions;
using Scheduler.Application.Catalog.Dtos;

namespace Scheduler.Application.Catalog.Queries;

/// <summary>Service types, optionally for one dealership.</summary>
public sealed record GetServiceTypesQuery(int? DealershipId);

/// <summary>Returns active service types, ordered by Id. An unknown dealership gives an empty list.</summary>
public sealed class GetServiceTypesHandler
{
    private readonly IServiceTypeRepository _serviceTypes;

    public GetServiceTypesHandler(IServiceTypeRepository serviceTypes)
    {
        _serviceTypes = serviceTypes;
    }

    public async Task<IReadOnlyList<ServiceTypeDto>> HandleAsync(GetServiceTypesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var serviceTypes = await _serviceTypes.GetActiveAsync(query.DealershipId, cancellationToken);
        return serviceTypes
            .Where(s => s.IsActive && (query.DealershipId is null || s.DealershipId == query.DealershipId))
            .OrderBy(s => s.Id)
            .Select(CatalogMapper.ToDto)
            .ToList();
    }
}
