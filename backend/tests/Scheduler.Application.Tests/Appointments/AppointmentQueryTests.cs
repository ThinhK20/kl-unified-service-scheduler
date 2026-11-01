using Scheduler.Application.Appointments.Queries;
using Scheduler.Application.Common;
using Scheduler.Application.Tests.TestSupport;

namespace Scheduler.Application.Tests.Appointments;

public class AppointmentQueryTests
{
    private const int OilChange = 1;
    private const int BrakeInspection = 2;
    private const int OilChangeThuDuc = 5;
    private readonly TestApp _app = new();

    private Task<Result<Scheduler.Application.Appointments.Dtos.AvailabilityDto>> Availability(int dealershipId, int serviceTypeId, string? date) =>
        _app.AvailabilityHandler().HandleAsync(new GetAvailabilityQuery(dealershipId, serviceTypeId, date), CancellationToken.None);

    [Fact]
    public async Task Availability_lists_every_grid_start_where_the_whole_job_fits()
    {
        var result = await Availability(1, BrakeInspection, "2026-11-10");

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Date.Should().Be("2026-11-10");
        dto.TimeZoneId.Should().Be("Asia/Ho_Chi_Minh");
        dto.DurationMinutes.Should().Be(90);
        // 08:00 ... 15:30 local = 31 starts; Nam covers the whole day.
        dto.Slots.Should().HaveCount(31);
        dto.Slots[0].Should().Be(new Scheduler.Application.Appointments.Dtos.TimeSlotDto(Local.Utc(8), Local.Utc(9, 30)));
        dto.Slots[^1].StartUtc.Should().Be(Local.Utc(15, 30));
        dto.Slots[^1].EndUtc.Should().Be(Local.Utc(17));
    }

    [Fact]
    public async Task Availability_removes_starts_that_overlap_a_booked_bay()
    {
        await _app.BookAsync(BrakeInspection, Local.Iso(13), "a"); // Bay 2 busy 13:00-14:30

        var result = await Availability(1, BrakeInspection, "2026-11-10");

        // Starts 11:45 ... 14:15 (11 of them) would overlap.
        result.Value.Slots.Should().HaveCount(20);
        result.Value.Slots.Select(s => s.StartUtc).Should().NotContain(Local.Utc(11, 45)).And.NotContain(Local.Utc(14, 15))
            .And.Contain(Local.Utc(11, 30)).And.Contain(Local.Utc(14, 30));
    }

    [Fact]
    public async Task Availability_only_lists_future_starts()
    {
        _app.Clock.UtcNow = Local.Utc(14, 5);

        var result = await Availability(1, BrakeInspection, "2026-11-10");

        result.Value.Slots.Select(s => s.StartUtc).Should().Equal(Local.Utc(14, 15), Local.Utc(14, 30), Local.Utc(14, 45), Local.Utc(15), Local.Utc(15, 15), Local.Utc(15, 30));
    }

    [Fact]
    public async Task Availability_for_a_past_date_is_empty()
    {
        var result = await Availability(1, OilChange, "2026-11-01");

        result.Value.Slots.Should().BeEmpty();
    }

    [Fact]
    public async Task Availability_reserves_nothing()
    {
        await Availability(1, OilChange, "2026-11-10");

        _app.Appointments.AddCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(99, OilChange)]
    [InlineData(1, 99)]
    public async Task Availability_unknown_dealership_or_service_is_not_found(int dealershipId, int serviceTypeId)
    {
        var result = await Availability(dealershipId, serviceTypeId, "2026-11-10");

        result.Error!.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Availability_service_of_another_dealership_is_a_rule_violation()
    {
        var result = await Availability(1, OilChangeThuDuc, "2026-11-10");

        result.Error!.Code.Should().Be(ErrorCodes.BookingRuleViolation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("10/11/2026")]
    [InlineData("2026-11-10T00:00:00Z")]
    [InlineData("2026-02-30")]
    [InlineData("9999-12-31")]
    [InlineData("1999-12-31")]
    [InlineData("0001-01-01")]
    public async Task Availability_bad_date_is_invalid(string? date)
    {
        var result = await Availability(1, OilChange, date);

        result.Error!.Code.Should().Be(ErrorCodes.InvalidRequest);
        result.Error.FieldErrors.Should().ContainKey("date");
    }

    [Fact]
    public async Task Appointments_are_newest_first()
    {
        await _app.BookAsync(OilChange, Local.Iso(9), "a");
        await _app.BookAsync(OilChange, Local.Iso(15), "b");
        await _app.BookAsync(OilChange, Local.Iso(11), "c");

        var result = await _app.AppointmentsHandler().HandleAsync(new GetAppointmentsQuery(1), CancellationToken.None);

        result.Value.Select(a => a.StartUtc).Should().Equal(Local.Utc(15), Local.Utc(11), Local.Utc(9));
    }

    [Fact]
    public async Task Appointments_of_a_customer_without_any_is_an_empty_list()
    {
        var result = await _app.AppointmentsHandler().HandleAsync(new GetAppointmentsQuery(2), CancellationToken.None);

        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Appointment_by_id_returns_the_details()
    {
        var booked = await _app.BookAsync(BrakeInspection, Local.Iso(13), "a");

        var result = await _app.AppointmentByIdHandler().HandleAsync(new GetAppointmentByIdQuery(booked.Appointment.Id), CancellationToken.None);

        result.Value.Should().BeEquivalentTo(booked.Appointment);
        result.Value.Vehicle.Vin.Should().Be("JTDBT923X71000001");
        result.Value.Dealership.TimeZoneId.Should().Be("Asia/Ho_Chi_Minh");
        result.Value.ServiceBay.BayType.Should().Be("Lift");
    }

    [Fact]
    public async Task Appointment_by_unknown_id_is_not_found()
    {
        var result = await _app.AppointmentByIdHandler().HandleAsync(new GetAppointmentByIdQuery(4040), CancellationToken.None);

        result.Error!.Code.Should().Be(ErrorCodes.NotFound);
    }
}
