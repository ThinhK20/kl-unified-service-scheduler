using Microsoft.AspNetCore.Mvc;
using Scheduler.Api.Http;
using Scheduler.Application.Catalog.Dtos;
using Scheduler.Application.Catalog.Queries;

namespace Scheduler.Api.Controllers;

/// <summary>Bookable services.</summary>
[ApiController]
[Route("api/service-types")]
public sealed class ServiceTypesController : ControllerBase
{
    /// <summary>Lists active service types, optionally for one dealership.</summary>
    /// <param name="dealershipId">Only services of this dealership (optional).</param>
    /// <param name="handler">Use case handler (injected).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">Active service types, ordered by id.</response>
    /// <response code="400">dealershipId is not a number.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ServiceTypeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? dealershipId,
        [FromServices] GetServiceTypesHandler handler,
        CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(new GetServiceTypesQuery(dealershipId), cancellationToken));
}
