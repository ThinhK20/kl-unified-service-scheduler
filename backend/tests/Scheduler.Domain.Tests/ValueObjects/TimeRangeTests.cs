using Scheduler.Domain.Errors;
using Scheduler.Domain.Tests.TestSupport;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Domain.Tests.ValueObjects;

public class TimeRangeTests
{
    private static TimeRange Range(int startHour, int startMinute, int endHour, int endMinute) =>
        TimeRange.Create(Utc.At(startHour, startMinute), Utc.At(endHour, endMinute));

    [Fact]
    public void Create_keeps_start_end_and_duration()
    {
        var range = Range(3, 0, 4, 30);

        range.StartUtc.Should().Be(Utc.At(3, 0));
        range.EndUtc.Should().Be(Utc.At(4, 30));
        range.Duration.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void Create_rejects_end_equal_to_start()
    {
        var act = () => TimeRange.Create(Utc.At(3, 0), Utc.At(3, 0));

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCodes.InvalidRequest);
    }

    [Fact]
    public void Create_rejects_end_before_start()
    {
        var act = () => TimeRange.Create(Utc.At(4, 0), Utc.At(3, 0));

        act.Should().Throw<DomainException>().Which.Error.Should().Be(DomainErrors.Time.EndNotAfterStart);
    }

    [Fact]
    public void Create_rejects_local_times()
    {
        var local = new DateTime(2026, 11, 10, 3, 0, 0, DateTimeKind.Local);

        var act = () => TimeRange.Create(local, local.AddHours(1));

        act.Should().Throw<DomainException>().Which.Error.Should().Be(DomainErrors.Time.NotUtc);
    }

    [Fact]
    public void Create_treats_unspecified_kind_as_utc()
    {
        var unspecified = new DateTime(2026, 11, 10, 3, 0, 0, DateTimeKind.Unspecified);

        var range = TimeRange.Create(unspecified, unspecified.AddHours(1));

        range.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
        range.StartUtc.Should().Be(Utc.At(3, 0));
    }

    [Fact]
    public void FromStart_rejects_zero_duration()
    {
        var act = () => TimeRange.FromStart(Utc.At(3, 0), TimeSpan.Zero);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Touching_ranges_do_not_overlap()
    {
        var first = Range(3, 0, 4, 0);
        var second = Range(4, 0, 5, 0);

        first.Overlaps(second).Should().BeFalse();
        second.Overlaps(first).Should().BeFalse();
    }

    [Fact]
    public void Range_inside_another_overlaps()
    {
        var outer = Range(3, 0, 6, 0);
        var inner = Range(4, 0, 5, 0);

        outer.Overlaps(inner).Should().BeTrue();
        inner.Overlaps(outer).Should().BeTrue();
    }

    [Fact]
    public void Crossing_ranges_overlap()
    {
        var first = Range(3, 0, 4, 30);
        var second = Range(4, 0, 5, 0);

        first.Overlaps(second).Should().BeTrue();
        second.Overlaps(first).Should().BeTrue();
    }

    [Fact]
    public void Identical_ranges_overlap_and_are_equal()
    {
        var first = Range(3, 0, 4, 0);
        var second = Range(3, 0, 4, 0);

        first.Overlaps(second).Should().BeTrue();
        first.Should().Be(second);
    }

    [Fact]
    public void Separate_ranges_do_not_overlap()
    {
        Range(3, 0, 4, 0).Overlaps(Range(5, 0, 6, 0)).Should().BeFalse();
    }

    [Fact]
    public void Contains_range_includes_equal_edges_but_not_outside()
    {
        var outer = Range(3, 0, 6, 0);

        outer.Contains(Range(3, 0, 6, 0)).Should().BeTrue();
        outer.Contains(Range(4, 0, 5, 0)).Should().BeTrue();
        outer.Contains(Range(5, 0, 6, 15)).Should().BeFalse();
        outer.Contains(Range(2, 45, 4, 0)).Should().BeFalse();
    }

    [Fact]
    public void Contains_instant_is_start_inclusive_end_exclusive()
    {
        var range = Range(3, 0, 4, 0);

        range.Contains(Utc.At(3, 0)).Should().BeTrue();
        range.Contains(Utc.At(3, 59, 59)).Should().BeTrue();
        range.Contains(Utc.At(4, 0)).Should().BeFalse();
    }
}
