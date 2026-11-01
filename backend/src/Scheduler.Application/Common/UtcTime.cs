namespace Scheduler.Application.Common;

/// <summary>Helpers for UTC <see cref="DateTime"/> values.</summary>
public static class UtcTime
{
    /// <summary>
    /// Marks a time read from the database (Kind = Unspecified) as UTC, so it serializes with a "Z".
    /// </summary>
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime(),
    };
}
