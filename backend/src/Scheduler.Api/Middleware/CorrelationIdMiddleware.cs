using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Scheduler.Api.Middleware;

/// <summary>
/// Gives every request a correlation id (SPEC section 10). It reads the <c>X-Correlation-Id</c> request header; a
/// missing or unsafe value is replaced by a new id. The id is sent back in the response header, put in a log scope
/// (with the W3C trace id) for the whole request, tagged on the current span, and added to every ProblemDetails.
/// </summary>
public sealed partial class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>Longest id we accept from a client.</summary>
    public const int MaxLength = 100;

    private static readonly object ItemKey = new();

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>The correlation id of this request, or null outside this middleware.</summary>
    public static string? Get(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items.TryGetValue(ItemKey, out var value) ? value as string : null;
    }

    /// <summary>True when a client value can be used as it is: 1 to 100 letters, digits, '.', '_', ':' or '-'.</summary>
    public static bool IsValid(string? value) => value is { Length: > 0 and <= MaxLength } && SafeCharacters().IsMatch(value);

    /// <summary>A new id: 32 lower-case hex characters.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sent = context.Request.Headers[HeaderName];
        var correlationId = sent.Count == 1 && IsValid(sent[0]) ? sent[0]! : NewId();

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        using (_logger.BeginScope(new CorrelationScope(correlationId, Activity.Current?.TraceId.ToHexString())))
        {
            await _next(context);
        }
    }

    /// <summary>The log scope: key/value pairs for JSON logs, and a short text for simple console logs.</summary>
    private sealed class CorrelationScope(string correlationId, string? traceId) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => 2;

        public KeyValuePair<string, object?> this[int index] => index switch
        {
            0 => new("CorrelationId", correlationId),
            1 => new("TraceId", traceId),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            yield return this[0];
            yield return this[1];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"CorrelationId:{correlationId}, TraceId:{traceId}";
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeCharacters();
}
