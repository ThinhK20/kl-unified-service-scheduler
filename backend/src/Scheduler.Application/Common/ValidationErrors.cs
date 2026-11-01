using FluentValidation.Results;

namespace Scheduler.Application.Common;

/// <summary>Turns a FluentValidation result into an <see cref="ErrorCodes.InvalidRequest"/> error.</summary>
public static class ValidationErrors
{
    public static Error ToError(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var fields = result.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray());
        var message = result.Errors.Count == 1
            ? result.Errors[0].ErrorMessage
            : "The request is not valid. See the field errors.";
        return Error.Validation(message, fields);
    }
}
