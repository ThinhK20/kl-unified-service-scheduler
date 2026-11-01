namespace Scheduler.Domain.Errors;

/// <summary>Stable error codes. They match the API error codes in SPEC section 6.</summary>
public static class DomainErrorCodes
{
    /// <summary>Bad input: wrong format, not on the 15-minute grid, in the past, and so on.</summary>
    public const string InvalidRequest = "INVALID_REQUEST";

    /// <summary>The request is valid but breaks a booking rule (opening hours, inactive service, ...).</summary>
    public const string BookingRuleViolation = "BOOKING_RULE_VIOLATION";

    /// <summary>The appointment is not in a state that allows cancelling.</summary>
    public const string AppointmentNotCancellable = "APPOINTMENT_NOT_CANCELLABLE";
}
