using Scheduler.Domain.Entities;
using Scheduler.Domain.Errors;
using Scheduler.Domain.Tests.TestSupport;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Domain.Tests.Entities;

/// <summary>District 1 is open 08:00-17:00 local (UTC+7), which is 01:00-10:00 UTC.</summary>
public class DealershipTests
{
    private readonly Dealership _dealership = Seed.District1();

    [Fact]
    public void Range_inside_opening_hours_is_open()
    {
        // 10:30-11:30 local
        _dealership.IsOpenFor(TimeRange.Create(Utc.At(3, 30), Utc.At(4, 30))).Should().BeTrue();
    }

    [Fact]
    public void Range_starting_exactly_at_open_is_allowed()
    {
        // 08:00-09:00 local
        _dealership.IsOpenFor(TimeRange.Create(Utc.At(1, 0), Utc.At(2, 0))).Should().BeTrue();
    }

    [Fact]
    public void Range_ending_exactly_at_close_is_allowed()
    {
        // 16:00-17:00 local
        _dealership.IsOpenFor(TimeRange.Create(Utc.At(9, 0), Utc.At(10, 0))).Should().BeTrue();
    }

    [Fact]
    public void Range_ending_one_minute_after_close_is_rejected()
    {
        // 16:01-17:01 local
        _dealership.IsOpenFor(TimeRange.Create(Utc.At(9, 1), Utc.At(10, 1))).Should().BeFalse();
    }

    [Fact]
    public void Range_starting_before_open_is_rejected()
    {
        // 07:45-08:45 local
        _dealership.IsOpenFor(TimeRange.Create(Utc.At(0, 45), Utc.At(1, 45))).Should().BeFalse();
    }

    [Fact]
    public void Range_that_is_inside_hours_in_utc_but_not_in_local_time_is_rejected()
    {
        // 12:00-13:00 reads as "open" if taken as local, but in UTC it is 19:00-20:00 local: closed.
        _dealership.IsOpenFor(TimeRange.Create(Utc.At(12, 0), Utc.At(13, 0))).Should().BeFalse();
    }

    [Fact]
    public void Range_crossing_local_midnight_is_rejected()
    {
        var start = new DateTime(2026, 11, 10, 16, 0, 0, DateTimeKind.Utc); // 23:00 local
        _dealership.IsOpenFor(TimeRange.FromStart(start, TimeSpan.FromHours(2))).Should().BeFalse();
    }

    [Fact]
    public void OpeningHoursUtc_converts_the_local_day_to_utc()
    {
        var hours = _dealership.OpeningHoursUtc(new DateOnly(2026, 11, 10));

        hours.Should().Be(TimeRange.Create(Utc.At(1, 0), Utc.At(10, 0)));
    }

    [Fact]
    public void ToLocal_uses_the_dealership_time_zone()
    {
        _dealership.ToLocal(Utc.At(3, 30)).Should().Be(new DateTime(2026, 11, 10, 10, 30, 0));
    }

    [Fact]
    public void Create_rejects_unknown_time_zone()
    {
        var act = () => Dealership.Create("X", "Not/AZone", new TimeOnly(8, 0), new TimeOnly(17, 0));

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCodes.InvalidRequest);
    }

    [Fact]
    public void Unknown_stored_time_zone_is_broken_data_not_a_client_error()
    {
        var dealership = Seed.District1();
        typeof(Dealership).GetProperty(nameof(Dealership.TimeZoneId))!.SetValue(dealership, "Mars/Olympus_Mons");

        var act = () => dealership.IsOpenFor(TimeRange.Create(Utc.At(3, 0), Utc.At(4, 0)));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Mars/Olympus_Mons*");
    }

    [Fact]
    public void Create_rejects_close_not_after_open()
    {
        var act = () => Dealership.Create("X", Seed.HoChiMinh, new TimeOnly(17, 0), new TimeOnly(8, 0));

        act.Should().Throw<DomainException>().Which.Error.Should().Be(DomainErrors.Time.WindowEndNotAfterStart);
    }

    [Fact]
    public void Create_makes_an_active_dealership()
    {
        _dealership.IsActive.Should().BeTrue();
        _dealership.TimeZoneId.Should().Be(Seed.HoChiMinh);
    }
}
