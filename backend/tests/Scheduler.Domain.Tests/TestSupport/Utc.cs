namespace Scheduler.Domain.Tests.TestSupport;

internal static class Utc
{
    /// <summary>A UTC time on 10 Nov 2026.</summary>
    public static DateTime At(int hour, int minute, int second = 0) =>
        new(2026, 11, 10, hour, minute, second, DateTimeKind.Utc);
}
