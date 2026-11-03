using Microsoft.AspNetCore.Mvc;
using Scheduler.Api.Http;
using Scheduler.Application.Catalog.Dtos;
using Scheduler.Application.Catalog.Queries;

namespace Scheduler.Api.Controllers;

/// <summary>Customers and their vehicles.</summary>
[ApiController]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    /// <summary>Lists every customer (the demo has three), for the "Booking as" switcher.</summary>
    /// <param name="handler">Use case handler (injected).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">All customers, ordered by id.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CustomerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromServices] GetCustomersHandler handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Lists a customer's vehicles.</summary>
    /// <param name="id">Customer id (the demo customers are 1, 2 and 3).</param>
    /// <param name="handler">Use case handler (injected).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">The vehicles, ordered by id.</response>
    /// <response code="404">Unknown customer.</response>
    [HttpGet("{id:int}/vehicles")]
    [ProducesResponseType<IReadOnlyList<VehicleDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType)]
    public async Task<IActionResult> GetVehicles(int id, [FromServices] GetCustomerVehiclesHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetCustomerVehiclesQuery(id), cancellationToken);
        return this.ToActionResult(result, vehicles => Ok(vehicles));
    }
}
