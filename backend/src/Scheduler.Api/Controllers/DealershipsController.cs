using Microsoft.AspNetCore.Mvc;
using Scheduler.Api.Http;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Appointments.Queries;
using Scheduler.Application.Catalog.Dtos;
using Scheduler.Application.Catalog.Queries;

namespace Scheduler.Api.Controllers;

/// <summary>Dealerships and their free times.</summary>
[ApiController]
[Route("api/dealerships")]
public sealed class DealershipsController : ControllerBase
{
    /// <summary>Lists the active dealerships.</summary>
    /// <response code="200">Active dealerships, ordered by id. Open and close times are local "HH:mm".</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DealershipDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromServices] GetDealershipsHandler handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Free start times for a service on one dealership-local date. Nothing is reserved.</summary>
    /// <param name="id">Dealership id.</param>
    /// <param name="serviceTypeId">Service type id (must be offered at this dealership).</param>
    /// <param name="date">Dealership-local calendar date, "YYYY-MM-DD".</param>
    /// <param name="handler">Use case handler (injected).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">Free start times in the future, ascending, in UTC.</response>
    /// <response code="400">Missing or badly formatted parameters.</response>
    /// <response code="404">Unknown dealership or service type.</response>
    /// <response code="422">Inactive dealership or service, or the service is not offered here.</response>
    [HttpGet("{id:int}/availability")]
    [ProducesResponseType<AvailabilityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ApiProblems.ContentType)]
    public async Task<IActionResult> GetAvailability(
        int id,
        [FromQuery] int serviceTypeId,
        [FromQuery] string? date,
        [FromServices] GetAvailabilityHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetAvailabilityQuery(id, serviceTypeId, date), cancellationToken);
        return this.ToActionResult(result, dto => Ok(dto));
    }
}
