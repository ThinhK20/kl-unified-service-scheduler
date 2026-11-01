namespace Scheduler.Domain.Errors;

/// <summary>All domain errors in one place, so codes and messages stay consistent.</summary>
public static class DomainErrors
{
    public static DomainError Invalid(string message) => new(DomainErrorCodes.InvalidRequest, message);

    public static DomainError RuleViolation(string message) => new(DomainErrorCodes.BookingRuleViolation, message);

    public static class Time
    {
        public static readonly DomainError NotUtc =
            Invalid("Time must be in UTC.");

        public static readonly DomainError EndNotAfterStart =
            Invalid("End time must be after start time.");

        public static readonly DomainError NotOnGrid =
            Invalid("Time must be on the 15-minute grid (:00, :15, :30 or :45, with zero seconds).");

        public static readonly DomainError DurationNotPositive =
            Invalid("Duration must be more than zero minutes.");

        public static readonly DomainError StartNotInFuture =
            Invalid("Start time must be in the future.");

        public static DomainError UnknownTimeZone(string timeZoneId) =>
            Invalid($"Unknown time zone '{timeZoneId}'.");

        public static readonly DomainError WindowEndNotAfterStart =
            Invalid("Daily closing (or shift end) time must be after opening (or shift start) time.");
    }

    public static class Appointment
    {
        public static DomainError NotCancellable(Enums.AppointmentStatus status) =>
            new(DomainErrorCodes.AppointmentNotCancellable,
                $"Only confirmed appointments can be cancelled. This appointment is {status}.");

        public static readonly DomainError AlreadyStarted =
            new(DomainErrorCodes.AppointmentNotCancellable, "This appointment has already started or passed.");

        public static readonly DomainError VehicleNotOwnedByCustomer =
            RuleViolation("The vehicle does not belong to the customer.");

        public static readonly DomainError DealershipInactive =
            RuleViolation("The dealership is not active.");

        public static readonly DomainError ServiceTypeInactive =
            RuleViolation("The service type is not active.");

        public static readonly DomainError ServiceNotOfferedAtDealership =
            RuleViolation("The service type is not offered at this dealership.");

        public static readonly DomainError OutsideOpeningHours =
            RuleViolation("The appointment must be inside the dealership opening hours.");

        public static readonly DomainError TechnicianNotUsable =
            RuleViolation("The technician is not active, not at this dealership, not qualified, or not on shift for the whole time.");

        public static readonly DomainError BayNotUsable =
            RuleViolation("The service bay is not active, not at this dealership, or not the required type.");
    }

    public static class Entity
    {
        public static DomainError Required(string field) => Invalid($"{field} is required.");

        public static DomainError MustBePositive(string field) => Invalid($"{field} must be more than zero.");
    }
}
