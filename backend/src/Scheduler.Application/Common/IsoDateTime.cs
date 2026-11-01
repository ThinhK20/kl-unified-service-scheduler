using System.Globalization;
using System.Text.RegularExpressions;

namespace Scheduler.Application.Common;

/// <summary>Strict parsing of the date and time formats the API accepts.</summary>
public static partial class IsoDateTime
{
    /// <summary>
    /// Parses an ISO-8601 date and time that MUST have an offset ("Z" or "+07:00"),
    /// for example "2026-11-10T10:30:00+07:00". Returns the instant in UTC.
    /// </summary>
    public static bool TryParseWithOffsetToUtc(string? value, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(value) || !DateTimeWithOffset().IsMatch(value))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        utc = parsed.UtcDateTime;
        return true;
    }

    /// <summary>Parses a calendar date "YYYY-MM-DD".</summary>
    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?([Zz]|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex DateTimeWithOffset();
}
