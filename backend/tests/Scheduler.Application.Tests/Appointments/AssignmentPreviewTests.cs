using Scheduler.Application.Allocation;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Appointments.Queries;
using Scheduler.Application.Common;
using Scheduler.Application.Tests.TestSupport;

namespace Scheduler.Application.Tests.Appointments;

/// <summary>SPEC section 6: the assignment preview ("who will serve you") and its parity with booking.</summary>
public class AssignmentPreviewTests
{
    private const int OilChange = 1;
    private const int BrakeInspection = 2;
    private const int TyreRotation = 3;
    private const int EvBatteryCheck = 4;
    private const int OilChangeThuDuc = 5;
    private readonly TestApp _app = new(); // now = Mon 9 Nov 2026 14:00 local; test day is Tue 10 Nov

    private Task<Result<AssignmentPreviewDto>> PreviewAsync(int serviceTypeId, string? startTime, string? vehicleId = null, int dealershipId = 1) =>
        _app.AssignmentPreviewHandler().HandleAsync(
            new GetAssignmentPreviewQuery(dealershipId, serviceTypeId, startTime, vehicleId), CancellationToken.None);

    private async Task<AssignmentPreviewDto> PreviewOkAsync(int serviceTypeId, string startTime, string? vehicleId = null)
    {
        var result = await PreviewAsync(serviceTypeId, startTime, vehicleId);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private static IEnumerable<(string, TechnicianState)> Techs(AssignmentPreviewDto dto) => dto.Technicians.Select(t => (t.Name, t.State));

    private static IEnumerable<(string, ServiceBayState)> Bays(AssignmentPreviewDto dto) => dto.ServiceBays.Select(b => (b.Name, b.State));

    [Fact]
    public async Task Oil_change_at_10_previews_nam_not_lan_because_lan_is_the_only_ev_technician()
    {
        var dto = await PreviewOkAsync(OilChange, Local.Iso(10), "1");

        dto.Status.Should().Be(AssignmentPreviewStatus.Assignable);
        dto.DealershipId.Should().Be(1);
        dto.ServiceTypeId.Should().Be(OilChange);
        dto.TimeZoneId.Should().Be("Asia/Ho_Chi_Minh");
        dto.StartUtc.Should().Be(Local.Utc(10));
        dto.EndUtc.Should().Be(Local.Utc(11));
        dto.Technician.Should().Be(new AppointmentTechnicianDto(1, "Nam"));
        dto.ServiceBay.Should().Be(new AppointmentServiceBayDto(1, "Bay 1", "Standard"));
        Techs(dto).Should().Equal(
            ("Nam", TechnicianState.WillServe),
            ("Lan", TechnicianState.Free),
            ("Minh", TechnicianState.OffShift), // shift starts 12:00
            ("Tuan", TechnicianState.Inactive));
        Bays(dto).Should().Equal(
            ("Bay 1", ServiceBayState.WillServe),
            ("Bay 2", ServiceBayState.WrongType),
            ("Bay 3", ServiceBayState.WrongType),
            ("Bay 4", ServiceBayState.Inactive));
        dto.ServiceBays.Select(b => b.BayType).Should().Equal("Standard", "Lift", "EV", "Lift");
    }

    [Fact]
    public async Task Lists_hold_every_technician_and_bay_of_the_dealership_in_state_then_name_order()
    {
        var dto = await PreviewOkAsync(EvBatteryCheck, Local.Iso(10));

        dto.Technician!.Name.Should().Be("Lan");
        dto.ServiceBay!.Name.Should().Be("Bay 3");
        Techs(dto).Should().Equal(
            ("Lan", TechnicianState.WillServe),
            ("Minh", TechnicianState.NotQualified), // name order inside a group
            ("Nam", TechnicianState.NotQualified),
            ("Tuan", TechnicianState.Inactive));
        dto.Technicians.Select(t => t.Id).Should().NotContain(4, "Hoa works at the other dealership");
        Bays(dto).Should().Equal(
            ("Bay 3", ServiceBayState.WillServe),
            ("Bay 1", ServiceBayState.WrongType),
            ("Bay 2", ServiceBayState.WrongType),
            ("Bay 4", ServiceBayState.Inactive));
    }

    [Fact]
    public async Task Busy_resources_are_busy_and_no_free_pair_is_no_availability()
    {
        await _app.BookAsync(OilChange, Local.Iso(10), "first", vehicleId: 1); // Nam + Bay 1, 10:00-11:00

        var dto = await PreviewOkAsync(OilChange, Local.Iso(10, 30), "2");

        dto.Status.Should().Be(AssignmentPreviewStatus.NoAvailability);
        dto.Technician.Should().BeNull();
        dto.ServiceBay.Should().BeNull();
        Techs(dto).Should().Equal(
            ("Lan", TechnicianState.Free),
            ("Nam", TechnicianState.Busy),
            ("Minh", TechnicianState.OffShift),
            ("Tuan", TechnicianState.Inactive));
        Bays(dto).Should().Equal(
            ("Bay 1", ServiceBayState.Busy),
            ("Bay 2", ServiceBayState.WrongType),
            ("Bay 3", ServiceBayState.WrongType),
            ("Bay 4", ServiceBayState.Inactive));
    }

    [Fact]
    public async Task A_busy_bay_with_free_technicians_is_no_availability()
    {
        await _app.BookAsync(BrakeInspection, Local.Iso(13), "brakes"); // Minh + Bay 2 (the only active lift)

        var dto = await PreviewOkAsync(TyreRotation, Local.Iso(13, 30));

        dto.Status.Should().Be(AssignmentPreviewStatus.NoAvailability);
        Techs(dto).Should().Equal(
            ("Lan", TechnicianState.Free),
            ("Nam", TechnicianState.Free),
            ("Minh", TechnicianState.NotQualified),
            ("Tuan", TechnicianState.Inactive));
        Bays(dto).Should().Equal(
            ("Bay 2", ServiceBayState.Busy),
            ("Bay 1", ServiceBayState.WrongType),
            ("Bay 3", ServiceBayState.WrongType),
            ("Bay 4", ServiceBayState.Inactive));
    }

    [Fact]
    public async Task Vehicle_booked_wins_over_no_availability_and_states_are_still_computed()
    {
        await _app.BookAsync(OilChange, Local.Iso(10), "first", vehicleId: 1);

        var withCar = await PreviewOkAsync(OilChange, Local.Iso(10, 30), "1");
        var withoutCar = await PreviewOkAsync(OilChange, Local.Iso(10, 30));

        withCar.Status.Should().Be(AssignmentPreviewStatus.VehicleBooked);
        withCar.Technician.Should().BeNull();
        withCar.Technicians.Should().NotContain(t => t.State == TechnicianState.WillServe);
        withCar.Technicians.Single(t => t.Name == "Nam").State.Should().Be(TechnicianState.Busy);
        withCar.ServiceBays.Single(b => b.Name == "Bay 1").State.Should().Be(ServiceBayState.Busy);
        withoutCar.Status.Should().Be(AssignmentPreviewStatus.NoAvailability);
    }

    [Fact]
    public async Task Vehicle_booked_at_a_free_time_when_the_car_is_elsewhere()
    {
        await _app.BookAsync(OilChangeThuDuc, Local.Iso(10), "thu-duc", vehicleId: 2, dealershipId: 2);

        var dto = await PreviewOkAsync(OilChange, Local.Iso(9, 15), "2"); // 09:15-10:15 overlaps 10:00-11:00

        dto.Status.Should().Be(AssignmentPreviewStatus.VehicleBooked);
        dto.Technicians.Should().Contain(t => t.Name == "Nam" && t.State == TechnicianState.Free);
        (await PreviewOkAsync(OilChange, Local.Iso(9), "2")).Status.Should().Be(AssignmentPreviewStatus.Assignable, "09:00-10:00 only touches");
    }

    [Fact]
    public async Task Past_wins_over_everything_and_is_not_an_error()
    {
        await _app.BookAsync(OilChange, Local.Iso(10), "first", vehicleId: 1);
        _app.Clock.UtcNow = Local.Utc(10);

        var dto = await PreviewOkAsync(OilChange, Local.Iso(10), "1"); // start == now

        dto.Status.Should().Be(AssignmentPreviewStatus.Past);
        dto.Technician.Should().BeNull();
        dto.ServiceBay.Should().BeNull();
        dto.Technicians.Single(t => t.Name == "Nam").State.Should().Be(TechnicianState.Busy);
        dto.ServiceBays.Should().NotContain(b => b.State == ServiceBayState.WillServe);
    }

    [Theory]
    [InlineData(OilChange, 13, 0)] // Minh is tried first in the afternoon (fewest other services)
    [InlineData(OilChange, 9, 0)]
    [InlineData(BrakeInspection, 8, 0)]
    [InlineData(BrakeInspection, 14, 0)]
    [InlineData(TyreRotation, 12, 30)]
    [InlineData(EvBatteryCheck, 16, 0)]
    public async Task Booking_right_after_an_assignable_preview_gets_exactly_that_technician_and_bay(int serviceTypeId, int hour, int minute)
    {
        await _app.BookAsync(OilChange, Local.Iso(10), "busy-morning", vehicleId: 1); // a confirmed count for Nam

        var preview = await PreviewOkAsync(serviceTypeId, Local.Iso(hour, minute), "2");
        var booked = await _app.BookAsync(serviceTypeId, Local.Iso(hour, minute), "check", vehicleId: 2);

        preview.Status.Should().Be(AssignmentPreviewStatus.Assignable);
        booked.Appointment.Technician.Should().Be(preview.Technician);
        booked.Appointment.ServiceBay.Should().Be(preview.ServiceBay);
    }

    [Fact]
    public async Task Preview_reserves_nothing()
    {
        await PreviewAsync(OilChange, Local.Iso(10), "1");

        _app.Appointments.AddCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null, "startTime is required.")]
    [InlineData("2026-11-10T10:00:00", "startTime must be an ISO-8601 date and time with an offset, for example 2026-11-10T10:30:00+07:00.")]
    [InlineData("2026-11-10", "startTime must be an ISO-8601 date and time with an offset, for example 2026-11-10T10:30:00+07:00.")]
    [InlineData("2026-11-10T10:10:00+07:00", "startTime must be on the 15-minute grid (:00, :15, :30 or :45, with zero seconds).")]
    [InlineData("9999-01-01T10:00:00+07:00", "startTime must be between the years 2000 and 9998.")]
    public async Task Bad_start_time_is_invalid_with_the_booking_messages(string? startTime, string message)
    {
        var result = await PreviewAsync(OilChange, startTime);

        result.Error!.Code.Should().Be(ErrorCodes.InvalidRequest);
        result.Error.FieldErrors.Should().ContainKey("startTime").WhoseValue.Should().Equal(message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    public async Task Bad_vehicle_id_is_invalid(string vehicleId)
    {
        var result = await PreviewAsync(OilChange, Local.Iso(10), vehicleId);

        result.Error!.FieldErrors.Should().ContainKey("vehicleId").WhoseValue.Should().Equal("vehicleId must be a positive number.");
    }

    [Fact]
    public async Task Bad_service_type_id_is_invalid()
    {
        var result = await PreviewAsync(0, Local.Iso(10));

        result.Error!.FieldErrors.Should().ContainKey("serviceTypeId");
    }

    [Theory]
    [InlineData(99, OilChange, "999", "Dealership 99 was not found.")]
    [InlineData(1, 99, "999", "Service type 99 was not found.")]
    [InlineData(1, OilChange, "999", "Vehicle 999 was not found.")]
    public async Task Unknown_ids_are_not_found_in_dealership_service_vehicle_order(int dealershipId, int serviceTypeId, string vehicleId, string message)
    {
        var result = await PreviewAsync(serviceTypeId, Local.Iso(10), vehicleId, dealershipId);

        result.Error!.Code.Should().Be(ErrorCodes.NotFound);
        result.Error.Message.Should().Be(message);
    }

    [Fact]
    public async Task Service_of_another_dealership_and_inactive_dealership_are_rule_violations()
    {
        var otherDealership = await PreviewAsync(OilChangeThuDuc, Local.Iso(10));
        _app.Data.GetDealership(1).Deactivate();
        var inactive = await PreviewAsync(OilChange, Local.Iso(10));

        otherDealership.Error!.Code.Should().Be(ErrorCodes.BookingRuleViolation);
        inactive.Error!.Code.Should().Be(ErrorCodes.BookingRuleViolation);
        inactive.Error.Message.Should().Be(Scheduler.Domain.Errors.DomainErrors.Appointment.DealershipInactive.Message);
    }

    [Theory]
    [InlineData(16, 30)] // ends 17:30
    [InlineData(7, 45)]
    public async Task Outside_opening_hours_is_422_with_bookings_message(int hour, int minute)
    {
        var preview = await PreviewAsync(OilChange, Local.Iso(hour, minute));
        var booking = await _app.BookHandler().HandleAsync(TestApp.Command(OilChange, Local.Iso(hour, minute)), CancellationToken.None);

        preview.Error!.Code.Should().Be(ErrorCodes.BookingRuleViolation);
        preview.Error.Message.Should().Be("The appointment must be inside the dealership opening hours.");
        preview.Error.Should().Be(booking.Error);
    }

    [Fact]
    public async Task A_past_start_outside_opening_hours_is_status_past_not_422()
    {
        var result = await PreviewAsync(OilChange, "2026-11-01T20:00:00+07:00");

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Status.Should().Be(AssignmentPreviewStatus.Past);
        result.Value.Technician.Should().BeNull();
        result.Value.Technicians.Should().HaveCount(4).And.NotContain(t => t.State == TechnicianState.WillServe);
    }

    [Fact]
    public async Task A_future_start_outside_opening_hours_is_still_422()
    {
        var result = await PreviewAsync(OilChange, "2026-11-10T20:00:00+07:00");

        result.Error!.Code.Should().Be(ErrorCodes.BookingRuleViolation);
        result.Error.Message.Should().Be("The appointment must be inside the dealership opening hours.");
    }

    // LoadDayAsync loads taken slots when only one resource kind has candidates (SPEC section 6, assignment preview).

    [Fact]
    public async Task Qualified_technicians_but_no_active_bay_of_the_type_is_no_availability_with_correct_technician_states()
    {
        await _app.BookAsync(BrakeInspection, Local.Iso(13), "brakes"); // Minh + Bay 2, 13:00-14:30
        _app.Data.ServiceBays.Single(b => b.Id == 2).Deactivate(); // no active lift bay left

        var dto = await PreviewOkAsync(BrakeInspection, Local.Iso(13));

        dto.Status.Should().Be(AssignmentPreviewStatus.NoAvailability);
        dto.Technician.Should().BeNull();
        Techs(dto).Should().Equal(
            ("Nam", TechnicianState.Free),
            ("Minh", TechnicianState.Busy),
            ("Lan", TechnicianState.NotQualified),
            ("Tuan", TechnicianState.Inactive));
        Bays(dto).Should().Equal(
            ("Bay 1", ServiceBayState.WrongType),
            ("Bay 3", ServiceBayState.WrongType),
            ("Bay 2", ServiceBayState.Inactive),
            ("Bay 4", ServiceBayState.Inactive));
    }

    [Fact]
    public async Task Bays_but_no_qualified_active_technician_is_no_availability_with_correct_bay_states()
    {
        await _app.BookAsync(EvBatteryCheck, Local.Iso(10), "ev"); // Lan + Bay 3, 10:00-11:00
        _app.Data.GetTechnician(2).Deactivate(); // Lan was the only active EV technician

        var busy = await PreviewOkAsync(EvBatteryCheck, Local.Iso(10, 30));
        var free = await PreviewOkAsync(EvBatteryCheck, Local.Iso(12));

        busy.Status.Should().Be(AssignmentPreviewStatus.NoAvailability);
        free.Status.Should().Be(AssignmentPreviewStatus.NoAvailability);
        busy.ServiceBay.Should().BeNull();
        Bays(busy).Should().Equal(
            ("Bay 3", ServiceBayState.Busy),
            ("Bay 1", ServiceBayState.WrongType),
            ("Bay 2", ServiceBayState.WrongType),
            ("Bay 4", ServiceBayState.Inactive));
        Bays(free).First().Should().Be(("Bay 3", ServiceBayState.Free));
        Techs(busy).Should().Equal(
            ("Minh", TechnicianState.NotQualified),
            ("Nam", TechnicianState.NotQualified),
            ("Lan", TechnicianState.Inactive),
            ("Tuan", TechnicianState.Inactive));
    }
}
