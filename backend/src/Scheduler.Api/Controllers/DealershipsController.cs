using Microsoft.AspNetCore.Mvc;
using Scheduler.Api.Http;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Appointments.Queries;
using Scheduler.Application.Catalog.Dtos;
using Scheduler.Application.Catalog.Queries;

namespace Scheduler.Api.Controllers;

/// <summary>Dealerships, their free times and the expected technician and bay.</summary>
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

    /// <summary>
    /// How a dealership is set up (SPEC section 6): hours, all service types, technicians and bays (inactive ones too),
    /// and for each service the active technicians qualified for it and the active bays of the right type. Read only.
    /// </summary>
    /// <param name="id">Dealership id. An inactive dealership is still returned.</param>
    /// <param name="handler">Use case handler (injected).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">
    /// The setup. Service types by id; technicians and bays active first, then by name. Times are local "HH:mm".
    /// </response>
    /// <response code="404">Unknown dealership.</response>
    [HttpGet("{id:int}/setup")]
    [ProducesResponseType<DealershipSetupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType)]
    public async Task<IActionResult> GetSetup(int id, [FromServices] GetDealershipSetupHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetDealershipSetupQuery(id), cancellationToken);
        return this.ToActionResult(result, dto => Ok(dto));
    }

    /// <summary>
    /// Start times for a service on one dealership-local date, each with a status (SPEC section 6). Nothing is reserved;
    /// the booking endpoint stays the final authority.
    /// </summary>
    /// <param name="id">Dealership id.</param>
    /// <param name="serviceTypeId">Service type id (must be offered at this dealership).</param>
    /// <param name="date">Dealership-local calendar date, "YYYY-MM-DD".</param>
    /// <param name="vehicleId">Optional positive vehicle id. When given, times when this car is booked anywhere are "VehicleBooked" and are not in <c>slots</c>.</param>
    /// <param name="handler">Use case handler (injected).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">
    /// <c>times</c>: every 15-minute start where the job fits in opening hours, ascending, UTC, with <c>status</c>
    /// (first match wins): Past, VehicleBooked, Booked, Available. <c>slots</c>: exactly the Available times.
    /// </response>
    /// <response code="400">Missing or badly formatted parameters (including a vehicleId that is not a positive number).</response>
    /// <response code="404">Unknown dealership, service type or vehicle.</response>
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
        [FromQuery] string? vehicleId,
        [FromServices] GetAvailabilityHandler handler,
        CancellationToken cancellationToken)
    {
        var query = new GetAvailabilityQuery(id, serviceTypeId, date, RawVehicleId(vehicleId));
        var result = await handler.HandleAsync(query, cancellationToken);
        return this.ToActionResult(result, dto => Ok(dto));
    }

    /// <summary>
    /// Per-day availability for a range of dealership-local dates, for a calendar (SPEC section 6). Nothing is reserved.
    /// </summary>
    /// <param name="id">Dealership id.</param>
    /// <param name="serviceTypeId">Service type id (must be offered at this dealership).</param>
    /// <param name="from">First dealership-local date, "YYYY-MM-DD".</param>
    /// <param name="to">Last dealership-local date (inclusive), "YYYY-MM-DD". At most 42 days after <paramref name="from"/> counting both.</param>
    /// <param name="vehicleId">Optional positive vehicle id; same meaning as on the day endpoint.</param>
    /// <param name="handler">Use case handler (injected).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">
    /// One entry per date, ascending. <c>availableCount</c> is the number of Available times by the day endpoint's rules.
    /// <c>status</c>: Past (before today, dealership time), FullyBooked (no Available time) or Available.
    /// </response>
    /// <response code="400">Missing or badly formatted parameters, to before from, or a range longer than 42 days.</response>
    /// <response code="404">Unknown dealership, service type or vehicle.</response>
    /// <response code="422">Inactive dealership or service, or the service is not offered here.</response>
    [HttpGet("{id:int}/availability/days")]
    [ProducesResponseType<AvailabilityDaysDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ApiProblems.ContentType)]
    public async Task<IActionResult> GetAvailabilityDays(
        int id,
        [FromQuery] int serviceTypeId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? vehicleId,
        [FromServices] GetAvailabilityDaysHandler handler,
        CancellationToken cancellationToken)
    {
        var query = new GetAvailabilityDaysQuery(id, serviceTypeId, from, to, RawVehicleId(vehicleId));
        var result = await handler.HandleAsync(query, cancellationToken);
        return this.ToActionResult(result, dto => Ok(dto));
    }

    /// <summary>
    /// Which technician and bay booking would pick for this start time if it were made now, and the state of every
    /// technician and bay of the dealership (SPEC section 6). Nothing is reserved; the booking endpoint stays the final authority.
    /// </summary>
    /// <param name="id">Dealership id.</param>
    /// <param name="serviceTypeId">Service type id (must be offered at this dealership).</param>
    /// <param name="startTime">Start, ISO-8601 with an offset, on the 15-minute grid (same rules as the booking request).</param>
    /// <param name="vehicleId">Optional positive vehicle id. When the car is booked anywhere in the range the status is VehicleBooked.</param>
    /// <param name="handler">Use case handler (injected).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">
    /// <c>status</c> (first match wins): Past, VehicleBooked, NoAvailability, Assignable. When Assignable,
    /// <c>technician</c> and <c>serviceBay</c> are the first pair booking would try; otherwise null. <c>technicians</c> and
    /// <c>serviceBays</c> list every resource with a state, the expected pick (WillServe) first.
    /// </response>
    /// <response code="400">Missing or badly formatted parameters (startTime without an offset or off the grid, bad vehicleId).</response>
    /// <response code="404">Unknown dealership, service type or vehicle.</response>
    /// <response code="422">Inactive dealership or service, the service is not offered here, or the job is outside opening hours.</response>
    [HttpGet("{id:int}/assignment-preview")]
    [ProducesResponseType<AssignmentPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ApiProblems.ContentType)]
    public async Task<IActionResult> GetAssignmentPreview(
        int id,
        [FromQuery] int serviceTypeId,
        [FromQuery] string? startTime,
        [FromQuery] string? vehicleId,
        [FromServices] GetAssignmentPreviewHandler handler,
        CancellationToken cancellationToken)
    {
        var query = new GetAssignmentPreviewQuery(id, serviceTypeId, startTime, RawVehicleId(vehicleId));
        var result = await handler.HandleAsync(query, cancellationToken);
        return this.ToActionResult(result, dto => Ok(dto));
    }

    // Model binding turns "?vehicleId=" into null; it is present but not a number, so pass "" and let validation say 400.
    private string? RawVehicleId(string? bound) =>
        bound ?? (Request.Query.ContainsKey("vehicleId") ? string.Empty : null);
}
