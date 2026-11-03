using Scheduler.Application.Appointments.Book;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;

namespace Scheduler.Application.Abstractions;

/// <summary>
/// Logs, metrics and traces for booking and cancelling (SPEC section 10). The Application only reports what happened;
/// the API decides how to record it, so this layer needs no logging or OpenTelemetry package.
/// Never pass customer email, phone or VIN here.
/// </summary>
public interface IAppointmentTelemetry
{
    /// <summary>Starts one booking request (a span and a timer). Call <see cref="IBookingAttempt.Finish"/> at the end.</summary>
    IBookingAttempt StartBooking(int dealershipId, int serviceTypeId);

    /// <summary>The database was busy (deadlock or timeout) and the booking is tried one more time.</summary>
    void BookingRetried(Exception exception);

    /// <summary>A cancel request finished with this result.</summary>
    void CancellationFinished(int appointmentId, Result<AppointmentDto> result);
}

/// <summary>One booking request in progress. Disposing it without <see cref="Finish"/> means an unexpected error.</summary>
public interface IBookingAttempt : IDisposable
{
    /// <summary>Records the outcome (see <see cref="BookingOutcomes"/>), the duration and a log line.</summary>
    void Finish(Result<BookAppointmentResult> result);
}

/// <summary>Values of the <c>outcome</c> tag for bookings (metrics and the booking span).</summary>
public static class BookingOutcomes
{
    public const string Created = "created";
    public const string Replayed = "replayed";
    public const string NoAvailability = "no_availability";
    public const string VehicleAlreadyBooked = "vehicle_already_booked";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string RuleViolation = "rule_violation";
    public const string Validation = "validation";
    public const string NotFound = "not_found";
    public const string DatabaseBusy = "database_busy";

    /// <summary>An exception escaped the handler (a bug or broken data). The API answers 500.</summary>
    public const string Error = "error";

    /// <summary>The outcome tag for a finished booking result.</summary>
    public static string From(Result<BookAppointmentResult> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return result.Value.IsReplay ? Replayed : Created;
        }

        return result.Error.Code switch
        {
            ErrorCodes.NoAvailability => NoAvailability,
            ErrorCodes.VehicleAlreadyBooked => VehicleAlreadyBooked,
            ErrorCodes.IdempotencyConflict => IdempotencyConflict,
            ErrorCodes.BookingRuleViolation => RuleViolation,
            ErrorCodes.InvalidRequest => Validation,
            ErrorCodes.NotFound => NotFound,
            ErrorCodes.DatabaseBusy => DatabaseBusy,
            _ => Error,
        };
    }
}

/// <summary>Values of the <c>outcome</c> tag for cancellations.</summary>
public static class CancellationOutcomes
{
    public const string Cancelled = "cancelled";
    public const string NotFound = "not_found";
    public const string NotCancellable = "not_cancellable";
    public const string DatabaseBusy = "database_busy";
    public const string Error = "error";

    /// <summary>The outcome tag for a finished cancel result.</summary>
    public static string From(Result<AppointmentDto> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return Cancelled;
        }

        return result.Error.Code switch
        {
            ErrorCodes.NotFound => NotFound,
            ErrorCodes.AppointmentNotCancellable or ErrorCodes.BookingRuleViolation => NotCancellable,
            ErrorCodes.DatabaseBusy => DatabaseBusy,
            _ => Error,
        };
    }
}
