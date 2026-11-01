using System.Net;
using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.IntegrationTests.Infrastructure;
using static Scheduler.IntegrationTests.Api.ApiClient;

namespace Scheduler.IntegrationTests.Api;

/// <summary>
/// SPEC section 8: double booking must be impossible under concurrent requests.
/// EV battery check at District 1 needs EV Senior and an EV bay: only Lan (Tuan is inactive) and only Bay 3 qualify.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ConcurrencyApiTests(SqlServerFixture fixture)
{
    private const int EvBatteryCheck = 4;
    private const int OilChange = 1;
    private readonly SchedulerApiFactory _factory = SchedulerApiFactory.For(fixture);

    [Fact]
    public async Task Twenty_parallel_bookings_of_the_same_slot_give_exactly_one_201_and_nineteen_409()
    {
        var day = TestDays.ConcurrentDifferentKeys;
        var start = LocalIso(day, 10);
        var client = _factory.CreateClient();
        using var gate = new SemaphoreSlim(0);

        var requests = Enumerable.Range(0, 20).Select(async i =>
        {
            await gate.WaitAsync();
            using var response = await client.BookAsync(EvBatteryCheck, start, $"concurrent-{day:yyyyMMdd}-{i}");
            var code = response.StatusCode == HttpStatusCode.Created ? "CREATED" : (await response.ReadProblemAsync()).Code();
            return (response.StatusCode, code);
        }).ToList();
        gate.Release(20);
        var results = await Task.WhenAll(requests);

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, string.Join(", ", results));
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict && r.code == "NO_AVAILABILITY").Should().Be(19, string.Join(", ", results));

        var startUtc = LocalToUtc(day, 10);
        var endUtc = startUtc.AddMinutes(60);
        await using var db = fixture.CreateDbContext();
        (await db.Appointments.CountAsync(a => a.StartUtc >= startUtc && a.StartUtc < endUtc)).Should().Be(1);
        (await db.ResourceSlots.CountAsync(s => s.SlotStartUtc >= startUtc && s.SlotStartUtc < endUtc)).Should().Be(2 * (60 / 15));
    }

    [Fact]
    public async Task Ten_parallel_retries_with_the_same_key_and_body_all_get_the_same_appointment()
    {
        var day = TestDays.ConcurrentSameKey;
        var key = $"same-key-{Guid.NewGuid()}";
        var client = _factory.CreateClient();
        using var gate = new SemaphoreSlim(0);

        var requests = Enumerable.Range(0, 10).Select(async _ =>
        {
            await gate.WaitAsync();
            using var response = await client.BookAsync(OilChange, LocalIso(day, 10), key);
            var body = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, Body: body);
        }).ToList();
        gate.Release(10);
        var results = await Task.WhenAll(requests);

        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created, string.Join("\n", results.Select(r => r.Body)));
        results.Select(r => System.Text.Json.JsonSerializer.Deserialize<AppointmentDto>(r.Body, Json)!.Id).Distinct().Should().ContainSingle();

        var startUtc = LocalToUtc(day, 0);
        await using var db = fixture.CreateDbContext();
        (await db.Appointments.CountAsync(a => a.StartUtc >= startUtc && a.StartUtc < startUtc.AddDays(1))).Should().Be(1);
    }
}
