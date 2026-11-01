using Microsoft.AspNetCore.Diagnostics;
using Scheduler.Api.Http;
using Scheduler.Application.Common;

namespace Scheduler.Api.Middleware;

/// <summary>
/// Fills empty error responses (unknown route 404, 405, 415, ...) with a ProblemDetails that has a code,
/// so every non-2xx answer from /api has the same shape.
/// </summary>
public static class StatusCodeProblems
{
    public static Task WriteAsync(StatusCodeContext statusCodeContext)
    {
        ArgumentNullException.ThrowIfNull(statusCodeContext);
        var context = statusCodeContext.HttpContext;
        var status = context.Response.StatusCode;

        var (code, title) = status switch
        {
            StatusCodes.Status404NotFound => (ErrorCodes.NotFound, "Not found"),
            >= 500 => (ErrorCodes.InternalError, "Internal error"),
            _ => (ErrorCodes.InvalidRequest, "Invalid request"),
        };

        var detail = status switch
        {
            StatusCodes.Status404NotFound => $"No resource at {context.Request.Path}.",
            StatusCodes.Status405MethodNotAllowed => $"{context.Request.Method} is not allowed here.",
            StatusCodes.Status415UnsupportedMediaType => "Send the body as application/json.",
            _ => null,
        };

        var problem = ApiProblems.Create(context, status, code, title, detail);
        return ApiProblems.WriteAsync(context, problem);
    }
}
