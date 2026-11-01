using System.Net;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.IntegrationTests.Infrastructure;
using static Scheduler.IntegrationTests.Api.ApiClient;

namespace Scheduler.IntegrationTests.Api;

[Collection(SqlServerCollection.Name)]
public sealed class AvailabilityApiTests(SqlServerFixture fixture)
{
    private const int BrakeInspection = 2;
    private readonly HttpClient _client = SchedulerApiFactory.For(fixture).CreateClient();

    private async Task<AvailabilityDto> GetAsync(DateOnly day, int serviceTypeId = BrakeInspection)
    {
        using var response = await _client.GetAsync($"/api/dealerships/1/availability?serviceTypeId={serviceTypeId}&date={day:yyyy-MM-dd}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<AvailabilityDto>();
    }

    [Fact]
    public async Task Availability_returns_every_free_start_sorted_ascending()
    {
        var day = TestDays.AvailabilitySorted;

        var availability = await GetAsync(day);

        availability.Date.Should().Be(day.ToString("yyyy-MM-dd"));
        availability.TimeZoneId.Should().Be("Asia/Ho_Chi_Minh");
        availability.DurationMinutes.Should().Be(90);
        availability.Slots.Should().HaveCount(31); // 08:00 ... 15:30 local
        availability.Slots.Select(s => s.StartUtc).Should().BeInAscendingOrder();
        availability.Slots[0].StartUtc.Should().Be(LocalToUtc(day, 8));
        availability.Slots[^1].EndUtc.Should().Be(LocalToUtc(day, 17));
    }

    [Fact]
    public async Task Availability_times_are_utc_with_z()
    {
        var json = await _client.GetStringAsync($"/api/dealerships/1/availability?serviceTypeId=1&date={TestDays.AvailabilitySorted:yyyy-MM-dd}");

        json.Should().Contain($"\"startUtc\":\"{UtcIso(LocalToUtc(TestDays.AvailabilitySorted, 8))}\"");
    }

    [Fact]
    public async Task A_booked_range_disappears_from_availability()
    {
        var day = TestDays.AvailabilityAfterBooking;
        await _client.BookOkAsync(BrakeInspection, LocalIso(day, 13)); // Bay 2 (only lift) 13:00-14:30

        var availability = await GetAsync(day);

        var starts = availability.Slots.Select(s => s.StartUtc).ToList();
        starts.Should().HaveCount(20);
        starts.Should().Contain(LocalToUtc(day, 11, 30)).And.Contain(LocalToUtc(day, 14, 30));
        starts.Should().NotContain(LocalToUtc(day, 11, 45)).And.NotContain(LocalToUtc(day, 13)).And.NotContain(LocalToUtc(day, 14, 15));
    }

    [Theory]
    [InlineData("/api/dealerships/1/availability?serviceTypeId=2&date=2030-13-01", HttpStatusCode.BadRequest, "INVALID_REQUEST")]
    [InlineData("/api/dealerships/1/availability?serviceTypeId=2", HttpStatusCode.BadRequest, "INVALID_REQUEST")]
    [InlineData("/api/dealerships/1/availability?serviceTypeId=abc&date=2030-06-03", HttpStatusCode.BadRequest, "INVALID_REQUEST")]
    [InlineData("/api/dealerships/99/availability?serviceTypeId=2&date=2030-06-03", HttpStatusCode.NotFound, "NOT_FOUND")]
    [InlineData("/api/dealerships/1/availability?serviceTypeId=99&date=2030-06-03", HttpStatusCode.NotFound, "NOT_FOUND")]
    [InlineData("/api/dealerships/1/availability?serviceTypeId=5&date=2030-06-03", HttpStatusCode.UnprocessableEntity, "BOOKING_RULE_VIOLATION")]
    public async Task Availability_errors(string url, HttpStatusCode status, string code)
    {
        using var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(status);
        (await response.ReadProblemAsync()).Code().Should().Be(code);
    }
}
