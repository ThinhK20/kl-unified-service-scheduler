using Scheduler.Application.Allocation;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Appointments.Queries;
using Scheduler.Application.Common;
using Scheduler.Application.Tests.TestSupport;

namespace Scheduler.Application.Tests.Appointments;

/// <summary>SPEC section 6: availability <c>times</c> with a status, and the days endpoint.</summary>
public class AvailabilityStatusTests
{
    private const int OilChange = 1;
    private const int BrakeInspection = 2; // 90 min, needs the only lift bay (Bay 2)
    private const int OilChangeThuDuc = 5;
    private readonly TestApp _app = new(); // now = Mon 9 Nov 2026 14:00 local

    private static DateTime Utc(DateOnly day, int hour, int minute = 0) =>
        DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(hour, minute)).AddHours(-7), DateTimeKind.Utc);

    private static string Iso(DateOnly day, int hour, int minute = 0) => $"{day:yyyy-MM-dd}T{hour:00}:{minute:00}:00+07:00";

    private async Task<AvailabilityDto> DayAsync(int serviceTypeId, DateOnly day, string? vehicleId = null)
    {
        var result = await _app.AvailabilityHandler().HandleAsync(
            new GetAvailabilityQuery(1, serviceTypeId, day.ToString("yyyy-MM-dd"), vehicleId), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private Task<Result<AvailabilityDaysDto>> DaysAsync(int serviceTypeId, string? from, string? to, string? vehicleId = null, int dealershipId = 1) =>
        _app.AvailabilityDaysHandler().HandleAsync(new GetAvailabilityDaysQuery(dealershipId, serviceTypeId, from, to, vehicleId), CancellationToken.None);

    private static IEnumerable<DateTime> StartsWith(AvailabilityDto dto, StartTimeStatus status) =>
        dto.Times.Where(t => t.Status == status).Select(t => t.StartUtc);

    private static void SlotsAreTheAvailableTimes(AvailabilityDto dto) =>
        dto.Slots.Should().Equal(dto.Times.Where(t => t.Status == StartTimeStatus.Available).Select(t => new TimeSlotDto(t.StartUtc, t.EndUtc)));

    private static IEnumerable<DateTime> Range(DateOnly day, int fromHour, int fromMinute, int toHour, int toMinute)
    {
        for (var t = Utc(day, fromHour, fromMinute); t <= Utc(day, toHour, toMinute); t = t.AddMinutes(15))
        {
            yield return t;
        }
    }

    // ---- Day endpoint: times ----

    [Fact]
    public async Task Times_are_every_grid_start_where_the_job_fits_ascending_and_all_available_on_a_free_day()
    {
        var dto = await DayAsync(BrakeInspection, Local.Day);

        dto.Times.Should().HaveCount(31); // 08:00 ... 15:30
        dto.Times.Select(t => t.StartUtc).Should().Equal(Range(Local.Day, 8, 0, 15, 30));
        dto.Times.Should().OnlyContain(t => t.EndUtc == t.StartUtc.AddMinutes(90) && t.Status == StartTimeStatus.Available);
        SlotsAreTheAvailableTimes(dto);
    }

    [Fact]
    public async Task Starts_at_or_before_now_are_past()
    {
        _app.Clock.UtcNow = Local.Utc(14, 15); // exactly a grid start: booking needs start > now

        var dto = await DayAsync(BrakeInspection, Local.Day);

        StartsWith(dto, StartTimeStatus.Past).Should().Equal(Range(Local.Day, 8, 0, 14, 15));
        StartsWith(dto, StartTimeStatus.Available).Should().Equal(Range(Local.Day, 14, 30, 15, 30));
        SlotsAreTheAvailableTimes(dto);
    }

    [Fact]
    public async Task Starts_with_no_free_pair_are_booked()
    {
        await _app.BookAsync(BrakeInspection, Local.Iso(13), "a"); // Bay 2 busy 13:00-14:30

        var dto = await DayAsync(BrakeInspection, Local.Day);

        StartsWith(dto, StartTimeStatus.Booked).Should().Equal(Range(Local.Day, 11, 45, 14, 15));
        StartsWith(dto, StartTimeStatus.VehicleBooked).Should().BeEmpty("no vehicleId was given");
        dto.Times.Should().HaveCount(31);
        SlotsAreTheAvailableTimes(dto);
    }

    [Fact]
    public async Task Starts_when_the_car_is_booked_elsewhere_are_vehicle_booked_only_for_that_car()
    {
        // Vehicle 2 has an oil change at the OTHER dealership 10:00-11:00.
        await _app.BookAsync(OilChangeThuDuc, Local.Iso(10), "thu-duc", vehicleId: 2, dealershipId: 2);

        var forCar2 = await DayAsync(BrakeInspection, Local.Day, "2");
        var forCar1 = await DayAsync(BrakeInspection, Local.Day, "1");
        var ignoringCar = await DayAsync(BrakeInspection, Local.Day);

        // [s, s+90) holds a 10:00-11:00 slot for s in 08:45 ... 10:45; 08:30 and 11:00 only touch.
        StartsWith(forCar2, StartTimeStatus.VehicleBooked).Should().Equal(Range(Local.Day, 8, 45, 10, 45));
        StartsWith(forCar2, StartTimeStatus.Available).Should().Contain(Utc(Local.Day, 8, 30)).And.Contain(Utc(Local.Day, 11));
        forCar2.Slots.Should().HaveCount(31 - 9);
        SlotsAreTheAvailableTimes(forCar2);

        forCar1.Times.Should().OnlyContain(t => t.Status == StartTimeStatus.Available);
        ignoringCar.Times.Should().OnlyContain(t => t.Status == StartTimeStatus.Available);
    }

    [Fact]
    public async Task Vehicle_booked_wins_over_booked_and_past_wins_over_both()
    {
        await _app.BookAsync(BrakeInspection, Local.Iso(13), "a"); // car 1 AND Bay 2 busy 13:00-14:30
        _app.Clock.UtcNow = Local.Utc(12);

        var dto = await DayAsync(BrakeInspection, Local.Day, "1");

        StartsWith(dto, StartTimeStatus.Past).Should().Equal(Range(Local.Day, 8, 0, 12, 0));
        StartsWith(dto, StartTimeStatus.VehicleBooked).Should().Equal(Range(Local.Day, 12, 15, 14, 15));
        StartsWith(dto, StartTimeStatus.Booked).Should().BeEmpty();
        StartsWith(dto, StartTimeStatus.Available).Should().Equal(Range(Local.Day, 14, 30, 15, 30));
        SlotsAreTheAvailableTimes(dto);
    }

    [Fact]
    public async Task Without_vehicle_id_slots_are_the_old_free_ranges()
    {
        await _app.BookAsync(BrakeInspection, Local.Iso(13), "a");
        _app.Clock.UtcNow = Local.Utc(9, 10);

        var dto = await DayAsync(BrakeInspection, Local.Day);

        // Old rule: future starts (after 09:10) with a free pair; Bay 2 is busy 13:00-14:30, so 11:45 ... 14:15 are gone.
        var expectedStarts = new[]
        {
            Local.Utc(9, 15), Local.Utc(9, 30), Local.Utc(9, 45), Local.Utc(10), Local.Utc(10, 15),
            Local.Utc(10, 30), Local.Utc(10, 45), Local.Utc(11), Local.Utc(11, 15), Local.Utc(11, 30),
            Local.Utc(14, 30), Local.Utc(14, 45), Local.Utc(15), Local.Utc(15, 15), Local.Utc(15, 30),
        };
        dto.Slots.Should().Equal(expectedStarts.Select(start => new TimeSlotDto(start, start.AddMinutes(90))));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.5")]
    [InlineData(" 1")]
    [InlineData("+1")]
    [InlineData("99999999999")]
    public async Task Bad_vehicle_id_is_invalid(string vehicleId)
    {
        var result = await _app.AvailabilityHandler().HandleAsync(
            new GetAvailabilityQuery(1, OilChange, "2026-11-10", vehicleId), CancellationToken.None);

        result.Error!.Code.Should().Be(ErrorCodes.InvalidRequest);
        result.Error.FieldErrors.Should().ContainKey("vehicleId").WhoseValue.Should().Equal("vehicleId must be a positive number.");
    }

    [Fact]
    public async Task Unknown_vehicle_is_not_found()
    {
        var result = await _app.AvailabilityHandler().HandleAsync(
            new GetAvailabilityQuery(1, OilChange, "2026-11-10", "999"), CancellationToken.None);

        result.Error!.Code.Should().Be(ErrorCodes.NotFound);
        result.Error.Message.Should().Be("Vehicle 999 was not found.");
    }

    [Fact]
    public async Task Vehicle_of_another_customer_is_allowed_there_is_no_ownership_check()
    {
        var dto = await DayAsync(OilChange, Local.Day, "3");

        dto.Slots.Should().HaveCount(33);
    }

    // ---- Days endpoint ----

    [Fact]
    public async Task Days_are_past_before_today_and_count_only_future_starts_today()
    {
        var result = await DaysAsync(BrakeInspection, "2026-11-08", "2026-11-10");

        var dto = result.Value;
        dto.From.Should().Be("2026-11-08");
        dto.To.Should().Be("2026-11-10");
        dto.TimeZoneId.Should().Be("Asia/Ho_Chi_Minh");
        dto.DurationMinutes.Should().Be(90);
        dto.Days.Should().Equal(
            new AvailabilityDayDto("2026-11-08", 0, DayAvailabilityStatus.Past),
            new AvailabilityDayDto("2026-11-09", 6, DayAvailabilityStatus.Available), // 14:15 ... 15:30 (now is 14:00)
            new AvailabilityDayDto("2026-11-10", 31, DayAvailabilityStatus.Available));
    }

    [Fact]
    public async Task A_day_with_no_available_time_is_fully_booked()
    {
        var day = new DateOnly(2026, 11, 11);
        foreach (var (hour, minute) in new[] { (8, 0), (9, 30), (11, 0), (12, 30), (14, 0), (15, 30) })
        {
            await _app.BookAsync(BrakeInspection, Iso(day, hour, minute), $"fill-{hour}-{minute}"); // the lift bay all day
        }

        var result = await DaysAsync(BrakeInspection, "2026-11-10", "2026-11-12");

        result.Value.Days.Select(d => (d.Date, d.AvailableCount, d.Status)).Should().Equal(
            ("2026-11-10", 31, DayAvailabilityStatus.Available),
            ("2026-11-11", 0, DayAvailabilityStatus.FullyBooked),
            ("2026-11-12", 31, DayAvailabilityStatus.Available));
        (await DayAsync(BrakeInspection, day)).Times.Should().OnlyContain(t => t.Status == StartTimeStatus.Booked);
    }

    [Fact]
    public async Task Today_is_fully_booked_when_every_remaining_time_has_passed()
    {
        _app.Clock.UtcNow = Utc(new DateOnly(2026, 11, 9), 16, 45); // last oil change start is 16:00

        var result = await DaysAsync(OilChange, "2026-11-09", "2026-11-10");

        result.Value.Days.Should().Equal(
            new AvailabilityDayDto("2026-11-09", 0, DayAvailabilityStatus.FullyBooked),
            new AvailabilityDayDto("2026-11-10", 33, DayAvailabilityStatus.Available));
    }

    [Fact]
    public async Task A_range_entirely_in_the_past_is_all_past()
    {
        var result = await DaysAsync(OilChange, "2026-10-30", "2026-11-08", "1");

        result.Value.Days.Should().HaveCount(10).And.OnlyContain(d => d.Status == DayAvailabilityStatus.Past && d.AvailableCount == 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("2")]
    public async Task Days_give_the_same_answer_as_the_day_endpoint_for_each_date(string? vehicleId)
    {
        _app.Clock.UtcNow = Utc(new DateOnly(2026, 11, 9), 9, 20);
        await _app.BookAsync(OilChangeThuDuc, Local.Iso(10), "thu-duc", vehicleId: 2, dealershipId: 2);
        await _app.BookAsync(BrakeInspection, Local.Iso(13), "brakes-10");
        await _app.BookAsync(BrakeInspection, Iso(new DateOnly(2026, 11, 9), 14), "brakes-today");
        await _app.BookAsync(OilChange, Iso(new DateOnly(2026, 11, 12), 8), "oil-12", vehicleId: 2);
        foreach (var (hour, minute) in new[] { (8, 0), (9, 30), (11, 0), (12, 30), (14, 0), (15, 30) })
        {
            await _app.BookAsync(BrakeInspection, Iso(new DateOnly(2026, 11, 11), hour, minute), $"fill-{hour}-{minute}");
        }

        foreach (var serviceTypeId in new[] { OilChange, BrakeInspection, 3, 4 })
        {
            var days = (await DaysAsync(serviceTypeId, "2026-11-07", "2026-11-13", vehicleId)).Value.Days;

            days.Should().HaveCount(7);
            foreach (var entry in days)
            {
                var single = await DayAsync(serviceTypeId, DateOnly.Parse(entry.Date, System.Globalization.CultureInfo.InvariantCulture), vehicleId);
                entry.AvailableCount.Should().Be(single.Slots.Count, $"service {serviceTypeId} on {entry.Date}");
            }
        }
    }

    [Fact]
    public async Task Days_are_car_aware()
    {
        await _app.BookAsync(OilChangeThuDuc, Local.Iso(10), "thu-duc", vehicleId: 2, dealershipId: 2);

        var withCar = await DaysAsync(BrakeInspection, "2026-11-10", "2026-11-10", "2");
        var withoutCar = await DaysAsync(BrakeInspection, "2026-11-10", "2026-11-10");

        withCar.Value.Days.Single().AvailableCount.Should().Be(31 - 9);
        withoutCar.Value.Days.Single().AvailableCount.Should().Be(31);
    }

    [Fact]
    public async Task Days_range_of_42_days_is_allowed()
    {
        var result = await DaysAsync(OilChange, "2026-11-10", "2026-12-21");

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Days.Should().HaveCount(42);
        result.Value.Days.Select(d => d.Date).Should().BeInAscendingOrder().And.StartWith("2026-11-10").And.EndWith("2026-12-21");
    }

    [Theory]
    [InlineData(null, "2026-11-10", "from", "from is required.")]
    [InlineData("2026/11/10", "2026-11-10", "from", "from must be in the format YYYY-MM-DD.")]
    [InlineData("1999-12-31", "2000-01-01", "from", "from must be between the years 2000 and 9998.")]
    [InlineData("2026-11-10", null, "to", "to is required.")]
    [InlineData("2026-11-10", "2026-11-31", "to", "to must be in the format YYYY-MM-DD.")]
    [InlineData("9998-12-31", "9999-01-01", "to", "to must be between the years 2000 and 9998.")]
    [InlineData("2026-11-10", "2026-11-09", "to", "to must be on or after from.")]
    [InlineData("2026-11-10", "2026-12-22", "to", "The range can be at most 42 days.")]
    public async Task Days_bad_range_is_invalid(string? from, string? to, string field, string message)
    {
        var result = await DaysAsync(OilChange, from, to);

        result.Error!.Code.Should().Be(ErrorCodes.InvalidRequest);
        result.Error.FieldErrors.Should().ContainKey(field).WhoseValue.Should().Equal(message);
        result.Error.FieldErrors.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("x")]
    public async Task Days_bad_vehicle_id_is_invalid(string vehicleId)
    {
        var result = await DaysAsync(OilChange, "2026-11-10", "2026-11-12", vehicleId);

        result.Error!.Code.Should().Be(ErrorCodes.InvalidRequest);
        result.Error.FieldErrors.Should().ContainKey("vehicleId").WhoseValue.Should().Equal("vehicleId must be a positive number.");
    }

    [Fact]
    public async Task Days_bad_service_type_id_is_invalid()
    {
        var result = await DaysAsync(0, "2026-11-10", "2026-11-12");

        result.Error!.FieldErrors.Should().ContainKey("serviceTypeId");
    }

    [Theory]
    [InlineData(99, OilChange, null, "Dealership 99 was not found.")]
    [InlineData(1, 99, null, "Service type 99 was not found.")]
    [InlineData(1, OilChange, "999", "Vehicle 999 was not found.")]
    public async Task Days_unknown_ids_are_not_found(int dealershipId, int serviceTypeId, string? vehicleId, string message)
    {
        var result = await DaysAsync(serviceTypeId, "2026-11-10", "2026-11-12", vehicleId, dealershipId);

        result.Error!.Code.Should().Be(ErrorCodes.NotFound);
        result.Error.Message.Should().Be(message);
    }

    [Fact]
    public async Task Days_service_of_another_dealership_is_a_rule_violation()
    {
        var result = await DaysAsync(OilChangeThuDuc, "2026-11-10", "2026-11-12");

        result.Error!.Code.Should().Be(ErrorCodes.BookingRuleViolation);
    }

    [Fact]
    public async Task Days_reserve_nothing()
    {
        await DaysAsync(OilChange, "2026-11-10", "2026-12-21", "1");

        _app.Appointments.AddCalls.Should().Be(0);
    }
}
