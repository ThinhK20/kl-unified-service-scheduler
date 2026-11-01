using Scheduler.Application.Abstractions;
using Scheduler.Application.Appointments.Cancel;
using Scheduler.Application.Common;
using Scheduler.Application.Tests.TestSupport;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Tests.Appointments;

public class CancelAppointmentHandlerTests
{
    private const int BrakeInspection = 2;
    private readonly TestApp _app = new();

    private Task<Result<Scheduler.Application.Appointments.Dtos.AppointmentDto>> Cancel(int id) =>
        _app.CancelHandler().HandleAsync(new CancelAppointmentCommand(id), CancellationToken.None);

    [Fact]
    public async Task Cancel_sets_cancelled_frees_the_slots_and_saves()
    {
        var booked = await _app.BookAsync(BrakeInspection, Local.Iso(13), "a");

        var result = await Cancel(booked.Appointment.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Cancelled");
        var saved = _app.Data.Appointments.Single();
        saved.Status.Should().Be(AppointmentStatus.Cancelled);
        saved.ResourceSlots.Should().BeEmpty();
        _app.UnitOfWork.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task After_cancel_the_same_time_can_be_booked_again()
    {
        var booked = await _app.BookAsync(BrakeInspection, Local.Iso(13), "a");
        await _app.BookAsync(BrakeInspection, Local.Iso(9), "fill-nam"); // unrelated
        await Cancel(booked.Appointment.Id);

        var again = await _app.BookAsync(BrakeInspection, Local.Iso(13), "b");

        again.Appointment.Technician.Name.Should().Be("Minh");
        again.Appointment.ServiceBay.Name.Should().Be("Bay 2");
    }

    [Fact]
    public async Task Unknown_appointment_is_not_found()
    {
        var result = await Cancel(12345);

        result.Error!.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Cancelling_twice_is_not_cancellable()
    {
        var booked = await _app.BookAsync(BrakeInspection, Local.Iso(13), "a");
        await Cancel(booked.Appointment.Id);

        var result = await Cancel(booked.Appointment.Id);

        result.Error!.Code.Should().Be(ErrorCodes.AppointmentNotCancellable);
        _app.UnitOfWork.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Concurrency_conflict_on_save_is_not_cancellable()
    {
        var booked = await _app.BookAsync(BrakeInspection, Local.Iso(13), "a");
        _app.UnitOfWork.NextSaveException = new ConcurrencyConflictException("rowversion changed");

        var result = await Cancel(booked.Appointment.Id);

        result.Error!.Code.Should().Be(ErrorCodes.AppointmentNotCancellable);
    }

    [Fact]
    public async Task Transient_error_on_save_is_database_busy()
    {
        var booked = await _app.BookAsync(BrakeInspection, Local.Iso(13), "a");
        _app.UnitOfWork.NextSaveException = new TransientDatabaseException("deadlock");

        var result = await Cancel(booked.Appointment.Id);

        result.Error!.Code.Should().Be(ErrorCodes.DatabaseBusy);
    }
}
