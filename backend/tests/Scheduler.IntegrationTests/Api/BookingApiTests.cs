using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Domain.Entities;
using Scheduler.IntegrationTests.Infrastructure;
using static Scheduler.IntegrationTests.Api.ApiClient;

namespace Scheduler.IntegrationTests.Api;

[Collection(SqlServerCollection.Name)]
public sealed class BookingApiTests(SqlServerFixture fixture)
{
    private const int OilChange = 1;
    private const int BrakeInspection = 2;
    private readonly SchedulerApiFactory _factory = SchedulerApiFactory.For(fixture);
    private HttpClient Client => _client ??= _factory.CreateClient();
    private HttpClient? _client;

    [Fact]
    public async Task Book_returns_201_with_location_and_body_then_get_by_id_returns_it()
    {
        var day = TestDays.BookAndRead;

        using var response = await Client.BookAsync(BrakeInspection, LocalIso(day, 13));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booked = await response.ReadAsync<AppointmentDto>();
        response.Headers.Location!.OriginalString.Should().Be($"/api/appointments/{booked.Id}");
        booked.Status.Should().Be("Confirmed");
        booked.StartUtc.Should().Be(LocalToUtc(day, 13));
        booked.EndUtc.Should().Be(LocalToUtc(day, 14, 30));
        booked.CustomerId.Should().Be(1);
        booked.Vehicle.Should().Be(new AppointmentVehicleDto(1, "Toyota", "Vios", 2021, "JTDBT923X71000001"));
        booked.Dealership.Should().Be(new AppointmentDealershipDto(1, "District 1 Service Centre", "Asia/Ho_Chi_Minh"));
        booked.ServiceType.Should().Be(new AppointmentServiceTypeDto(BrakeInspection, "Brake inspection", 90));
        booked.Technician.Name.Should().Be("Minh");
        booked.ServiceBay.Should().Be(new AppointmentServiceBayDto(2, "Bay 2", "Lift"));

        var read = await (await Client.GetAsync($"/api/appointments/{booked.Id}")).ReadAsync<AppointmentDto>();
        read.Should().BeEquivalentTo(booked);
    }

    [Fact]
    public async Task Response_times_are_utc_iso_strings_with_z()
    {
        using var response = await Client.BookAsync(OilChange, LocalIso(TestDays.BookAndRead, 9));
        var json = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("startUtc").GetString().Should().Be(UtcIso(LocalToUtc(TestDays.BookAndRead, 9)));
        document.RootElement.GetProperty("createdUtc").GetString().Should().EndWith("Z");
        document.RootElement.GetProperty("status").GetString().Should().Be("Confirmed");
    }

    [Fact]
    public async Task List_is_newest_first()
    {
        var day = TestDays.ListNewestFirst;
        var early = await Client.BookOkAsync(OilChange, LocalIso(day, 9));
        var late = await Client.BookOkAsync(OilChange, LocalIso(day, 15));
        var middle = await Client.BookOkAsync(OilChange, LocalIso(day, 11));

        var list = await (await Client.GetAsync("/api/appointments?customerId=1")).ReadAsync<List<AppointmentDto>>();

        list.Select(a => a.StartUtc).Should().BeInDescendingOrder();
        list.Select(a => a.Id).Where(id => id == early.Id || id == late.Id || id == middle.Id)
            .Should().Equal(late.Id, middle.Id, early.Id);
    }

    [Fact]
    public async Task List_for_an_unknown_customer_is_empty_and_missing_customer_is_400()
    {
        (await (await Client.GetAsync("/api/appointments?customerId=999999")).ReadAsync<List<AppointmentDto>>()).Should().BeEmpty();

        using var response = await Client.GetAsync("/api/appointments");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Code().Should().Be("INVALID_REQUEST");
    }

    [Fact]
    public async Task Get_unknown_appointment_is_404()
    {
        using var response = await Client.GetAsync("/api/appointments/987654321");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).Code().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Brake_inspection_at_13_00_goes_to_Minh_the_least_qualified()
    {
        var booked = await Client.BookOkAsync(BrakeInspection, LocalIso(TestDays.LeastQualifiedAfternoon, 13));

        booked.Technician.Name.Should().Be("Minh");
    }

    [Fact]
    public async Task Brake_inspection_at_09_00_goes_to_Nam_because_Minh_starts_at_12_00()
    {
        var booked = await Client.BookOkAsync(BrakeInspection, LocalIso(TestDays.LeastQualifiedMorning, 9));

        booked.Technician.Name.Should().Be("Nam");
    }

    [Theory]
    [InlineData(16, 0)]  // 16:00 + 90 = 17:30, after close
    [InlineData(7, 30)]  // before opening
    public async Task Outside_opening_hours_is_422(int hour, int minute)
    {
        using var response = await Client.BookAsync(BrakeInspection, LocalIso(TestDays.OutsideHours, hour, minute));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code().Should().Be("BOOKING_RULE_VIOLATION");
    }

    [Fact]
    public async Task Ending_exactly_at_close_is_allowed()
    {
        (await Client.BookOkAsync(BrakeInspection, LocalIso(TestDays.OutsideHours, 15, 30))).EndUtc
            .Should().Be(LocalToUtc(TestDays.OutsideHours, 17));
    }

    [Theory]
    [InlineData("2030-06-14T10:10:00+07:00", "startTime")] // off grid
    [InlineData("2030-06-14T10:00:30+07:00", "startTime")] // seconds
    [InlineData("2030-06-14T10:00:00", "startTime")]       // no offset
    [InlineData("2029-12-31T10:00:00+07:00", "startTime")] // past (FixedClock is 2030-01-01)
    [InlineData("tomorrow", "startTime")]
    public async Task Bad_start_time_is_400_with_a_field_error(string startTime, string field)
    {
        using var response = await Client.BookAsync(OilChange, startTime);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync();
        problem.Code().Should().Be("INVALID_REQUEST");
        problem.GetProperty("errors").GetProperty(field).GetArrayLength().Should().BePositive();
    }

    [Fact]
    public async Task Missing_idempotency_key_is_400()
    {
        var body = new { customerId = 1, vehicleId = 1, dealershipId = 1, serviceTypeId = OilChange, startTime = LocalIso(TestDays.BadRequests, 10) };
        using var response = await Client.PostWithKeyAsync("/api/appointments", body, idempotencyKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync();
        problem.Code().Should().Be("INVALID_REQUEST");
        problem.GetProperty("errors").TryGetProperty("Idempotency-Key", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Too_long_idempotency_key_is_400()
    {
        using var response = await Client.BookAsync(OilChange, LocalIso(TestDays.BadRequests, 10), new string('k', 101));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"customerId\":\"one\"}")]
    [InlineData("")]
    public async Task Unreadable_body_is_400_invalid_request(string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/appointments")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Code().Should().Be("INVALID_REQUEST");
    }

    [Fact]
    public async Task Wrong_content_type_is_415_with_a_code()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/appointments") { Content = new StringContent("x") };
        request.Headers.Add("Idempotency-Key", "k");
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await response.ReadProblemAsync()).Code().Should().Be("INVALID_REQUEST");
    }

    [Theory]
    [InlineData(999999, 1, 1, OilChange)]
    [InlineData(1, 999999, 1, OilChange)]
    [InlineData(1, 1, 999999, OilChange)]
    [InlineData(1, 1, 1, 999999)]
    public async Task Unknown_ids_are_404(int customerId, int vehicleId, int dealershipId, int serviceTypeId)
    {
        using var response = await Client.BookAsync(serviceTypeId, LocalIso(TestDays.BadRequests, 10), customerId: customerId, vehicleId: vehicleId, dealershipId: dealershipId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).Code().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Vehicle_of_another_customer_is_422()
    {
        var otherVehicleId = await EnsureOtherCustomerVehicleAsync();

        using var response = await Client.BookAsync(OilChange, LocalIso(TestDays.OtherCustomer, 10), vehicleId: otherVehicleId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code().Should().Be("BOOKING_RULE_VIOLATION");
    }

    [Fact]
    public async Task Service_of_another_dealership_is_422()
    {
        using var response = await Client.BookAsync(5, LocalIso(TestDays.OtherCustomer, 10));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private async Task<int> EnsureOtherCustomerVehicleAsync()
    {
        const string vin = "TESTOTHERCUST0001";
        await using var db = fixture.CreateDbContext();
        var existing = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Vin == vin);
        if (existing is not null)
        {
            return existing.Id;
        }

        var customer = Customer.Create("Other Customer", "other@example.com", "+84 900 000 001");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var vehicle = Vehicle.Create(customer.Id, vin, "Kia", "Morning", 2020);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return vehicle.Id;
    }
}
