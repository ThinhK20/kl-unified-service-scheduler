using System.Diagnostics;
using Scheduler.Application.Abstractions;
using Scheduler.Application.Appointments.Book;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;

namespace Scheduler.Api.Observability;

/// <summary>
/// Records booking and cancel requests as logs, metrics and a span (SPEC section 10).
/// Log lines hold only ids, codes and durations: never customer email, phone or VIN.
/// </summary>
public sealed class AppointmentTelemetry : IAppointmentTelemetry
{
    /// <summary>Log category of the booking and cancel log lines.</summary>
    public const string LogCategory = "Scheduler.Appointments";

    /// <summary>Name of the span around one booking request.</summary>
    public const string BookingSpanName = "Book appointment";

    private readonly SchedulerInstruments _instruments;
    private readonly ILogger _logger;

    public AppointmentTelemetry(SchedulerInstruments instruments, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _instruments = instruments;
        _logger = loggerFactory.CreateLogger(LogCategory);
    }

    public IBookingAttempt StartBooking(int dealershipId, int serviceTypeId)
    {
        var activity = SchedulerInstruments.ActivitySource.StartActivity(BookingSpanName, ActivityKind.Internal);
        activity?.SetTag("dealership.id", dealershipId);
        activity?.SetTag("service_type.id", serviceTypeId);
        return new BookingAttempt(this, activity, dealershipId, serviceTypeId);
    }

    public void BookingRetried(Exception exception)
    {
        _instruments.DatabaseRetries.Add(1, new KeyValuePair<string, object?>("operation", "book"));
        _logger.LogWarning(exception, "The database was busy during a booking. Trying the booking one more time.");
    }

    public void CancellationFinished(int appointmentId, Result<AppointmentDto> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var outcome = CancellationOutcomes.From(result);
        _instruments.Cancellations.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

        switch (outcome)
        {
            case CancellationOutcomes.Cancelled:
                _logger.LogInformation("Cancelled appointment {AppointmentId}", appointmentId);
                break;
            case CancellationOutcomes.DatabaseBusy:
                _logger.LogError("Could not cancel appointment {AppointmentId}: {Code}, the database is busy", appointmentId, result.Error!.Code);
                break;
            default:
                _logger.LogInformation("Cancel of appointment {AppointmentId} was refused: {Code}", appointmentId, result.Error!.Code);
                break;
        }
    }

    private void RecordBooking(Activity? activity, int dealershipId, int serviceTypeId, long startTimestamp, Result<BookAppointmentResult>? result)
    {
        var outcome = result is null ? BookingOutcomes.Error : BookingOutcomes.From(result);
        var durationMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        var tag = new KeyValuePair<string, object?>("outcome", outcome);
        _instruments.Bookings.Add(1, tag);
        _instruments.BookingDuration.Record(durationMs, tag);

        activity?.SetTag("booking.outcome", outcome);
        if (outcome is BookingOutcomes.DatabaseBusy or BookingOutcomes.Error)
        {
            activity?.SetStatus(ActivityStatusCode.Error, outcome);
        }

        var elapsed = (long)Math.Round(durationMs);
        switch (outcome)
        {
            case BookingOutcomes.Created:
                var appointment = result!.Value.Appointment;
                activity?.SetTag("appointment.id", appointment.Id);
                _logger.LogInformation(
                    "Booked appointment {AppointmentId} at dealership {DealershipId} for service type {ServiceTypeId} with technician {TechnicianId} in bay {ServiceBayId} in {DurationMs} ms",
                    appointment.Id, dealershipId, serviceTypeId, appointment.Technician.Id, appointment.ServiceBay.Id, elapsed);
                break;
            case BookingOutcomes.Replayed:
                _logger.LogInformation(
                    "Booking replayed from its Idempotency-Key: returned the saved appointment {AppointmentId} in {DurationMs} ms",
                    result!.Value.Appointment.Id, elapsed);
                break;
            case BookingOutcomes.DatabaseBusy:
                _logger.LogError(
                    "Booking failed with {Code}: the database was still busy after one retry (dealership {DealershipId}, service type {ServiceTypeId}, {DurationMs} ms)",
                    result!.Error!.Code, dealershipId, serviceTypeId, elapsed);
                break;
            case BookingOutcomes.Error:
                // The exception middleware logs the exception at Error; here we only count it.
                break;
            default:
                _logger.LogInformation(
                    "Booking was refused with {Code} (dealership {DealershipId}, service type {ServiceTypeId}, {DurationMs} ms)",
                    result!.Error!.Code, dealershipId, serviceTypeId, elapsed);
                break;
        }
    }

    private sealed class BookingAttempt(AppointmentTelemetry owner, Activity? activity, int dealershipId, int serviceTypeId) : IBookingAttempt
    {
        private readonly long _startTimestamp = Stopwatch.GetTimestamp();
        private bool _recorded;

        public void Finish(Result<BookAppointmentResult> result)
        {
            ArgumentNullException.ThrowIfNull(result);
            Record(result);
        }

        public void Dispose()
        {
            // No Finish means an exception left the handler.
            Record(null);
            activity?.Dispose();
        }

        private void Record(Result<BookAppointmentResult>? result)
        {
            if (_recorded)
            {
                return;
            }

            _recorded = true;
            owner.RecordBooking(activity, dealershipId, serviceTypeId, _startTimestamp, result);
        }
    }
}
