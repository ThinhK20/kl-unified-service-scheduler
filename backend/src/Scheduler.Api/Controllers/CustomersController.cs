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
    /// <summary>Lists a customer's vehicles.</summary>
    /// <param name="id">Customer id (the demo customer is 1).</param>
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
