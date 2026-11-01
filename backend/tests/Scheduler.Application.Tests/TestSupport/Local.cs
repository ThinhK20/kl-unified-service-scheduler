namespace Scheduler.Application.Tests.TestSupport;

/// <summary>Times on the test day, Tuesday 10 Nov 2026, in Asia/Ho_Chi_Minh (UTC+7).</summary>
internal static class Local
{
    public static readonly DateOnly Day = new(2026, 11, 10);

    /// <summary>The request string for a local time, for example "2026-11-10T13:00:00+07:00".</summary>
    public static string Iso(int hour, int minute = 0) => $"2026-11-10T{hour:00}:{minute:00}:00+07:00";

    /// <summary>The UTC instant of a local time on the test day.</summary>
    public static DateTime Utc(int hour, int minute = 0) =>
        new DateTime(2026, 11, 10, hour, minute, 0, DateTimeKind.Utc).AddHours(-7);
}
