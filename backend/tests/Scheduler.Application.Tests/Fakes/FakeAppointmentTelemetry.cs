using Scheduler.Application.Abstractions;
using Scheduler.Application.Appointments.Book;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;

namespace Scheduler.Application.Tests.Fakes;

/// <summary>Remembers what the handlers reported, so tests can check outcomes and retries.</summary>
public sealed class FakeAppointmentTelemetry : IAppointmentTelemetry
{
    public List<(int DealershipId, int ServiceTypeId)> BookingsStarted { get; } = [];

    /// <summary>Outcome of each finished booking ("error" when it was disposed without Finish).</summary>
    public List<string> BookingOutcomes { get; } = [];

    public List<Exception> Retries { get; } = [];

    public List<(int AppointmentId, string Outcome)> Cancellations { get; } = [];

    public IBookingAttempt StartBooking(int dealershipId, int serviceTypeId)
    {
        BookingsStarted.Add((dealershipId, serviceTypeId));
        return new Attempt(this);
    }

    public void BookingRetried(Exception exception) => Retries.Add(exception);

    public void CancellationFinished(int appointmentId, Result<AppointmentDto> result) =>
        Cancellations.Add((appointmentId, CancellationOutcomes.From(result)));

    private sealed class Attempt(FakeAppointmentTelemetry owner) : IBookingAttempt
    {
        private bool _done;

        public void Finish(Result<BookAppointmentResult> result)
        {
            _done = true;
            owner.BookingOutcomes.Add(Abstractions.BookingOutcomes.From(result));
        }

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                owner.BookingOutcomes.Add(Abstractions.BookingOutcomes.Error);
            }
        }
    }
}
