using System.Net;
using System.Text.Json;
using Scheduler.Application.Allocation;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Catalog.Dtos;
using Scheduler.IntegrationTests.Infrastructure;
using static Scheduler.IntegrationTests.Api.ApiClient;

namespace Scheduler.IntegrationTests.Api;

/// <summary>
/// SPEC section 6: GET /api/customers and the assignment preview against the real API and SQL Server.
/// The key invariant: an Assignable preview, booked right away with the same car, gives 201 with that technician and bay.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AssignmentPreviewApiTests(SqlServerFixture fixture)
{
    private const int OilChange = 1;
    private const int BrakeInspection = 2;
    private const int TyreRotation = 3;
    private const int EvBatteryCheck = 4;
    private const int OilChangeThuDuc = 5;
    private readonly HttpClient _client = SchedulerApiFactory.For(fixture).CreateClient();

    private static string Url(int serviceTypeId, string startTime, int? vehicleId = null, int dealershipId = 1) =>
        $"/api/dealerships/{dealershipId}/assignment-preview?serviceTypeId={serviceTypeId}&startTime={Uri.EscapeDataString(startTime)}"
        + (vehicleId is { } v ? $"&vehicleId={v}" : string.Empty);

    private async Task<AssignmentPreviewDto> PreviewAsync(int serviceTypeId, string startTime, int? vehicleId = null)
    {
        using var response = await _client.GetAsync(Url(serviceTypeId, startTime, vehicleId));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<AssignmentPreviewDto>();
    }

    [Fact]
    public async Task Customers_are_listed_by_id_starting_with_the_three_demo_customers()
    {
        var customers = await (await _client.GetAsync("/api/customers")).ReadAsync<List<CustomerDto>>();

        customers.Select(c => c.Id).Should().BeInAscendingOrder();
        customers.Should().StartWith(
        [
            new CustomerDto(1, "An Nguyen", "an.nguyen@example.com", "+84 900 000 001"),
            new CustomerDto(2, "Binh Tran", "binh.tran@example.com", "+84 900 000 002"),
            new CustomerDto(3, "Chi Le", "chi.le@example.com", "+84 900 000 003"),
        ]);
        var chiCars = await (await _client.GetAsync("/api/customers/3/vehicles")).ReadAsync<List<VehicleDto>>();
        chiCars.Select(v => (v.Id, v.Make, v.Model, v.Vin)).Should().Equal((4, "Mazda", "CX-5", "JM3KFBCM1N0000004"), (5, "Kia", "Morning", "KNABE511AL0000005"));
    }

    [Fact]
    public async Task Customers_json_is_camel_case()
    {
        var json = await _client.GetStringAsync("/api/customers");

        json.Should().StartWith("[{\"id\":1,\"name\":\"An Nguyen\",\"email\":\"an.nguyen@example.com\",\"phone\":\"+84 900 000 001\"},");
    }

    [Fact]
    public async Task Preview_on_a_quiet_day_lists_every_resource_with_the_scarcity_pick_first()
    {
        var day = TestDays.PreviewQuietDay;

        var oil = await PreviewAsync(OilChange, LocalIso(day, 10), vehicleId: 1);
        var ev = await PreviewAsync(EvBatteryCheck, LocalIso(day, 10), vehicleId: 1);

        oil.Status.Should().Be(AssignmentPreviewStatus.Assignable);
        oil.StartUtc.Should().Be(LocalToUtc(day, 10));
        oil.EndUtc.Should().Be(LocalToUtc(day, 11));
        oil.Technician.Should().Be(new AppointmentTechnicianDto(1, "Nam")); // Lan is kept for EV work
        oil.ServiceBay.Should().Be(new AppointmentServiceBayDto(1, "Bay 1", "Standard"));
        oil.Technicians.Select(t => (t.Name, t.State)).Should().Equal(
            ("Nam", TechnicianState.WillServe), ("Lan", TechnicianState.Free), ("Minh", TechnicianState.OffShift), ("Tuan", TechnicianState.Inactive));
        oil.ServiceBays.Select(b => (b.Name, b.State)).Should().Equal(
            ("Bay 1", ServiceBayState.WillServe), ("Bay 2", ServiceBayState.WrongType), ("Bay 3", ServiceBayState.WrongType), ("Bay 4", ServiceBayState.Inactive));

        ev.Technician!.Name.Should().Be("Lan");
        ev.ServiceBay!.Name.Should().Be("Bay 3");
        ev.Technicians.Select(t => (t.Name, t.State)).Should().Equal(
            ("Lan", TechnicianState.WillServe), ("Minh", TechnicianState.NotQualified), ("Nam", TechnicianState.NotQualified), ("Tuan", TechnicianState.Inactive));
    }

    [Fact]
    public async Task Preview_json_shape_uses_string_states_and_null_pick_when_not_assignable()
    {
        var day = TestDays.PreviewQuietDay;

        var assignable = await _client.GetStringAsync(Url(EvBatteryCheck, LocalIso(day, 10)));
        var past = await _client.GetStringAsync(Url(EvBatteryCheck, "2029-12-31T10:00:00+07:00"));

        assignable.Should().Be(
            "{\"dealershipId\":1,\"serviceTypeId\":4,\"timeZoneId\":\"Asia/Ho_Chi_Minh\","
            + $"\"startUtc\":\"{UtcIso(LocalToUtc(day, 10))}\",\"endUtc\":\"{UtcIso(LocalToUtc(day, 11))}\","
            + "\"status\":\"Assignable\",\"technician\":{\"id\":2,\"name\":\"Lan\"},\"serviceBay\":{\"id\":3,\"name\":\"Bay 3\",\"bayType\":\"EV\"},"
            + "\"technicians\":[{\"id\":2,\"name\":\"Lan\",\"state\":\"WillServe\"},{\"id\":3,\"name\":\"Minh\",\"state\":\"NotQualified\"},"
            + "{\"id\":1,\"name\":\"Nam\",\"state\":\"NotQualified\"},{\"id\":5,\"name\":\"Tuan\",\"state\":\"Inactive\"}],"
            + "\"serviceBays\":[{\"id\":3,\"name\":\"Bay 3\",\"bayType\":\"EV\",\"state\":\"WillServe\"},{\"id\":1,\"name\":\"Bay 1\",\"bayType\":\"Standard\",\"state\":\"WrongType\"},"
            + "{\"id\":2,\"name\":\"Bay 2\",\"bayType\":\"Lift\",\"state\":\"WrongType\"},{\"id\":5,\"name\":\"Bay 4\",\"bayType\":\"Lift\",\"state\":\"Inactive\"}]}");
        past.Should().Contain("\"status\":\"Past\",\"technician\":null,\"serviceBay\":null,");
        past.Should().Contain("{\"id\":2,\"name\":\"Lan\",\"state\":\"Free\"}");
    }

    [Fact]
    public async Task A_car_busy_at_another_dealership_is_vehicle_booked_and_booking_agrees()
    {
        var day = TestDays.PreviewCarBusy;
        using (var setup = await _client.BookAsync(OilChangeThuDuc, LocalIso(day, 10), vehicleId: 2, dealershipId: 2))
        {
            setup.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var preview = await PreviewAsync(OilChange, LocalIso(day, 9, 30), vehicleId: 2);
        var otherCar = await PreviewAsync(OilChange, LocalIso(day, 9, 30), vehicleId: 1);

        preview.Status.Should().Be(AssignmentPreviewStatus.VehicleBooked);
        preview.Technician.Should().BeNull();
        preview.Technicians.Should().Contain(t => t.Name == "Nam" && t.State == TechnicianState.Free);
        otherCar.Status.Should().Be(AssignmentPreviewStatus.Assignable);
        using var booking = await _client.BookAsync(OilChange, LocalIso(day, 9, 30), vehicleId: 2);
        booking.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await booking.ReadProblemAsync()).Code().Should().Be("VEHICLE_ALREADY_BOOKED");
    }

    [Fact]
    public async Task Every_assignable_preview_on_a_busy_day_books_201_with_that_technician_and_bay()
    {
        var day = TestDays.PreviewBusyDay;
        var (customerId, cars) = await TestFleet.EnsureAsync(fixture, 60);
        var nextCar = 0;
        (int ServiceTypeId, int Hour, int Minute)[] samples =
        [
            (OilChange, 10, 0), (EvBatteryCheck, 10, 0), (OilChange, 10, 0), (OilChange, 10, 0),
            (BrakeInspection, 8, 0), (TyreRotation, 9, 30), (BrakeInspection, 9, 0), (TyreRotation, 9, 45),
            (OilChange, 13, 0), (OilChange, 13, 0), (OilChange, 13, 0), (OilChange, 13, 15),
            (EvBatteryCheck, 13, 0), (EvBatteryCheck, 13, 30), (BrakeInspection, 12, 30), (TyreRotation, 14, 0),
            (BrakeInspection, 14, 0), (OilChange, 16, 0), (OilChange, 16, 0), (OilChange, 16, 0), (EvBatteryCheck, 16, 0),
        ];

        var assignable = 0;
        var noAvailability = 0;
        var picks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (serviceTypeId, hour, minute) in samples)
        {
            var car = cars[nextCar++];
            var start = LocalIso(day, hour, minute);
            var preview = await PreviewAsync(serviceTypeId, start, car);
            using var response = await _client.BookAsync(serviceTypeId, start, customerId: customerId, vehicleId: car);
            var text = await response.Content.ReadAsStringAsync();
            var because = $"service {serviceTypeId} at {hour:00}:{minute:00}: {text}";

            if (preview.Status == AssignmentPreviewStatus.Assignable)
            {
                assignable++;
                response.StatusCode.Should().Be(HttpStatusCode.Created, because);
                var booked = JsonSerializer.Deserialize<AppointmentDto>(text, Json)!;
                booked.Technician.Should().Be(preview.Technician, because);
                booked.ServiceBay.Should().Be(preview.ServiceBay, because);
                preview.Technicians.Single(t => t.State == TechnicianState.WillServe).Id.Should().Be(booked.Technician.Id);
                preview.ServiceBays.Single(b => b.State == ServiceBayState.WillServe).Id.Should().Be(booked.ServiceBay.Id);
                picks.Add($"{booked.Technician.Name}/{booked.ServiceBay.Name}");
            }
            else
            {
                preview.Status.Should().Be(AssignmentPreviewStatus.NoAvailability, because);
                response.StatusCode.Should().Be(HttpStatusCode.Conflict, because);
                text.Should().Contain("NO_AVAILABILITY");
                noAvailability++;
            }
        }

        assignable.Should().BeGreaterThanOrEqualTo(10);
        noAvailability.Should().BeGreaterThan(0, "the busy day must also exercise NoAvailability");
        picks.Count.Should().BeGreaterThan(2, "different technicians and bays were picked");
    }

    [Theory]
    [InlineData("/api/dealerships/1/assignment-preview?serviceTypeId=1", "startTime", "startTime is required.")]
    [InlineData("/api/dealerships/1/assignment-preview?serviceTypeId=1&startTime=2030-10-01T10:00:00", "startTime", "startTime must be an ISO-8601 date and time with an offset, for example 2026-11-10T10:30:00+07:00.")]
    [InlineData("/api/dealerships/1/assignment-preview?serviceTypeId=1&startTime=2030-10-01T10:05:00Z", "startTime", "startTime must be on the 15-minute grid (:00, :15, :30 or :45, with zero seconds).")]
    [InlineData("/api/dealerships/1/assignment-preview?serviceTypeId=1&startTime=9999-01-01T10:00:00Z", "startTime", "startTime must be between the years 2000 and 9998.")]
    [InlineData("/api/dealerships/1/assignment-preview?serviceTypeId=1&startTime=2030-10-01T03:00:00Z&vehicleId=0", "vehicleId", "vehicleId must be a positive number.")]
    [InlineData("/api/dealerships/1/assignment-preview?serviceTypeId=1&startTime=2030-10-01T03:00:00Z&vehicleId=", "vehicleId", "vehicleId must be a positive number.")]
    [InlineData("/api/dealerships/1/assignment-preview?serviceTypeId=1&startTime=2030-10-01T03:00:00Z&vehicleId=x", "vehicleId", "vehicleId must be a positive number.")]
    [InlineData("/api/dealerships/1/assignment-preview?serviceTypeId=0&startTime=2030-10-01T03:00:00Z", "serviceTypeId", "serviceTypeId is required.")]
    public async Task Bad_parameters_are_400_with_the_field(string url, string field, string message)
    {
        using var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync();
        problem.Code().Should().Be("INVALID_REQUEST");
        problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()).Should().Equal(message);
    }

    [Theory]
    [InlineData(99, OilChange, 987654, HttpStatusCode.NotFound, "Dealership 99 was not found.")]
    [InlineData(1, 99, 987654, HttpStatusCode.NotFound, "Service type 99 was not found.")]
    [InlineData(1, OilChange, 987654, HttpStatusCode.NotFound, "Vehicle 987654 was not found.")]
    [InlineData(1, OilChangeThuDuc, 1, HttpStatusCode.UnprocessableEntity, "The service type is not offered at this dealership.")]
    public async Task Unknown_ids_are_404_and_rule_violations_422(int dealershipId, int serviceTypeId, int vehicleId, HttpStatusCode status, string detail)
    {
        using var response = await _client.GetAsync(Url(serviceTypeId, LocalIso(TestDays.PreviewQuietDay, 10), vehicleId, dealershipId));

        response.StatusCode.Should().Be(status);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("detail").GetString().Should().Be(detail);
    }

    [Fact]
    public async Task Outside_opening_hours_is_422_with_bookings_exact_message()
    {
        var start = LocalIso(TestDays.PreviewQuietDay, 16, 30); // oil change would end 17:30

        using var preview = await _client.GetAsync(Url(OilChange, start));
        using var booking = await _client.BookAsync(OilChange, start);

        preview.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        booking.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var previewProblem = await preview.ReadProblemAsync();
        var bookingProblem = await booking.ReadProblemAsync();
        previewProblem.Code().Should().Be("BOOKING_RULE_VIOLATION");
        previewProblem.GetProperty("detail").GetString().Should().Be("The appointment must be inside the dealership opening hours.")
            .And.Be(bookingProblem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_past_start_outside_opening_hours_is_200_past()
    {
        var preview = await PreviewAsync(OilChange, "2029-12-31T20:00:00+07:00"); // before FixedClock.DefaultNow, after closing

        preview.Status.Should().Be(AssignmentPreviewStatus.Past);
        preview.Technician.Should().BeNull();
        preview.ServiceBay.Should().BeNull();
    }
}
