using System.Net;
using Scheduler.IntegrationTests.Infrastructure;

namespace Scheduler.IntegrationTests.Api;

/// <summary>SPEC section 6: GET /api/dealerships/{id}/setup on the seed data.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class DealershipSetupApiTests(SqlServerFixture fixture)
{
    private readonly HttpClient _client = SchedulerApiFactory.For(fixture).CreateClient();

    [Fact]
    public async Task Dealership_1_setup_is_exactly_the_seed()
    {
        var json = await _client.GetStringAsync("/api/dealerships/1/setup");

        json.Should().Be(
            "{\"dealership\":{\"id\":1,\"name\":\"District 1 Service Centre\",\"timeZoneId\":\"Asia/Ho_Chi_Minh\",\"openTime\":\"08:00\",\"closeTime\":\"17:00\",\"isActive\":true},"
            + "\"serviceTypes\":["
            + "{\"id\":1,\"name\":\"Oil change\",\"durationMinutes\":60,\"requiredSkill\":\"General\",\"minimumTier\":\"Trainee\",\"requiredBayType\":\"Standard\",\"isActive\":true,\"qualifiedTechnicianIds\":[1,2,3],\"matchingBayIds\":[1]},"
            + "{\"id\":2,\"name\":\"Brake inspection\",\"durationMinutes\":90,\"requiredSkill\":\"Brakes\",\"minimumTier\":\"Qualified\",\"requiredBayType\":\"Lift\",\"isActive\":true,\"qualifiedTechnicianIds\":[1,3],\"matchingBayIds\":[2]},"
            + "{\"id\":3,\"name\":\"Tyre rotation\",\"durationMinutes\":30,\"requiredSkill\":\"Tyres\",\"minimumTier\":\"Trainee\",\"requiredBayType\":\"Lift\",\"isActive\":true,\"qualifiedTechnicianIds\":[1,2],\"matchingBayIds\":[2]},"
            + "{\"id\":4,\"name\":\"EV battery check\",\"durationMinutes\":60,\"requiredSkill\":\"EV\",\"minimumTier\":\"Senior\",\"requiredBayType\":\"EV\",\"isActive\":true,\"qualifiedTechnicianIds\":[2],\"matchingBayIds\":[3]}],"
            + "\"technicians\":["
            + "{\"id\":2,\"name\":\"Lan\",\"shiftStart\":\"08:00\",\"shiftEnd\":\"17:00\",\"isActive\":true,\"skills\":[{\"skill\":\"General\",\"tier\":\"Qualified\"},{\"skill\":\"Tyres\",\"tier\":\"Qualified\"},{\"skill\":\"EV\",\"tier\":\"Senior\"}]},"
            + "{\"id\":3,\"name\":\"Minh\",\"shiftStart\":\"12:00\",\"shiftEnd\":\"17:00\",\"isActive\":true,\"skills\":[{\"skill\":\"General\",\"tier\":\"Trainee\"},{\"skill\":\"Brakes\",\"tier\":\"Qualified\"}]},"
            + "{\"id\":1,\"name\":\"Nam\",\"shiftStart\":\"08:00\",\"shiftEnd\":\"17:00\",\"isActive\":true,\"skills\":[{\"skill\":\"General\",\"tier\":\"Master\"},{\"skill\":\"Brakes\",\"tier\":\"Senior\"},{\"skill\":\"Tyres\",\"tier\":\"Senior\"}]},"
            + "{\"id\":5,\"name\":\"Tuan\",\"shiftStart\":\"08:00\",\"shiftEnd\":\"17:00\",\"isActive\":false,\"skills\":[{\"skill\":\"General\",\"tier\":\"Master\"},{\"skill\":\"Brakes\",\"tier\":\"Master\"},{\"skill\":\"EV\",\"tier\":\"Master\"}]}],"
            + "\"serviceBays\":["
            + "{\"id\":1,\"name\":\"Bay 1\",\"bayType\":\"Standard\",\"isActive\":true},"
            + "{\"id\":2,\"name\":\"Bay 2\",\"bayType\":\"Lift\",\"isActive\":true},"
            + "{\"id\":3,\"name\":\"Bay 3\",\"bayType\":\"EV\",\"isActive\":true},"
            + "{\"id\":5,\"name\":\"Bay 4\",\"bayType\":\"Lift\",\"isActive\":false}]}");
    }

    [Fact]
    public async Task Dealership_2_setup_is_exactly_the_seed()
    {
        var json = await _client.GetStringAsync("/api/dealerships/2/setup");

        json.Should().Be(
            "{\"dealership\":{\"id\":2,\"name\":\"Thu Duc Service Centre\",\"timeZoneId\":\"Asia/Ho_Chi_Minh\",\"openTime\":\"07:30\",\"closeTime\":\"18:00\",\"isActive\":true},"
            + "\"serviceTypes\":[{\"id\":5,\"name\":\"Oil change\",\"durationMinutes\":60,\"requiredSkill\":\"General\",\"minimumTier\":\"Trainee\",\"requiredBayType\":\"Standard\",\"isActive\":true,\"qualifiedTechnicianIds\":[4],\"matchingBayIds\":[4]}],"
            + "\"technicians\":[{\"id\":4,\"name\":\"Hoa\",\"shiftStart\":\"07:30\",\"shiftEnd\":\"18:00\",\"isActive\":true,\"skills\":[{\"skill\":\"General\",\"tier\":\"Senior\"}]}],"
            + "\"serviceBays\":[{\"id\":4,\"name\":\"Bay A\",\"bayType\":\"Standard\",\"isActive\":true}]}");
    }

    [Theory]
    [InlineData("/api/dealerships/99/setup", HttpStatusCode.NotFound, "NOT_FOUND", "Dealership 99 was not found.")]
    [InlineData("/api/dealerships/abc/setup", HttpStatusCode.NotFound, "NOT_FOUND", null)] // the {id:int} route does not match
    public async Task Unknown_dealership_is_404(string url, HttpStatusCode status, string code, string? detail)
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
