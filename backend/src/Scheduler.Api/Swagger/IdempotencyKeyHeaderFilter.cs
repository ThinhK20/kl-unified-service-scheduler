using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Scheduler.Api.Swagger;

/// <summary>Marks the Idempotency-Key header as required in the Swagger document.</summary>
public sealed class IdempotencyKeyHeaderFilter : IOperationFilter
{
    public const string HeaderName = "Idempotency-Key";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var header = operation.Parameters?.FirstOrDefault(p => p.In == ParameterLocation.Header && p.Name == HeaderName);
        if (header is null)
        {
            return;
        }

        header.Required = true;
        header.Description = "A new random value (for example a UUID) per booking attempt, max 100 characters. "
            + "Send the same value again to retry safely: you get the same response and no second booking.";
        header.Schema ??= new OpenApiSchema();
        header.Schema.MaxLength = 100;
    }
}
