using Scheduler.Domain.Common;
using Scheduler.Domain.Errors;

namespace Scheduler.Domain.ValueObjects;

/// <summary>
/// The 15-minute booking grid. All slot math lives here: grid checks, rounding and
/// splitting a range into slot start times.
/// </summary>
public static class SlotGrid
{
    /// <summary>Length of one slot in minutes.</summary>
    public const int SlotMinutes = 15;

    /// <summary>Length of one slot.</summary>
    public static readonly TimeSpan SlotLength = TimeSpan.FromMinutes(SlotMinutes);

    /// <summary>True when the time is at :00, :15, :30 or :45 with zero seconds and zero fractions.</summary>
    public static bool IsOnGrid(DateTime time) => time.Ticks % SlotLength.Ticks == 0;

    /// <summary>Rounds a positive number of minutes up to the next multiple of 15 (for example 50 → 60).</summary>
    public static int RoundUpToGrid(int minutes)
    {
        if (minutes <= 0)
        {
            throw new DomainException(DomainErrors.Time.DurationNotPositive);
        }

        var remainder = minutes % SlotMinutes;
        return remainder == 0 ? minutes : checked(minutes + SlotMinutes - remainder);
    }

    /// <summary>
    /// Splits a range into slot start times. A 60-minute range gives 4 starts.
    /// Both ends of the range must be on the grid.
    /// </summary>
    public static IReadOnlyList<DateTime> Split(TimeRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!IsOnGrid(range.StartUtc) || !IsOnGrid(range.EndUtc))
        {
            throw new DomainException(DomainErrors.Time.NotOnGrid);
        }

        var slots = new List<DateTime>((int)(range.Duration.Ticks / SlotLength.Ticks));
        for (var start = range.StartUtc; start < range.EndUtc; start += SlotLength)
        {
            slots.Add(start);
        }

        return slots;
    }

    /// <summary>Throws a <see cref="DomainException"/> when the time is not on the grid.</summary>
    internal static DateTime EnsureOnGrid(DateTime time)
    {
        if (!IsOnGrid(time))
        {
            throw new DomainException(DomainErrors.Time.NotOnGrid);
        }

        return Guard.Utc(time);
    }
}
