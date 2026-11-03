using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Scheduler.Application.Common;

namespace Scheduler.Api.Http;

/// <summary>
/// The one place that turns an <see cref="Error"/> into an RFC 7807 ProblemDetails response (SPEC section 6).
/// Extension members: <c>code</c>, <c>traceId</c>, <c>correlationId</c> (SPEC section 10), and when present
/// <c>errors</c> and <c>suggestedStartTimes</c>.
/// </summary>
public static class ApiProblems
{
    public const string ContentType = "application/problem+json";

    private static readonly Dictionary<string, (int Status, string Title)> ByCode = new(StringComparer.Ordinal)
    {
        [ErrorCodes.InvalidRequest] = (StatusCodes.Status400BadRequest, "Invalid request"),
        [ErrorCodes.NotFound] = (StatusCodes.Status404NotFound, "Not found"),
        [ErrorCodes.BookingRuleViolation] = (StatusCodes.Status422UnprocessableEntity, "Booking rule violation"),
        [ErrorCodes.NoAvailability] = (StatusCodes.Status409Conflict, "No availability"),
        [ErrorCodes.IdempotencyConflict] = (StatusCodes.Status409Conflict, "Idempotency conflict"),
        [ErrorCodes.VehicleAlreadyBooked] = (StatusCodes.Status409Conflict, "Vehicle already booked"),
        [ErrorCodes.AppointmentNotCancellable] = (StatusCodes.Status409Conflict, "Appointment cannot be cancelled"),
        [ErrorCodes.DatabaseBusy] = (StatusCodes.Status503ServiceUnavailable, "Database busy"),
        [ErrorCodes.InternalError] = (StatusCodes.Status500InternalServerError, "Internal error"),
    };

    private static readonly Dictionary<int, string> TypeByStatus = new()
    {
        [400] = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        [404] = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        [405] = "https://tools.ietf.org/html/rfc9110#section-15.5.6",
        [406] = "https://tools.ietf.org/html/rfc9110#section-15.5.7",
        [409] = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        [415] = "https://tools.ietf.org/html/rfc9110#section-15.5.16",
        [422] = "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        [500] = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        [503] = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
    };

    /// <summary>HTTP status for an error code. Unknown codes (bugs) are 500.</summary>
    public static int StatusFor(string code) =>
        ByCode.TryGetValue(code, out var entry) ? entry.Status : StatusCodes.Status500InternalServerError;

    /// <summary>Builds the ProblemDetails for an application error.</summary>
    public static ProblemDetails From(Error error, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(httpContext);

        var known = ByCode.TryGetValue(error.Code, out var entry);
        var code = known ? error.Code : ErrorCodes.InternalError;
        var status = known ? entry.Status : StatusCodes.Status500InternalServerError;
        var title = known ? entry.Title : "Internal error";
        var detail = known ? error.Message : "An unexpected error occurred.";

        var problem = Create(httpContext, status, code, title, detail);
        if (error.FieldErrors.Count > 0)
        {
            problem.Extensions["errors"] = error.FieldErrors;
        }

        if (error.Code == ErrorCodes.NoAvailability)
        {
            problem.Extensions["suggestedStartTimes"] = error.SuggestedStartTimes
                .Order()
                .Select(FormatUtc)
                .ToArray();
        }

        return problem;
    }

    /// <summary>Builds a ProblemDetails with the standard extension members.</summary>
    public static ProblemDetails Create(HttpContext httpContext, int status, string code, string title, string? detail)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var problem = new ProblemDetails
        {
            Type = TypeByStatus.GetValueOrDefault(status, "about:blank"),
            Title = title,
            Status = status,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        if (Middleware.CorrelationIdMiddleware.Get(httpContext) is { } correlationId)
        {
            problem.Extensions["correlationId"] = correlationId;
        }
        return problem;
    }

    /// <summary>MVC result for an application error.</summary>
    public static ObjectResult ToActionResult(Error error, HttpContext httpContext)
    {
        var problem = From(error, httpContext);
        if (problem.Status == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter = "1";
        }

        var result = new ObjectResult(problem) { StatusCode = problem.Status };
        result.ContentTypes.Add(ContentType);
        return result;
    }

    /// <summary>Writes a ProblemDetails directly to the response (used by middleware).</summary>
    public static Task WriteAsync(HttpContext httpContext, ProblemDetails problem)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(problem);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        if (httpContext.Response.StatusCode == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter = "1";
        }

        return httpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, ContentType);
    }

    private static string FormatUtc(DateTime value) =>
        UtcTime.Normalize(value).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
