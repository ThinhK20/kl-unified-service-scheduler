using Scheduler.Domain.Errors;

namespace Scheduler.Application.Common;

/// <summary>
/// A failure of a use case: a stable <see cref="Code"/>, a plain <see cref="Message"/>,
/// and optional extra data that the API puts into ProblemDetails.
/// </summary>
public sealed record Error(string Code, string Message)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoFieldErrors =
        new Dictionary<string, string[]>();

    /// <summary>Field name → messages (ProblemDetails <c>errors</c>). Empty when not a field error.</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; init; } = NoFieldErrors;

    /// <summary>Free start times (UTC) close to the requested one. Used with <see cref="ErrorCodes.NoAvailability"/>.</summary>
    public IReadOnlyList<DateTime> SuggestedStartTimes { get; init; } = [];

    public static Error Validation(string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(ErrorCodes.InvalidRequest, message) { FieldErrors = fieldErrors ?? NoFieldErrors };

    public static Error NotFound(string entityName, object id) =>
        new(ErrorCodes.NotFound, $"{entityName} {id} was not found.");

    public static Error BookingRuleViolation(string message) =>
        new(ErrorCodes.BookingRuleViolation, message);

    public static Error NoAvailability(IReadOnlyList<DateTime> suggestedStartTimes) =>
        new(ErrorCodes.NoAvailability, "No technician and bay are free for the whole time you picked.")
        {
            SuggestedStartTimes = suggestedStartTimes,
        };

    public static Error IdempotencyConflict() =>
        new(ErrorCodes.IdempotencyConflict, "This Idempotency-Key was already used with a different request body.");

    public static Error DatabaseBusy() =>
        new(ErrorCodes.DatabaseBusy, "The system is busy right now. Please try again.");

    /// <summary>Turns a broken domain rule into an application error with the same code and message.</summary>
    public static Error FromDomain(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Error(error.Code, error.Message);
    }
}
