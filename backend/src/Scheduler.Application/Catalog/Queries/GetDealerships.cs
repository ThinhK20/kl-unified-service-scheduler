using Scheduler.Application.Abstractions;
using Scheduler.Application.Catalog.Dtos;

namespace Scheduler.Application.Catalog.Queries;

/// <summary>Returns the active dealerships, ordered by Id.</summary>
public sealed class GetDealershipsHandler
{
    private readonly IDealershipRepository _dealerships;

    public GetDealershipsHandler(IDealershipRepository dealerships)
    {
        _dealerships = dealerships;
    }

    public async Task<IReadOnlyList<DealershipDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var dealerships = await _dealerships.GetActiveAsync(cancellationToken);
        return dealerships.Where(d => d.IsActive).OrderBy(d => d.Id).Select(CatalogMapper.ToDto).ToList();
    }
}
