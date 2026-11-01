using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Scheduler.Application.Catalog.Dtos;
using Scheduler.IntegrationTests.Infrastructure;

namespace Scheduler.IntegrationTests.Api;

/// <summary>Catalog endpoints, health, CORS and the error shape of unknown routes.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class CatalogApiTests(SqlServerFixture fixture)
{
    private readonly HttpClient _client = SchedulerApiFactory.For(fixture).CreateClient();

    [Fact]
    public async Task Dealerships_are_listed_with_local_hours()
    {
        var dealerships = await (await _client.GetAsync("/api/dealerships")).ReadAsync<List<DealershipDto>>();

        dealerships.Should().BeEquivalentTo(
            [
                new DealershipDto(1, "District 1 Service Centre", "Asia/Ho_Chi_Minh", "08:00", "17:00"),
                new DealershipDto(2, "Thu Duc Service Centre", "Asia/Ho_Chi_Minh", "07:30", "18:00"),
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Service_types_can_be_filtered_by_dealership()
    {
        var all = await (await _client.GetAsync("/api/service-types")).ReadAsync<List<ServiceTypeDto>>();
        var thuDuc = await (await _client.GetAsync("/api/service-types?dealershipId=2")).ReadAsync<List<ServiceTypeDto>>();

        all.Select(s => s.Id).Should().Equal(1, 2, 3, 4, 5);
        thuDuc.Should().ContainSingle().Which.Should().Be(new ServiceTypeDto(5, 2, "Oil change", 60, "General", "Trainee", "Standard"));
    }

    [Fact]
    public async Task Service_types_json_uses_camel_case_and_enum_strings()
    {
        var json = await _client.GetStringAsync("/api/service-types?dealershipId=1");

        using var document = JsonDocument.Parse(json);
        var brake = document.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetInt32() == 2);
        brake.GetProperty("requiredSkill").GetString().Should().Be("Brakes");
        brake.GetProperty("minimumTier").GetString().Should().Be("Qualified");
        brake.GetProperty("requiredBayType").GetString().Should().Be("Lift");
        brake.GetProperty("durationMinutes").GetInt32().Should().Be(90);
    }

    [Fact]
    public async Task Vehicles_of_the_demo_customer()
    {
        var vehicles = await (await _client.GetAsync("/api/customers/1/vehicles")).ReadAsync<List<VehicleDto>>();

        vehicles.Select(v => (v.Id, v.Make, v.Model, v.Year, v.Vin)).Should().StartWith(
            [(1, "Toyota", "Vios", 2021, "JTDBT923X71000001"), (2, "Honda", "CR-V", 2023, "7FARW2H58PE000002")]);
    }

    [Fact]
    public async Task Vehicles_of_an_unknown_customer_is_404_not_found()
    {
        using var response = await _client.GetAsync("/api/customers/999999/vehicles");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).Code().Should().Be("NOT_FOUND");
    }

    [Theory]
    [InlineData("/api/nothing-here")]
    [InlineData("/api/appointments/not-a-number")]
    public async Task Unknown_route_is_a_problem_with_code_not_found(string url)
    {
        using var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).Code().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Wrong_method_is_a_problem_with_a_code()
    {
        using var response = await _client.DeleteAsync("/api/dealerships");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await response.ReadProblemAsync()).Code().Should().Be("INVALID_REQUEST");
    }

    [Fact]
    public async Task Health_live_is_200()
    {
        using var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_ready_is_200_when_the_database_is_up()
    {
        using var response = await _client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"sqlserver\"");
    }

    [Fact]
    public async Task Swagger_when_enabled_documents_the_required_idempotency_key_header()
    {
        using var factory = SchedulerApiFactory.For(fixture).WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" })));
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/swagger/v1/swagger.json");

        using var document = JsonDocument.Parse(json);
        var post = document.RootElement.GetProperty("paths").GetProperty("/api/appointments").GetProperty("post");
        var header = post.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Idempotency-Key");
        header.GetProperty("in").GetString().Should().Be("header");
        header.GetProperty("required").GetBoolean().Should().BeTrue();
        post.GetProperty("summary").GetString().Should().Contain("Books a service");
    }

    [Fact]
    public async Task Swagger_is_off_outside_development_by_default()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://localhost:3000")]
    public async Task Cors_allows_the_web_origins_and_exposes_location(string origin)
    {
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/appointments");
        preflight.Headers.Add("Origin", origin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,idempotency-key");
        using var preflightResponse = await _client.SendAsync(preflight);

        preflightResponse.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);

        using var get = new HttpRequestMessage(HttpMethod.Get, "/api/dealerships");
        get.Headers.Add("Origin", origin);
        using var getResponse = await _client.SendAsync(get);
        getResponse.Headers.GetValues("Access-Control-Expose-Headers").Single().Should().Contain("Location");
    }

    [Fact]
    public async Task Cors_does_not_allow_other_origins()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dealerships");
        request.Headers.Add("Origin", "http://evil.example");
        using var response = await _client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
