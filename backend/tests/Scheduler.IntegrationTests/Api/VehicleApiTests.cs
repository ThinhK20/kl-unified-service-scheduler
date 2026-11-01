using System.Net;
using Scheduler.IntegrationTests.Infrastructure;
using static Scheduler.IntegrationTests.Api.ApiClient;

namespace Scheduler.IntegrationTests.Api;

/// <summary>SPEC section 0c: the vehicle is a reserved resource. One car cannot be in two places at once.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class VehicleApiTests(SqlServerFixture fixture)
{
    private const int OilChangeDistrict1 = 1;
    private const int OilChangeThuDuc = 5;
    private readonly HttpClient _client = SchedulerApiFactory.For(fixture).CreateClient();

    [Fact]
    public async Task Same_vehicle_at_two_dealerships_at_the_same_time_is_409_vehicle_already_booked_until_cancelled()
    {
        var day = TestDays.VehicleTwoDealerships;
        var first = await _client.BookOkAsync(OilChangeDistrict1, LocalIso(day, 10));

        using var second = await _client.BookAsync(OilChangeThuDuc, LocalIso(day, 10, 30), dealershipId: 2);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await second.ReadProblemAsync();
        problem.Code().Should().Be("VEHICLE_ALREADY_BOOKED");
        problem.GetProperty("detail").GetString().Should().Be("This vehicle already has a booking at that time.");
        problem.TryGetProperty("suggestedStartTimes", out _).Should().BeFalse();

        (await _client.PostAsync($"/api/appointments/{first.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var afterCancel = await _client.BookAsync(OilChangeThuDuc, LocalIso(day, 10, 30), dealershipId: 2);
        afterCancel.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Ten_parallel_bookings_of_the_same_vehicle_at_different_dealerships_give_one_201_and_nine_409()
    {
        var day = TestDays.VehicleConcurrent;
        var start = LocalIso(day, 10);
        // Different dealerships and services (oil, brakes, tyres, EV at District 1; oil at Thu Duc), same car, same time.
        (int ServiceTypeId, int DealershipId)[] targets = [(1, 1), (2, 1), (3, 1), (4, 1), (5, 2)];
        var client = SchedulerApiFactory.For(fixture).CreateClient();
        using var gate = new SemaphoreSlim(0);

        var requests = Enumerable.Range(0, 10).Select(async i =>
        {
            var (serviceTypeId, dealershipId) = targets[i % targets.Length];
            await gate.WaitAsync();
            using var response = await client.BookAsync(serviceTypeId, start, $"vehicle-race-{i}-{Guid.NewGuid()}", vehicleId: 2, dealershipId: dealershipId);
            var code = response.StatusCode == HttpStatusCode.Created ? "CREATED" : (await response.ReadProblemAsync()).Code();
            return (response.StatusCode, code);
        }).ToList();
        gate.Release(10);
        var results = await Task.WhenAll(requests);

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, string.Join(", ", results));
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict && r.code == "VEHICLE_ALREADY_BOOKED")
            .Should().Be(9, string.Join(", ", results));
    }
}
