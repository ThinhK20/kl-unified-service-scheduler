using Microsoft.AspNetCore.Mvc;
using Scheduler.Api.Contracts;
using Scheduler.Api.Http;
using Scheduler.Api.Swagger;
using Scheduler.Application.Appointments.Book;
using Scheduler.Application.Appointments.Cancel;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Appointments.Queries;

namespace Scheduler.Api.Controllers;

/// <summary>Book, list and cancel appointments.</summary>
[ApiController]
[Route("api/appointments")]
public sealed class AppointmentsController : ControllerBase
{
    /// <summary>Books a service. The system picks the technician and the bay.</summary>
    /// <remarks>
    /// Send a new <c>Idempotency-Key</c> per booking attempt. Sending the same key and body again returns the
    /// same 201 response without a second booking; the same key with a different body is 409 IDEMPOTENCY_CONFLICT.
    /// </remarks>
    /// <param name="request">What to book. <c>startTime</c> needs an offset, for example "2026-11-10T10:30:00+07:00".</param>
    /// <param name="idempotencyKey">Required header, max 100 characters.</param>
    /// <param name="handler">Use case handler (injected).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="201">Booked (or the saved response of an earlier identical request). Location points to the appointment.</response>
    /// <response code="400">Missing field or header, bad format, not on the 15-minute grid, or in the past.</response>
    /// <response code="404">Unknown customer, vehicle, dealership or service type.</response>
    /// <response code="409">NO_AVAILABILITY (with suggestedStartTimes) or IDEMPOTENCY_CONFLICT.</response>
    /// <response code="422">A booking rule is broken, for example outside opening hours.</response>
    /// <response code="503">The database is busy. Retry with the same Idempotency-Key.</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ApiProblems.ContentType)]
    public async Task<IActionResult> Book(
        [FromBody] BookAppointmentRequest request,
        [FromHeader(Name = IdempotencyKeyHeaderFilter.HeaderName)] string? idempotencyKey,
        [FromServices] BookAppointmentHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new BookAppointmentCommand(
            idempotencyKey,
            request.CustomerId,
            request.VehicleId,
            request.DealershipId,
            request.ServiceTypeId,
            request.StartTime);

        var result = await handler.HandleAsync(command, cancellationToken);

        // A replay answers exactly like the original: 201 + Location + the saved body.
        return this.ToActionResult(result, booked => Created(LocationOf(booked.Appointment.Id), booked.Appointment));
    }

    /// <summary>Lists a customer's appointments, newest first.</summary>
    /// <param name="customerId">Customer id.</param>
    /// <param name="handler">Use case handler (injected).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">Appointments ordered by start time, newest first (empty for an unknown customer).</response>
    /// <response code="400">customerId is missing or not positive.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AppointmentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    public async Task<IActionResult> GetByCustomer(
        [FromQuery] int customerId,
        [FromServices] GetAppointmentsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetAppointmentsQuery(customerId), cancellationToken);
        return this.ToActionResult(result, appointments => Ok(appointments));
    }

    /// <summary>Gets one appointment.</summary>
    /// <param name="id">Appointment id.</param>
    /// <param name="handler">Use case handler (injected).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">The appointment.</response>
    /// <response code="404">Unknown appointment.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType)]
    public async Task<IActionResult> GetById(int id, [FromServices] GetAppointmentByIdHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetAppointmentByIdQuery(id), cancellationToken);
        return this.ToActionResult(result, appointment => Ok(appointment));
    }

    /// <summary>Cancels a confirmed appointment and frees its technician and bay.</summary>
    /// <param name="id">Appointment id.</param>
    /// <param name="handler">Use case handler (injected).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">The cancelled appointment.</response>
    /// <response code="404">Unknown appointment.</response>
    /// <response code="409">APPOINTMENT_NOT_CANCELLABLE: it is not Confirmed.</response>
    /// <response code="503">The database is busy. Try again.</response>
    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ApiProblems.ContentType)]
    public async Task<IActionResult> Cancel(int id, [FromServices] CancelAppointmentHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CancelAppointmentCommand(id), cancellationToken);
        return this.ToActionResult(result, appointment => Ok(appointment));
    }

    private static string LocationOf(int id) => $"/api/appointments/{id}";
}
