using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scheduler.Application.Abstractions;
using Scheduler.Domain.Entities;
using Scheduler.IntegrationTests.Infrastructure;

namespace Scheduler.IntegrationTests.Api;

/// <summary>Client input that could overflow time math, and broken server data.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class RobustnessApiTests(SqlServerFixture fixture)
{
    private readonly HttpClient _client = SchedulerApiFactory.For(fixture).CreateClient();

    [Theory]
    [InlineData("9999-12-31T23:45:00Z")]
    [InlineData("9999-12-31T23:45:00+07:00")]
    public async Task Booking_a_start_in_year_9999_is_400_not_500(string startTime)
    {
        using var response = await _client.BookAsync(1, startTime);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync();
        problem.Code().Should().Be("INVALID_REQUEST");
        problem.GetProperty("errors").GetProperty("startTime")[0].GetString().Should().Contain("2000 and 9998");
    }

    [Fact]
    public async Task Availability_for_year_9999_is_400_not_500()
    {
        using var response = await _client.GetAsync("/api/dealerships/1/availability?serviceTypeId=1&date=9999-12-31");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Code().Should().Be("INVALID_REQUEST");
    }

    [Fact]
    public async Task Unknown_time_zone_in_stored_data_is_500_internal_error_not_400()
    {
        using var factory = SchedulerApiFactory.For(fixture).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDealershipRepository>();
                services.AddScoped<IDealershipRepository, CorruptDealershipRepository>();
            }));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/dealerships/1/availability?serviceTypeId=1&date={TestDays.CorruptTimeZone:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await response.ReadProblemAsync();
        problem.Code().Should().Be("INTERNAL_ERROR");
        problem.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.", "no details leak outside Development");
    }

    /// <summary>Returns dealership 1 with a time zone id that does not exist, as if the database row were broken.</summary>
    private sealed class CorruptDealershipRepository : IDealershipRepository
    {
        public Task<Dealership?> GetByIdAsync(int dealershipId, CancellationToken cancellationToken)
        {
            var dealership = Dealership.Create("Broken", "Asia/Ho_Chi_Minh", new TimeOnly(8, 0), new TimeOnly(17, 0));
            typeof(Dealership).GetProperty(nameof(Dealership.Id))!.SetValue(dealership, 1);
            typeof(Dealership).GetProperty(nameof(Dealership.TimeZoneId))!.SetValue(dealership, "Mars/Olympus_Mons");
            return Task.FromResult<Dealership?>(dealership);
        }

        public Task<IReadOnlyList<Dealership>> GetActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Dealership>>([]);
    }
}
