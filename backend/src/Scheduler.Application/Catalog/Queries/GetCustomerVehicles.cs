using Scheduler.Application.Abstractions;
using Scheduler.Application.Catalog.Dtos;
using Scheduler.Application.Common;

namespace Scheduler.Application.Catalog.Queries;

/// <summary>The vehicles of one customer.</summary>
public sealed record GetCustomerVehiclesQuery(int CustomerId);

/// <summary>Returns the customer's vehicles ordered by Id, or NOT_FOUND for an unknown customer.</summary>
public sealed class GetCustomerVehiclesHandler
{
    private readonly ICustomerRepository _customers;
    private readonly IVehicleRepository _vehicles;

    public GetCustomerVehiclesHandler(ICustomerRepository customers, IVehicleRepository vehicles)
    {
        _customers = customers;
        _vehicles = vehicles;
    }

    public async Task<Result<IReadOnlyList<VehicleDto>>> HandleAsync(GetCustomerVehiclesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await _customers.ExistsAsync(query.CustomerId, cancellationToken))
        {
            return Error.NotFound("Customer", query.CustomerId);
        }

        var vehicles = await _vehicles.GetByCustomerAsync(query.CustomerId, cancellationToken);
        IReadOnlyList<VehicleDto> dtos = vehicles.OrderBy(v => v.Id).Select(CatalogMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}
