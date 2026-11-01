using Scheduler.Domain.Errors;

namespace Scheduler.Domain.Common;

/// <summary>Looks up time zones by IANA id (for example "Asia/Ho_Chi_Minh").</summary>
public static class TimeZones
{
    /// <summary>Finds the time zone or throws a <see cref="DomainException"/>.</summary>
    public static TimeZoneInfo Find(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            throw new DomainException(DomainErrors.Time.UnknownTimeZone(timeZoneId));
        }
    }
}
