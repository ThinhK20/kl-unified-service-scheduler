using System.Globalization;
using System.Net;
using System.Text.Json;
using Scheduler.Application.Allocation;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.IntegrationTests.Infrastructure;
using static Scheduler.IntegrationTests.Api.ApiClient;

namespace Scheduler.IntegrationTests.Api;

/// <summary>
/// SPEC section 6: availability <c>times</c> with a status (car-aware with vehicleId) and the days endpoint, against
/// the real API and SQL Server. The key invariant: what the screen calls Available, booking accepts.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AvailabilityStatusApiTests(SqlServerFixture fixture)
{
    private const int OilChange = 1;
    private const int BrakeInspection = 2; // 90 min, only the lift bay (Bay 2) can host it
    private const int OilChangeThuDuc = 5;
    private const int CarOne = 1;
    private const int CarTwo = 2;
    private readonly SchedulerApiFactory _factory = SchedulerApiFactory.For(fixture);
    private readonly HttpClient _client = SchedulerApiFactory.For(fixture).CreateClient();

    private static string Date(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IEnumerable<DateTime> Grid(DateOnly day, int fromHour, int fromMinute, int toHour, int toMinute)
    {
        for (var t = LocalToUtc(day, fromHour, fromMinute); t <= LocalToUtc(day, toHour, toMinute); t = t.AddMinutes(15))
        {
            yield return t;
        }
    }

    private async Task<AvailabilityDto> DayAsync(DateOnly day, int serviceTypeId, int? vehicleId = null)
    {
        var url = $"/api/dealerships/1/availability?serviceTypeId={serviceTypeId}&date={Date(day)}" + (vehicleId is { } v ? $"&vehicleId={v}" : string.Empty);
        using var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<AvailabilityDto>();
    }

    private async Task<AvailabilityDaysDto> DaysAsync(DateOnly from, DateOnly to, int serviceTypeId, int? vehicleId = null)
    {
        var url = $"/api/dealerships/1/availability/days?serviceTypeId={serviceTypeId}&from={Date(from)}&to={Date(to)}"
            + (vehicleId is { } v ? $"&vehicleId={v}" : string.Empty);
        using var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<AvailabilityDaysDto>();
    }

    private async Task<AppointmentDto> BookCreatedAsync(int serviceTypeId, string startTime, int vehicleId = CarOne, int dealershipId = 1)
    {
        using var response = await _client.BookAsync(serviceTypeId, startTime, vehicleId: vehicleId, dealershipId: dealershipId);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, text);
        return JsonSerializer.Deserialize<AppointmentDto>(text, Json)!;
    }

    private static IEnumerable<DateTime> Starts(AvailabilityDto dto, StartTimeStatus status) =>
        dto.Times.Where(t => t.Status == status).Select(t => t.StartUtc);

    [Fact]
    public async Task Times_when_the_car_is_booked_at_another_dealership_are_vehicle_booked()
    {
        var day = TestDays.TimesCarBusy;
        await BookCreatedAsync(OilChangeThuDuc, LocalIso(day, 10), CarTwo, dealershipId: 2); // car 2 busy 10:00-11:00

        var forCarTwo = await DayAsync(day, BrakeInspection, CarTwo);
        var forCarOne = await DayAsync(day, BrakeInspection, CarOne);
        var ignoringCar = await DayAsync(day, BrakeInspection);

        forCarTwo.Times.Should().HaveCount(31);
        Starts(forCarTwo, StartTimeStatus.VehicleBooked).Should().Equal(Grid(day, 8, 45, 10, 45));
        forCarTwo.Slots.Select(s => s.StartUtc).Should().Equal(Starts(forCarTwo, StartTimeStatus.Available)).And.HaveCount(22);
        forCarOne.Slots.Should().HaveCount(31);
        ignoringCar.Slots.Should().HaveCount(31);
        ignoringCar.Times.Should().OnlyContain(t => t.Status == StartTimeStatus.Available);

        // Booking agrees: a VehicleBooked time is VEHICLE_ALREADY_BOOKED.
        using var conflict = await _client.BookAsync(BrakeInspection, LocalIso(day, 9), vehicleId: CarTwo);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.ReadProblemAsync()).Code().Should().Be("VEHICLE_ALREADY_BOOKED");
    }

    [Fact]
    public async Task A_fully_booked_day_is_fully_booked_in_days_and_every_time_is_booked()
    {
        var day = TestDays.TimesFullyBooked;
        foreach (var (hour, minute) in new[] { (8, 0), (9, 30), (11, 0), (12, 30), (14, 0), (15, 30) })
        {
            await BookCreatedAsync(BrakeInspection, LocalIso(day, hour, minute)); // the only lift bay, all day
        }

        var days = await DaysAsync(day.AddDays(-1), day, BrakeInspection, CarOne);
        var single = await DayAsync(day, BrakeInspection, CarTwo);

        days.Days.Select(d => (d.Date, d.AvailableCount, d.Status)).Should().Equal(
            (Date(day.AddDays(-1)), 31, DayAvailabilityStatus.Available),
            (Date(day), 0, DayAvailabilityStatus.FullyBooked));
        single.Times.Should().HaveCount(31).And.OnlyContain(t => t.Status == StartTimeStatus.Booked);
        single.Slots.Should().BeEmpty();

        using var conflict = await _client.BookAsync(BrakeInspection, LocalIso(day, 10), vehicleId: CarTwo);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.ReadProblemAsync()).Code().Should().Be("NO_AVAILABILITY");
    }

    [Fact]
    public async Task Days_give_the_same_answer_as_the_day_endpoint_for_every_date_of_a_range_with_bookings()
    {
        var from = TestDays.DaysRangeFrom;
        var to = TestDays.DaysRangeTo;
        _factory.Clock.UtcNow = LocalToUtc(from.AddDays(1), 11, 10); // 10 Sep 11:10 local: 9 Sep is past, 10 Sep partly
        try
        {
            await BookCreatedAsync(BrakeInspection, LocalIso(from.AddDays(1), 13));
            await BookCreatedAsync(OilChangeThuDuc, LocalIso(from.AddDays(2), 10), CarTwo, dealershipId: 2);
            await BookCreatedAsync(OilChange, LocalIso(from.AddDays(2), 8), CarOne);
            await BookCreatedAsync(BrakeInspection, LocalIso(from.AddDays(3), 9, 30), CarTwo);
            await BookCreatedAsync(OilChange, LocalIso(to, 16), CarTwo);
            foreach (var (hour, minute) in new[] { (8, 0), (9, 30), (11, 0), (12, 30), (14, 0), (15, 30) })
            {
                await BookCreatedAsync(BrakeInspection, LocalIso(from.AddDays(4), hour, minute));
            }

            foreach (var serviceTypeId in new[] { OilChange, BrakeInspection, 3, 4 })
            {
                foreach (var vehicleId in new int?[] { null, CarOne, CarTwo })
                {
                    var days = await DaysAsync(from, to, serviceTypeId, vehicleId);

                    days.Days.Select(d => d.Date).Should().Equal(Enumerable.Range(0, 12).Select(i => Date(from.AddDays(i))));
                    foreach (var entry in days.Days)
                    {
                        var date = DateOnly.ParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                        var single = await DayAsync(date, serviceTypeId, vehicleId);
                        var because = $"service {serviceTypeId}, vehicle {vehicleId?.ToString(CultureInfo.InvariantCulture) ?? "none"}, {entry.Date}";

                        entry.AvailableCount.Should().Be(single.Slots.Count, because);
                        var expected = date < from.AddDays(1)
                            ? DayAvailabilityStatus.Past
                            : single.Slots.Count == 0 ? DayAvailabilityStatus.FullyBooked : DayAvailabilityStatus.Available;
                        entry.Status.Should().Be(expected, because);
                    }
                }
            }

            var brakes = await DaysAsync(from, to, BrakeInspection);
            brakes.Days[0].Status.Should().Be(DayAvailabilityStatus.Past);
            brakes.Days[4].Status.Should().Be(DayAvailabilityStatus.FullyBooked);
            brakes.Days[1].AvailableCount.Should().BeInRange(1, 30, "today: past starts and the 13:00 booking are not counted");
        }
        finally
        {
            _factory.Clock.UtcNow = FixedClock.DefaultNow;
        }
    }

    [Theory]
    [InlineData(BrakeInspection)]
    [InlineData(OilChange)]
    public async Task Every_available_time_for_the_car_books_with_201_and_every_other_time_is_refused_for_that_reason(int serviceTypeId)
    {
        var day = serviceTypeId == BrakeInspection ? TestDays.TimesEveryAvailableBrakes : TestDays.TimesEveryAvailableOil;
        _factory.Clock.UtcNow = LocalToUtc(day, 8, 40); // 08:00 ... 08:30 are past
        try
        {
            // Bay 2 and a technician busy 13:00-14:30 (car 1); car 2 busy 10:00-11:00 at the other dealership.
            await BookCreatedAsync(BrakeInspection, LocalIso(day, 13), CarOne);
            await BookCreatedAsync(OilChangeThuDuc, LocalIso(day, 10), CarTwo, dealershipId: 2);
            if (serviceTypeId == OilChange)
            {
                // Bay 1 (the only standard bay) busy 15:00-16:00, so some oil change times are Booked.
                await BookCreatedAsync(OilChange, LocalIso(day, 15), CarOne);
            }

            var availability = await DayAsync(day, serviceTypeId, CarTwo);
            Starts(availability, StartTimeStatus.Past).Should().Equal(Grid(day, 8, 0, 8, 30));
            Starts(availability, StartTimeStatus.VehicleBooked).Should().NotBeEmpty();
            Starts(availability, StartTimeStatus.Booked).Should().NotBeEmpty();
            Starts(availability, StartTimeStatus.Available).Should().HaveCountGreaterThanOrEqualTo(8); // brakes: 31 - 3 past - 9 car - 11 bay

            foreach (var time in availability.Times)
            {
                using var response = await _client.BookAsync(serviceTypeId, UtcIso(time.StartUtc), vehicleId: CarTwo);
                var text = await response.Content.ReadAsStringAsync();
                switch (time.Status)
                {
                    case StartTimeStatus.Available:
                        response.StatusCode.Should().Be(HttpStatusCode.Created, $"{UtcIso(time.StartUtc)} was Available: {text}");
                        var booked = JsonSerializer.Deserialize<AppointmentDto>(text, Json)!;
                        // Cancel to restore the day, so the next time is checked against the same snapshot.
                        (await _client.PostAsync($"/api/appointments/{booked.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
                        break;
                    case StartTimeStatus.Past:
                        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
                        break;
                    case StartTimeStatus.VehicleBooked:
                        response.StatusCode.Should().Be(HttpStatusCode.Conflict, text);
                        text.Should().Contain("VEHICLE_ALREADY_BOOKED");
                        break;
                    case StartTimeStatus.Booked:
                        response.StatusCode.Should().Be(HttpStatusCode.Conflict, text);
                        text.Should().Contain("NO_AVAILABILITY");
                        break;
                }
            }

            (await DayAsync(day, serviceTypeId, CarTwo)).Should().BeEquivalentTo(availability, "every test booking was cancelled");
        }
        finally
        {
            _factory.Clock.UtcNow = FixedClock.DefaultNow;
        }
    }

    [Fact]
    public async Task Json_shape_has_times_with_string_status_and_days_with_counts()
    {
        var day = TestDays.AvailabilitySorted;

        var json = await _client.GetStringAsync($"/api/dealerships/1/availability?serviceTypeId=1&date={Date(day)}&vehicleId=1");
        var daysJson = await _client.GetStringAsync($"/api/dealerships/1/availability/days?serviceTypeId=1&from={Date(day)}&to={Date(day)}");

        json.Should().Contain($"\"slots\":[{{\"startUtc\":\"{UtcIso(LocalToUtc(day, 8))}\",\"endUtc\":\"{UtcIso(LocalToUtc(day, 9))}\"}}");
        json.Should().Contain($"\"times\":[{{\"startUtc\":\"{UtcIso(LocalToUtc(day, 8))}\",\"endUtc\":\"{UtcIso(LocalToUtc(day, 9))}\",\"status\":\"Available\"}}");
        daysJson.Should().Be(
            "{\"dealershipId\":1,\"serviceTypeId\":1,\"from\":\"2030-06-03\",\"to\":\"2030-06-03\",\"timeZoneId\":\"Asia/Ho_Chi_Minh\","
            + "\"durationMinutes\":60,\"days\":[{\"date\":\"2030-06-03\",\"availableCount\":33,\"status\":\"Available\"}]}");
    }

    [Fact]
    public async Task A_range_of_42_days_is_allowed()
    {
        var days = await DaysAsync(TestDays.DaysMaxRangeFrom, TestDays.DaysMaxRangeTo, OilChange, CarOne);

        days.Days.Should().HaveCount(42).And.OnlyContain(d => d.Status == DayAvailabilityStatus.Available && d.AvailableCount == 33);
    }

    [Theory]
    [InlineData("/api/dealerships/1/availability?serviceTypeId=1&date=2030-06-03&vehicleId=0", "vehicleId", "vehicleId must be a positive number.")]
    [InlineData("/api/dealerships/1/availability?serviceTypeId=1&date=2030-06-03&vehicleId=-1", "vehicleId", "vehicleId must be a positive number.")]
    [InlineData("/api/dealerships/1/availability?serviceTypeId=1&date=2030-06-03&vehicleId=abc", "vehicleId", "vehicleId must be a positive number.")]
    [InlineData("/api/dealerships/1/availability?serviceTypeId=1&date=2030-06-03&vehicleId=", "vehicleId", "vehicleId must be a positive number.")]
    [InlineData("/api/dealerships/1/availability?serviceTypeId=1&date=2030-06-03&vehicleId=1.0", "vehicleId", "vehicleId must be a positive number.")]
    [InlineData("/api/dealerships/1/availability/days?serviceTypeId=1&from=2030-06-03&to=2030-06-05&vehicleId=0", "vehicleId", "vehicleId must be a positive number.")]
    [InlineData("/api/dealerships/1/availability/days?serviceTypeId=1&to=2030-06-05", "from", "from is required.")]
    [InlineData("/api/dealerships/1/availability/days?serviceTypeId=1&from=03-06-2030&to=2030-06-05", "from", "from must be in the format YYYY-MM-DD.")]
    [InlineData("/api/dealerships/1/availability/days?serviceTypeId=1&from=2030-06-03", "to", "to is required.")]
    [InlineData("/api/dealerships/1/availability/days?serviceTypeId=1&from=2030-06-03&to=2030-06-02", "to", "to must be on or after from.")]
    [InlineData("/api/dealerships/1/availability/days?serviceTypeId=1&from=2031-01-01&to=2031-02-12", "to", "The range can be at most 42 days.")]
    [InlineData("/api/dealerships/1/availability/days?serviceTypeId=1&from=2030-06-03&to=9999-01-01", "to", "to must be between the years 2000 and 9998.")]
    [InlineData("/api/dealerships/1/availability/days?serviceTypeId=0&from=2030-06-03&to=2030-06-05", "serviceTypeId", "serviceTypeId is required.")]
    public async Task Bad_parameters_are_400_invalid_request_with_the_field(string url, string field, string message)
    {
        using var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync();
        problem.Code().Should().Be("INVALID_REQUEST");
        problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()).Should().Equal(message);
    }

    [Theory]
    [InlineData("/api/dealerships/1/availability?serviceTypeId=1&date=2030-06-03&vehicleId=987654", HttpStatusCode.NotFound, "NOT_FOUND", "Vehicle 987654 was not found.")]
    [InlineData("/api/dealerships/1/availability/days?serviceTypeId=1&from=2030-06-03&to=2030-06-05&vehicleId=987654", HttpStatusCode.NotFound, "NOT_FOUND", "Vehicle 987654 was not found.")]
    [InlineData("/api/dealerships/99/availability/days?serviceTypeId=1&from=2030-06-03&to=2030-06-05", HttpStatusCode.NotFound, "NOT_FOUND", "Dealership 99 was not found.")]
    [InlineData("/api/dealerships/1/availability/days?serviceTypeId=99&from=2030-06-03&to=2030-06-05", HttpStatusCode.NotFound, "NOT_FOUND", "Service type 99 was not found.")]
    [InlineData("/api/dealerships/1/availability/days?serviceTypeId=5&from=2030-06-03&to=2030-06-05", HttpStatusCode.UnprocessableEntity, "BOOKING_RULE_VIOLATION", null)]
    public async Task Unknown_ids_and_rule_violations(string url, HttpStatusCode status, string code, string? detail)
    {
        using var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(status);
        var problem = await response.ReadProblemAsync();
        problem.Code().Should().Be(code);
        if (detail is not null)
        {
            problem.GetProperty("detail").GetString().Should().Be(detail);
        }
    }
}
