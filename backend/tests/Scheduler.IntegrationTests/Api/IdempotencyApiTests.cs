using System.Net;
using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.IntegrationTests.Infrastructure;
using static Scheduler.IntegrationTests.Api.ApiClient;

namespace Scheduler.IntegrationTests.Api;

[Collection(SqlServerCollection.Name)]
public sealed class IdempotencyApiTests(SqlServerFixture fixture)
{
    private const int OilChange = 1;
    private const int BrakeInspection = 2;
    private readonly SchedulerApiFactory _factory = SchedulerApiFactory.For(fixture);
    private HttpClient Client => _client ??= _factory.CreateClient();
    private HttpClient? _client;

    [Fact]
    public async Task Same_key_and_same_body_returns_the_same_appointment_and_saves_one_row()
    {
        var day = TestDays.IdempotentReplay;
        var key = Guid.NewGuid().ToString();

        using var first = await Client.BookAsync(BrakeInspection, LocalIso(day, 13), key);
        using var second = await Client.BookAsync(BrakeInspection, LocalIso(day, 13), key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var a = await first.ReadAsync<AppointmentDto>();
        var b = await second.ReadAsync<AppointmentDto>();
        b.Should().BeEquivalentTo(a);
        second.Headers.Location.Should().Be(first.Headers.Location);
        (await CountAppointmentsAsync(day)).Should().Be(1);
    }

    [Fact]
    public async Task Same_key_with_a_different_body_is_409_idempotency_conflict()
    {
        var day = TestDays.IdempotencyConflict;
        var key = Guid.NewGuid().ToString();
        await Client.BookOkAsync(BrakeInspection, LocalIso(day, 13), key);

        using var second = await Client.BookAsync(BrakeInspection, LocalIso(day, 14), key);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.ReadProblemAsync()).Code().Should().Be("IDEMPOTENCY_CONFLICT");
        (await CountAppointmentsAsync(day)).Should().Be(1);
    }

    [Fact]
    public async Task Replay_after_the_start_time_has_passed_still_returns_the_saved_201()
    {
        var day = TestDays.ReplayAfterStart;
        var key = Guid.NewGuid().ToString();
        var booked = await Client.BookOkAsync(OilChange, LocalIso(day, 9), key);
        _factory.Clock.UtcNow = LocalToUtc(day, 12);

        try
        {
            using var retry = await Client.BookAsync(OilChange, LocalIso(day, 9), key);

            retry.StatusCode.Should().Be(HttpStatusCode.Created);
            (await retry.ReadAsync<AppointmentDto>()).Id.Should().Be(booked.Id);
        }
        finally
        {
            _factory.Clock.UtcNow = FixedClock.DefaultNow;
        }
    }

    [Fact]
    public async Task Replay_of_a_cancelled_booking_returns_the_original_response()
    {
        var day = TestDays.CancelledReplay;
        var key = Guid.NewGuid().ToString();
        var booked = await Client.BookOkAsync(OilChange, LocalIso(day, 9), key);
        await Client.PostAsync($"/api/appointments/{booked.Id}/cancel", null);

        var replay = await Client.BookOkAsync(OilChange, LocalIso(day, 9), key);

        replay.Should().BeEquivalentTo(booked);
        (await CountAppointmentsAsync(day)).Should().Be(1);
    }

    [Fact]
    public async Task No_availability_is_409_with_up_to_three_suggestions_ascending()
    {
        var day = TestDays.NoAvailability;
        await Client.BookOkAsync(BrakeInspection, LocalIso(day, 13)); // the only lift bay: 13:00-14:30

        using var response = await Client.BookAsync(BrakeInspection, LocalIso(day, 13, 30), vehicleId: 2); // another car

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.Code().Should().Be("NO_AVAILABILITY");
        var suggestions = problem.GetProperty("suggestedStartTimes").EnumerateArray().Select(e => e.GetString()).ToList();
        suggestions.Should().Equal(UtcIso(LocalToUtc(day, 14, 30)), UtcIso(LocalToUtc(day, 14, 45)), UtcIso(LocalToUtc(day, 15)));
    }

    [Fact]
    public async Task No_availability_suggestions_skip_times_when_the_customers_car_is_busy()
    {
        var day = TestDays.SuggestionsSkipBusyCar;
        await Client.BookOkAsync(BrakeInspection, LocalIso(day, 13)); // car 1 takes the only lift bay 13:00-14:30
        using (var thuDuc = await Client.BookAsync(5, LocalIso(day, 15, 30), vehicleId: 2, dealershipId: 2)) // car 2 busy 15:30-16:30
        {
            thuDuc.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using var response = await Client.BookAsync(BrakeInspection, LocalIso(day, 13, 30), vehicleId: 2);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.Code().Should().Be("NO_AVAILABILITY");
        // 14:30, 14:45 and 15:00 would overlap car 2's booking at Thu Duc, so earlier times are suggested.
        problem.GetProperty("suggestedStartTimes").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(UtcIso(LocalToUtc(day, 11)), UtcIso(LocalToUtc(day, 11, 15)), UtcIso(LocalToUtc(day, 11, 30)));
    }

    private async Task<int> CountAppointmentsAsync(DateOnly day)
    {
        var start = LocalToUtc(day, 0);
        var end = start.AddDays(1);
        await using var db = fixture.CreateDbContext();
        return await db.Appointments.CountAsync(a => a.StartUtc >= start && a.StartUtc < end);
    }
}
