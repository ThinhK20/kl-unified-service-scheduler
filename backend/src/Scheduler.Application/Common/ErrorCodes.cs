using Scheduler.Domain.Errors;

namespace Scheduler.Application.Common;

/// <summary>Stable error codes returned by the API (SPEC section 6).</summary>
public static class ErrorCodes
{
    public const string InvalidRequest = DomainErrorCodes.InvalidRequest;
    public const string NotFound = "NOT_FOUND";
    public const string BookingRuleViolation = DomainErrorCodes.BookingRuleViolation;
    public const string NoAvailability = "NO_AVAILABILITY";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string AppointmentNotCancellable = DomainErrorCodes.AppointmentNotCancellable;
    public const string DatabaseBusy = "DATABASE_BUSY";
    public const string InternalError = "INTERNAL_ERROR";
}
