using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Scheduler.Application.Common;

namespace Scheduler.Api.Http;

/// <summary>Model binding errors (bad JSON, wrong types, missing body) → 400 INVALID_REQUEST.</summary>
public static class ModelStateProblems
{
    public static IActionResult Create(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var fields = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => ToFieldName(entry.Key),
                entry => entry.Value!.Errors.Select(Message).ToArray());

        var error = Error.Validation("The request could not be read. Check the JSON body and the parameters.", fields);
        return ApiProblems.ToActionResult(error, context.HttpContext);
    }

    // "$.startTime" → "startTime"; "" (whole body) → "body".
    private static string ToFieldName(string key)
    {
        var name = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        return string.IsNullOrEmpty(name) || name == "$" ? "body" : name;
    }

    private static string Message(ModelError error) =>
        string.IsNullOrWhiteSpace(error.ErrorMessage) ? "The value is not valid." : error.ErrorMessage;
}
