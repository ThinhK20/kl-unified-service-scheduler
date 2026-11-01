namespace Scheduler.Application.Common;

/// <summary>
/// TECHNICAL bound for dates sent by clients, not a business booking horizon. Outside these years the time math
/// (adding a duration, converting a local day to UTC) can overflow <see cref="DateTime"/> and crash with a 500.
/// Requests outside the bound get 400 INVALID_REQUEST instead.
/// </summary>
public static class SupportedDates
{
    public const int MinYear = 2000;
    public const int MaxYear = 9998;

    public static string Message(string field) => $"{field} must be between the years {MinYear} and {MaxYear}.";

    public static bool Contains(DateTime value) => value.Year is >= MinYear and <= MaxYear;

    public static bool Contains(DateOnly value) => value.Year is >= MinYear and <= MaxYear;
}
