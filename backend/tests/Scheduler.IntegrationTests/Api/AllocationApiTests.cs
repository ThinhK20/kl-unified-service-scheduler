using System.Net;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.IntegrationTests.Infrastructure;
using static Scheduler.IntegrationTests.Api.ApiClient;

namespace Scheduler.IntegrationTests.Api;

[Collection(SqlServerCollection.Name)]
public sealed class AllocationApiTests(SqlServerFixture fixture)
{
    private const int OilChange = 1;
    private const int EvBatteryCheck = 4;

    /// <summary>Own day (no other test uses August 2030), see <see cref="TestDays"/>.</summary>
    private static readonly DateOnly ScarcityDay = new(2030, 8, 5);

    private readonly SchedulerApiFactory _factory = SchedulerApiFactory.For(fixture);
    private HttpClient Client => _client ??= _factory.CreateClient();
    private HttpClient? _client;

    [Fact]
    public async Task Oil_change_at_10_goes_to_Nam_so_an_EV_check_at_10_can_still_go_to_Lan()
    {
        using var oilResponse = await Client.BookAsync(OilChange, LocalIso(ScarcityDay, 10), vehicleId: 1);
        oilResponse.StatusCode.Should().Be(HttpStatusCode.Created, await oilResponse.Content.ReadAsStringAsync());
        (await oilResponse.ReadAsync<AppointmentDto>()).Technician.Name.Should().Be("Nam");

        using var evResponse = await Client.BookAsync(EvBatteryCheck, LocalIso(ScarcityDay, 10), vehicleId: 2);
        evResponse.StatusCode.Should().Be(HttpStatusCode.Created, await evResponse.Content.ReadAsStringAsync());
        var ev = await evResponse.ReadAsync<AppointmentDto>();
        ev.Technician.Name.Should().Be("Lan");
        ev.ServiceBay.Name.Should().Be("Bay 3");
    }
}
