using Scheduler.Application.Abstractions;
using Scheduler.Application.Appointments.Book;
using Scheduler.Application.Appointments.Cancel;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;
using Scheduler.Application.Tests.TestSupport;

namespace Scheduler.Application.Tests.Observability;

/// <summary>SPEC section 10: the handlers report one outcome per request and each database retry.</summary>
public class AppointmentTelemetryReportingTests
{
    private const int OilChange = 1;
    private readonly TestApp _app = new();

    private Task<Result<BookAppointmentResult>> BookAsync(string key, int hour = 9, int serviceTypeId = OilChange, int vehicleId = 1) =>
        _app.BookHandler().HandleAsync(TestApp.Command(serviceTypeId, Local.Iso(hour), key, vehicleId: vehicleId), CancellationToken.None);

    [Fact]
    public async Task Created_replayed_and_conflicts_are_reported_with_their_outcome()
    {
        await BookAsync("a");                    // created
        await BookAsync("a");                    // same key + body: replayed
        await BookAsync("b", vehicleId: 2);      // Nam + Bay 1 busy, Lan + Bay 1 too: the only standard bay is taken
        await BookAsync("c");                    // car 1 is busy at 09:00

        _app.Telemetry.BookingsStarted.Should().AllBeEquivalentTo((1, OilChange));
        _app.Telemetry.BookingOutcomes.Should().Equal(
            BookingOutcomes.Created, BookingOutcomes.Replayed, BookingOutcomes.NoAvailability, BookingOutcomes.VehicleAlreadyBooked);
        _app.Telemetry.Retries.Should().BeEmpty();
    }

    [Fact]
    public async Task Validation_not_found_rule_violation_and_idempotency_conflict_are_reported()
    {
        await BookAsync("");                                                       // no Idempotency-Key
        await _app.BookHandler().HandleAsync(TestApp.Command(OilChange, Local.Iso(9), "nf", vehicleId: 999), CancellationToken.None);
        await BookAsync("owner", vehicleId: 3);                                    // car 3 belongs to customer 2
        await BookAsync("k");
        await BookAsync("k", hour: 10);                                            // same key, other body

        _app.Telemetry.BookingOutcomes.Should().Equal(
            BookingOutcomes.Validation, BookingOutcomes.NotFound, BookingOutcomes.RuleViolation, BookingOutcomes.Created, BookingOutcomes.IdempotencyConflict);
    }

    [Fact]
    public async Task One_transient_error_is_one_retry_and_then_created()
    {
        _app.Appointments.TransientFailuresLeft = 1;

        var result = await BookAsync("retry");

        result.IsSuccess.Should().BeTrue();
        _app.Telemetry.Retries.Should().ContainSingle().Which.Should().BeOfType<TransientDatabaseException>();
        _app.Telemetry.BookingOutcomes.Should().Equal(BookingOutcomes.Created);
    }

    [Fact]
    public async Task Two_transient_errors_are_one_retry_and_database_busy()
    {
        _app.Appointments.TransientFailuresLeft = 2;

        await BookAsync("busy");

        _app.Telemetry.Retries.Should().HaveCount(1);
        _app.Telemetry.BookingOutcomes.Should().Equal(BookingOutcomes.DatabaseBusy);
    }

    [Fact]
    public async Task An_exception_that_escapes_is_reported_as_error()
    {
        _app.Appointments.NextAddResults.Enqueue(null);
        _app.Appointments.BeforeNextAdd = () => throw new InvalidOperationException("bug");

        var act = () => BookAsync("boom");

        await act.Should().ThrowAsync<InvalidOperationException>();
        _app.Telemetry.BookingOutcomes.Should().Equal(BookingOutcomes.Error);
    }

    [Fact]
    public async Task Cancellations_are_reported_with_their_outcome()
    {
        var booked = await _app.BookAsync(OilChange, Local.Iso(9), "to-cancel");
        var handler = _app.CancelHandler();

        await handler.HandleAsync(new CancelAppointmentCommand(booked.Appointment.Id), CancellationToken.None);
        await handler.HandleAsync(new CancelAppointmentCommand(booked.Appointment.Id), CancellationToken.None);
        await handler.HandleAsync(new CancelAppointmentCommand(4040), CancellationToken.None);
        var second = await _app.BookAsync(OilChange, Local.Iso(11), "busy-cancel");
        _app.UnitOfWork.NextSaveException = new TransientDatabaseException("deadlock");
        await handler.HandleAsync(new CancelAppointmentCommand(second.Appointment.Id), CancellationToken.None);

        _app.Telemetry.Cancellations.Should().Equal(
            (booked.Appointment.Id, CancellationOutcomes.Cancelled),
            (booked.Appointment.Id, CancellationOutcomes.NotCancellable),
            (4040, CancellationOutcomes.NotFound),
            (second.Appointment.Id, CancellationOutcomes.DatabaseBusy));
    }

    [Theory]
    [InlineData(ErrorCodes.NoAvailability, BookingOutcomes.NoAvailability)]
    [InlineData(ErrorCodes.VehicleAlreadyBooked, BookingOutcomes.VehicleAlreadyBooked)]
    [InlineData(ErrorCodes.IdempotencyConflict, BookingOutcomes.IdempotencyConflict)]
    [InlineData(ErrorCodes.BookingRuleViolation, BookingOutcomes.RuleViolation)]
    [InlineData(ErrorCodes.InvalidRequest, BookingOutcomes.Validation)]
    [InlineData(ErrorCodes.NotFound, BookingOutcomes.NotFound)]
    [InlineData(ErrorCodes.DatabaseBusy, BookingOutcomes.DatabaseBusy)]
    [InlineData(ErrorCodes.InternalError, BookingOutcomes.Error)]
    public void Every_error_code_maps_to_one_booking_outcome(string code, string outcome)
    {
        Result<BookAppointmentResult> result = new Error(code, "x");

        BookingOutcomes.From(result).Should().Be(outcome);
    }

    [Fact]
    public void Cancel_codes_map_to_cancel_outcomes()
    {
        CancellationOutcomes.From(Result.Failure<AppointmentDto>(new Error(ErrorCodes.AppointmentNotCancellable, "x"))).Should().Be(CancellationOutcomes.NotCancellable);
        CancellationOutcomes.From(Result.Failure<AppointmentDto>(Error.NotFound("Appointment", 1))).Should().Be(CancellationOutcomes.NotFound);
        CancellationOutcomes.From(Result.Failure<AppointmentDto>(Error.DatabaseBusy())).Should().Be(CancellationOutcomes.DatabaseBusy);
    }
}
