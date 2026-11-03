using Scheduler.Application.Abstractions;
using Scheduler.Application.Catalog.Dtos;

namespace Scheduler.Application.Catalog.Queries;

/// <summary>Returns every customer, ordered by Id (SPEC section 6), for the "Booking as" switcher.</summary>
public sealed class GetCustomersHandler
{
    private readonly ICustomerRepository _customers;

    public GetCustomersHandler(ICustomerRepository customers)
    {
        _customers = customers;
    }

    public async Task<IReadOnlyList<CustomerDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var customers = await _customers.GetAllAsync(cancellationToken);
        return customers.OrderBy(c => c.Id).Select(CatalogMapper.ToDto).ToList();
    }
}
