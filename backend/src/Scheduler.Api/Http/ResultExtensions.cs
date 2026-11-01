using Microsoft.AspNetCore.Mvc;
using Scheduler.Application.Common;

namespace Scheduler.Api.Http;

/// <summary>Turns a use case <see cref="Result{T}"/> into an HTTP response.</summary>
public static class ResultExtensions
{
    /// <summary>Success → <paramref name="onSuccess"/>; failure → ProblemDetails with the mapped status.</summary>
    public static IActionResult ToActionResult<T>(this ControllerBase controller, Result<T> result, Func<T, IActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        return result.IsSuccess
            ? onSuccess(result.Value)
            : ApiProblems.ToActionResult(result.Error, controller.HttpContext);
    }
}
