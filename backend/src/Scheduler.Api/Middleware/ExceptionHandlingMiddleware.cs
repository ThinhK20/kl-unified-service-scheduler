using Microsoft.AspNetCore.Mvc;
using Scheduler.Api.Http;
using Scheduler.Application.Abstractions;
using Scheduler.Application.Common;
using Scheduler.Domain.Errors;

namespace Scheduler.Api.Middleware;

/// <summary>
/// Last line of defence (Chain of Responsibility): any exception that escapes becomes a ProblemDetails with a code.
/// Unknown exceptions are 500 INTERNAL_ERROR and the details are shown only in Development.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing to answer.
            _logger.LogDebug("Request {Path} was cancelled by the client", context.Request.Path);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogError(ex, "Unhandled exception after the response started for {Path}", context.Request.Path);
                throw;
            }

            await WriteProblemAsync(context, ex);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case DomainException domain:
                _logger.LogWarning(domain, "Domain rule broken: {Code}", domain.Code);
                problem = ApiProblems.From(Error.FromDomain(domain.Error), context);
                break;

            case TransientDatabaseException transient:
                _logger.LogWarning(transient, "Database busy");
                problem = ApiProblems.From(Error.DatabaseBusy(), context);
                break;

            case BadHttpRequestException badRequest:
                _logger.LogInformation(badRequest, "Bad HTTP request");
                problem = ApiProblems.Create(
                    context,
                    badRequest.StatusCode,
                    badRequest.StatusCode == StatusCodes.Status404NotFound ? ErrorCodes.NotFound : ErrorCodes.InvalidRequest,
                    "Invalid request",
                    badRequest.Message);
                break;

            default:
                _logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
                problem = ApiProblems.Create(
                    context,
                    StatusCodes.Status500InternalServerError,
                    ErrorCodes.InternalError,
                    "Internal error",
                    _environment.IsDevelopment() ? exception.ToString() : "An unexpected error occurred.");
                break;
        }

        // Do not clear headers: CORS headers set earlier in the pipeline must stay.
        context.Response.ContentType = ApiProblems.ContentType;
        await ApiProblems.WriteAsync(context, problem);
    }
}
