using Scheduler.Domain.Common;
using Scheduler.Domain.Errors;

namespace Scheduler.Domain.ValueObjects;

/// <summary>
/// A half-open UTC time range [StartUtc, EndUtc). EndUtc is always after StartUtc.
/// Two ranges that only touch (one ends when the other starts) do not overlap.
/// </summary>
public sealed record TimeRange
{
    private TimeRange(DateTime startUtc, DateTime endUtc)
    {
        StartUtc = startUtc;
        EndUtc = endUtc;
    }

    /// <summary>Inclusive start, UTC.</summary>
    public DateTime StartUtc { get; }

    /// <summary>Exclusive end, UTC.</summary>
    public DateTime EndUtc { get; }

    /// <summary>Length of the range.</summary>
    public TimeSpan Duration => EndUtc - StartUtc;

    /// <summary>Creates a range. Throws a <see cref="DomainException"/> if the end is not after the start.</summary>
    public static TimeRange Create(DateTime startUtc, DateTime endUtc)
    {
        var start = Guard.Utc(startUtc);
        var end = Guard.Utc(endUtc);
        if (end <= start)
        {
            throw new DomainException(DomainErrors.Time.EndNotAfterStart);
        }

        return new TimeRange(start, end);
    }

    /// <summary>Creates a range from a start and a length.</summary>
    public static TimeRange FromStart(DateTime startUtc, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new DomainException(DomainErrors.Time.DurationNotPositive);
        }

        var start = Guard.Utc(startUtc);
        return Create(start, start + duration);
    }

    /// <summary>True when the two ranges share at least one instant. Touching ranges do not overlap.</summary>
    public bool Overlaps(TimeRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return StartUtc < other.EndUtc && other.StartUtc < EndUtc;
    }

    /// <summary>True when <paramref name="other"/> is fully inside this range.</summary>
    public bool Contains(TimeRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return StartUtc <= other.StartUtc && other.EndUtc <= EndUtc;
    }

    /// <summary>True when the instant is inside the range (start inclusive, end exclusive).</summary>
    public bool Contains(DateTime instantUtc)
    {
        var instant = Guard.Utc(instantUtc);
        return StartUtc <= instant && instant < EndUtc;
    }

    public override string ToString() => $"[{StartUtc:O}, {EndUtc:O})";
}
