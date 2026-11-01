using System.Diagnostics.CodeAnalysis;
using Scheduler.Domain.Errors;

namespace Scheduler.Domain.Common;

/// <summary>Looks up time zones by IANA id (for example "Asia/Ho_Chi_Minh").</summary>
public static class TimeZones
{
    /// <summary>Finds the time zone or throws a <see cref="DomainException"/> (for input checks).</summary>
    public static TimeZoneInfo Find(string timeZoneId) =>
        TryFind(timeZoneId, out var timeZone)
            ? timeZone
            : throw new DomainException(DomainErrors.Time.UnknownTimeZone(timeZoneId));

    /// <summary>Finds the time zone; false when the id is unknown or invalid.</summary>
    public static bool TryFind(string? timeZoneId, [NotNullWhen(true)] out TimeZoneInfo? timeZone)
    {
        timeZone = null;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return false;
        }
    }
}
