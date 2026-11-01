using Scheduler.Application.Abstractions;
using Scheduler.Application.Common;
using Scheduler.Application.Tests.TestSupport;
using Scheduler.Domain.Enums;
using Scheduler.Application.Appointments.Book;

namespace Scheduler.Application.Tests.Appointments;

public class BookAppointmentHandlerTests
{
    private const int OilChange = 1;
    private const int BrakeInspection = 2;
    private const int EvBatteryCheck = 4;
    private const int OilChangeThuDuc = 5;

    private readonly TestApp _app = new();

    private Task<Result<BookAppointmentResult>> Book(
        int serviceTypeId, string startTime, string key = "key-1", int customerId = 1, int vehicleId = 1, int dealershipId = 1) =>
        _app.BookHandler().HandleAsync(TestApp.Command(serviceTypeId, startTime, key, customerId, vehicleId, dealershipId), CancellationToken.None);

    // ---------- Allocation ----------

    [Fact]
    public async Task Brake_inspection_at_13_00_picks_least_qualified_Minh_over_Nam()
    {
        var result = await Book(BrakeInspection, Local.Iso(13));

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value.Appointment;
        dto.Technician.Name.Should().Be("Minh");
        dto.ServiceBay.Name.Should().Be("Bay 2");
        dto.StartUtc.Should().Be(Local.Utc(13));
        dto.EndUtc.Should().Be(Local.Utc(14, 30));
        dto.Status.Should().Be("Confirmed");
        dto.ServiceType.DurationMinutes.Should().Be(90);
        result.Value.IsReplay.Should().BeFalse();
    }

    [Fact]
    public async Task Brake_inspection_at_09_00_picks_Nam_because_Minh_starts_at_12_00()
    {
        var result = await Book(BrakeInspection, Local.Iso(9));

        result.Value.Appointment.Technician.Name.Should().Be("Nam");
    }

    [Fact]
    public async Task Saved_appointment_has_slot_rows_for_technician_and_bay()
    {
        await Book(BrakeInspection, Local.Iso(13));

        var saved = _app.Data.Appointments.Single();
        saved.ResourceSlots.Should().HaveCount(12); // 90 min = 6 slots x (technician + bay)
        saved.RequiredSkillSnapshot.Should().Be(Skill.Brakes);
        saved.RequiredBayTypeSnapshot.Should().Be(BayType.Lift);
        saved.CreatedUtc.Should().Be(_app.Clock.UtcNow);
    }

    [Fact]
    public async Task Inactive_master_technician_is_skipped()
    {
        // EV battery check needs EV Senior: Lan (Senior) or Tuan (Master, inactive).
        var first = await Book(EvBatteryCheck, Local.Iso(10), key: "a");
        first.Value.Appointment.Technician.Name.Should().Be("Lan");

        // Lan is busy now; only the inactive Tuan would qualify.
        _app.Data.ServiceBays.Add(Domain.Entities.ServiceBay.Create(1, "Spare EV bay", BayType.EV).WithId(6));
        var second = await Book(EvBatteryCheck, Local.Iso(10), key: "b");

        second.Error!.Code.Should().Be(ErrorCodes.NoAvailability);
    }

    [Fact]
    public async Task Inactive_bay_and_wrong_type_bay_are_skipped()
    {
        // Bay 2 is the only ACTIVE Lift bay. Bay 4 (Lift) is inactive; Bays 1 and 3 are the wrong type.
        await Book(BrakeInspection, Local.Iso(13), key: "a");

        // Nam is free and qualified, but no lift bay is free.
        var result = await Book(BrakeInspection, Local.Iso(13), key: "b");

        result.Error!.Code.Should().Be(ErrorCodes.NoAvailability);
        _app.Appointments.AddCalls.Should().Be(1, "no pair should even be tried");
    }

    [Fact]
    public async Task Unqualified_technician_is_skipped()
    {
        // Brakes: Lan has no Brakes skill. With Minh off shift at 09:00 and Nam busy, nobody is left.
        _app.Data.ServiceBays.Add(Domain.Entities.ServiceBay.Create(1, "Bay 5", BayType.Lift).WithId(7));
        (await Book(BrakeInspection, Local.Iso(9), key: "a")).Value.Appointment.Technician.Name.Should().Be("Nam");

        var result = await Book(BrakeInspection, Local.Iso(9), key: "b");

        result.Error!.Code.Should().Be(ErrorCodes.NoAvailability);
    }

    [Fact]
    public async Task Off_shift_technician_is_skipped()
    {
        // 11:00-12:30 local overlaps Minh's 12:00 start, so only Nam can do it.
        var result = await Book(BrakeInspection, Local.Iso(11));

        result.Value.Appointment.Technician.Name.Should().Be("Nam");
    }

    [Fact]
    public async Task Fewest_confirmed_appointments_that_day_breaks_a_tier_tie()
    {
        // Binh (Id 8) is a second General Trainee working all day, like Minh (Id 3) from 12:00.
        _app.Data.Technicians.Add(Domain.Entities.Technician.Create(1, "Binh", new TimeOnly(8, 0), new TimeOnly(17, 0))
            .WithSkill(Skill.General, SkillTier.Trainee).WithId(8));

        // Same tier, both 0 appointments: lower Id wins.
        (await Book(OilChange, Local.Iso(13), key: "a")).Value.Appointment.Technician.Name.Should().Be("Minh");

        // Same tier, Minh now has 1 confirmed appointment today: Binh wins.
        (await Book(OilChange, Local.Iso(15), key: "b")).Value.Appointment.Technician.Name.Should().Be("Binh");
    }

    [Fact]
    public async Task Cancelled_appointments_do_not_count_for_the_tie_break()
    {
        _app.Data.Technicians.Add(Domain.Entities.Technician.Create(1, "Binh", new TimeOnly(8, 0), new TimeOnly(17, 0))
            .WithSkill(Skill.General, SkillTier.Trainee).WithId(8));
        var first = await Book(OilChange, Local.Iso(13), key: "a");
        await _app.CancelHandler().HandleAsync(new(first.Value.Appointment.Id), CancellationToken.None);

        (await Book(OilChange, Local.Iso(15), key: "b")).Value.Appointment.Technician.Name.Should().Be("Minh");
    }

    [Fact]
    public async Task Next_pair_is_tried_when_the_slot_was_taken()
    {
        _app.Appointments.NextAddResults.Enqueue(RepositoryErrors.SlotTaken);

        var result = await Book(BrakeInspection, Local.Iso(13));

        result.IsSuccess.Should().BeTrue();
        result.Value.Appointment.Technician.Name.Should().Be("Nam");
        _app.Appointments.AddAttempts.Select(a => a.TechnicianId).Should().Equal(3, 1);
    }

    [Fact]
    public async Task No_availability_when_every_pair_is_taken_at_save_time()
    {
        _app.Appointments.NextAddResults.Enqueue(RepositoryErrors.SlotTaken);
        _app.Appointments.NextAddResults.Enqueue(RepositoryErrors.SlotTaken);

        var result = await Book(BrakeInspection, Local.Iso(13));

        result.Error!.Code.Should().Be(ErrorCodes.NoAvailability);
        _app.Appointments.AddCalls.Should().Be(2);
    }

    // ---------- Suggestions ----------

    [Fact]
    public async Task No_availability_suggests_three_nearest_free_times_ascending()
    {
        // Bay 2 (only lift bay) is busy 13:00-14:30 local.
        await Book(BrakeInspection, Local.Iso(13), key: "a");

        var result = await Book(BrakeInspection, Local.Iso(13, 30), key: "b");

        result.Error!.Code.Should().Be(ErrorCodes.NoAvailability);
        // Free starts: ... 11:30 (120 min away) | 14:30 (60), 14:45 (75), 15:00 (90), 15:15, 15:30.
        result.Error.SuggestedStartTimes.Should().Equal(Local.Utc(14, 30), Local.Utc(14, 45), Local.Utc(15, 0));
    }

    [Fact]
    public async Task Suggestions_are_on_the_same_day_and_can_be_fewer_than_three()
    {
        // Occupy Bay 2 from 08:00 to 15:30 local, so only 15:30 is left for a 90-minute job.
        string[] starts = ["08:00", "09:30", "11:00", "12:30", "14:00"];
        var i = 0;
        foreach (var start in starts)
        {
            var parts = start.Split(':');
            await _app.BookAsync(BrakeInspection, Local.Iso(int.Parse(parts[0]), int.Parse(parts[1])), $"k{i++}");
        }

        var result = await Book(BrakeInspection, Local.Iso(14), key: "late");

        result.Error!.SuggestedStartTimes.Should().Equal(Local.Utc(15, 30));
    }

    [Fact]
    public async Task Suggestions_are_never_in_the_past()
    {
        // Now = 10:20 local on the test day; Bay 2 busy 11:00-12:30.
        _app.Clock.UtcNow = Local.Utc(10, 20);
        await Book(BrakeInspection, Local.Iso(11), key: "a");

        var result = await Book(BrakeInspection, Local.Iso(11), key: "b");

        result.Error!.SuggestedStartTimes.Should().OnlyContain(t => t > _app.Clock.UtcNow);
        result.Error.SuggestedStartTimes.Should().Equal(Local.Utc(12, 30), Local.Utc(12, 45), Local.Utc(13, 0));
    }

    // ---------- Idempotency ----------

    [Fact]
    public async Task Same_key_and_same_body_returns_the_saved_response_without_a_new_booking()
    {
        var first = await Book(BrakeInspection, Local.Iso(13), key: "same");

        var second = await Book(BrakeInspection, Local.Iso(13), key: "same");

        second.IsSuccess.Should().BeTrue();
        second.Value.IsReplay.Should().BeTrue();
        second.Value.Appointment.Should().BeEquivalentTo(first.Value.Appointment);
        _app.Appointments.AddCalls.Should().Be(1);
        _app.Data.Appointments.Should().ContainSingle();
    }

    [Fact]
    public async Task Same_key_and_same_instant_in_another_offset_is_a_replay()
    {
        await Book(BrakeInspection, Local.Iso(13), key: "same");

        var second = await Book(BrakeInspection, "2026-11-10T06:00:00Z", key: "same");

        second.Value.IsReplay.Should().BeTrue();
        _app.Appointments.AddCalls.Should().Be(1);
    }

    [Fact]
    public async Task Same_key_with_a_different_body_is_an_idempotency_conflict()
    {
        await Book(BrakeInspection, Local.Iso(13), key: "same");

        var second = await Book(BrakeInspection, Local.Iso(14), key: "same");

        second.Error!.Code.Should().Be(ErrorCodes.IdempotencyConflict);
        _app.Appointments.AddCalls.Should().Be(1);
    }

    [Fact]
    public async Task Replay_still_works_after_the_appointment_was_cancelled()
    {
        var first = await Book(BrakeInspection, Local.Iso(13), key: "same");
        await _app.CancelHandler().HandleAsync(new(first.Value.Appointment.Id), CancellationToken.None);

        var second = await Book(BrakeInspection, Local.Iso(13), key: "same");

        second.Value.IsReplay.Should().BeTrue();
        second.Value.Appointment.Status.Should().Be("Confirmed", "the saved response is returned as it was");
    }

    [Fact]
    public async Task Concurrent_request_with_same_key_wins_then_the_saved_response_is_replayed()
    {
        // The winner saved its appointment and key. Our read of the key and of the slots happened before that,
        // so we see neither: hide both, then let the insert fail on the IdempotencyKeys primary key.
        var winner = await Book(OilChange, Local.Iso(9), key: "same");
        var record = _app.Data.IdempotencyKeys["same"];
        _app.Data.IdempotencyKeys.Clear();
        _app.Data.Appointments.Clear();
        _app.Appointments.NextAddResults.Enqueue(RepositoryErrors.IdempotencyKeyTaken);
        var addsBefore = _app.Appointments.AddCalls;
        var store = new KeyAppearsAfterInsert(() => _app.Appointments.AddCalls > addsBefore ? record : null);

        var result = await _app.BookHandler(store).HandleAsync(TestApp.Command(OilChange, Local.Iso(9), "same"), CancellationToken.None);

        result.Value.IsReplay.Should().BeTrue();
        result.Value.Appointment.Id.Should().Be(winner.Value.Appointment.Id);
    }

    [Fact]
    public async Task The_saved_response_has_the_appointment_id()
    {
        var first = await Book(BrakeInspection, Local.Iso(13), key: "k");

        var record = _app.Data.IdempotencyKeys["k"];
        record.AppointmentId.Should().Be(first.Value.Appointment.Id);
        record.RequestHash.Should().HaveLength(64);
        record.ResponseJson.Should().Contain($"\"id\":{first.Value.Appointment.Id}");
    }

    // ---------- Rules ----------

    [Fact]
    public async Task Job_ending_after_closing_time_is_a_booking_rule_violation()
    {
        // 16:00 + 90 min = 17:30 local, close is 17:00.
        var result = await Book(BrakeInspection, Local.Iso(16));

        result.Error!.Code.Should().Be(ErrorCodes.BookingRuleViolation);
        _app.Appointments.AddCalls.Should().Be(0);
    }

    [Fact]
    public async Task Job_ending_exactly_at_closing_time_is_allowed()
    {
        var result = await Book(BrakeInspection, Local.Iso(15, 30));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Job_before_opening_is_a_booking_rule_violation()
    {
        var result = await Book(OilChange, Local.Iso(7, 45));

        result.Error!.Code.Should().Be(ErrorCodes.BookingRuleViolation);
    }

    [Theory]
    [InlineData(99, 1, 1, OilChange, "Customer")]
    [InlineData(1, 99, 1, OilChange, "Vehicle")]
    [InlineData(1, 1, 99, OilChange, "Dealership")]
    [InlineData(1, 1, 1, 99, "Service type")]
    public async Task Unknown_ids_are_not_found(int customerId, int vehicleId, int dealershipId, int serviceTypeId, string entity)
    {
        var result = await Book(serviceTypeId, Local.Iso(10), customerId: customerId, vehicleId: vehicleId, dealershipId: dealershipId);

        result.Error!.Code.Should().Be(ErrorCodes.NotFound);
        result.Error.Message.Should().StartWith(entity);
    }

    [Fact]
    public async Task Vehicle_of_another_customer_is_a_booking_rule_violation()
    {
        var result = await Book(OilChange, Local.Iso(10), vehicleId: 3);

        result.Error!.Code.Should().Be(ErrorCodes.BookingRuleViolation);
    }

    [Fact]
    public async Task Service_of_another_dealership_is_a_booking_rule_violation()
    {
        var result = await Book(OilChangeThuDuc, Local.Iso(10));

        result.Error!.Code.Should().Be(ErrorCodes.BookingRuleViolation);
    }

    [Fact]
    public async Task Inactive_service_is_a_booking_rule_violation()
    {
        _app.Data.GetServiceType(OilChange).Deactivate();

        var result = await Book(OilChange, Local.Iso(10));

        result.Error!.Code.Should().Be(ErrorCodes.BookingRuleViolation);
    }

    [Fact]
    public async Task Inactive_dealership_is_a_booking_rule_violation()
    {
        _app.Data.GetDealership(1).Deactivate();

        var result = await Book(OilChange, Local.Iso(10));

        result.Error!.Code.Should().Be(ErrorCodes.BookingRuleViolation);
    }

    [Fact]
    public async Task Invalid_request_is_rejected_before_any_database_call()
    {
        _app.Appointments.TransientFailuresLeft = 99;

        var result = await Book(OilChange, "2026-11-10T10:10:00+07:00");

        result.Error!.Code.Should().Be(ErrorCodes.InvalidRequest);
        result.Error.FieldErrors.Should().ContainKey("startTime");
        _app.Appointments.TransientFailuresLeft.Should().Be(99);
    }

    [Fact]
    public async Task Start_in_the_past_is_invalid()
    {
        var result = await Book(OilChange, "2026-11-09T06:45:00Z");

        result.Error!.Code.Should().Be(ErrorCodes.InvalidRequest);
        result.Error.FieldErrors["startTime"].Should().Equal("startTime must be in the future.");
        _app.Appointments.AddCalls.Should().Be(0);
    }

    [Fact]
    public async Task Start_equal_to_now_is_invalid()
    {
        var result = await Book(OilChange, "2026-11-09T07:00:00Z");

        result.Error!.Code.Should().Be(ErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task Replay_whose_start_is_now_in_the_past_returns_the_saved_response()
    {
        var first = await Book(BrakeInspection, Local.Iso(13), key: "late-retry");
        _app.Clock.UtcNow = Local.Utc(15); // the appointment has started already

        var retry = await Book(BrakeInspection, Local.Iso(13), key: "late-retry");

        retry.IsSuccess.Should().BeTrue();
        retry.Value.IsReplay.Should().BeTrue();
        retry.Value.Appointment.Should().BeEquivalentTo(first.Value.Appointment);
        _app.Appointments.AddCalls.Should().Be(1);
    }

    [Fact]
    public async Task New_key_with_a_past_start_is_still_invalid()
    {
        _app.Clock.UtcNow = Local.Utc(15);

        var result = await Book(BrakeInspection, Local.Iso(13), key: "new");

        result.Error!.Code.Should().Be(ErrorCodes.InvalidRequest);
    }

    // ---------- Transient errors ----------

    [Fact]
    public async Task One_transient_error_is_retried_and_the_booking_succeeds()
    {
        _app.Appointments.TransientFailuresLeft = 1;

        var result = await Book(BrakeInspection, Local.Iso(13));

        result.IsSuccess.Should().BeTrue();
        _app.Data.Appointments.Should().ContainSingle();
    }

    [Fact]
    public async Task Two_transient_errors_give_database_busy()
    {
        _app.Appointments.TransientFailuresLeft = 2;

        var result = await Book(BrakeInspection, Local.Iso(13));

        result.Error!.Code.Should().Be(ErrorCodes.DatabaseBusy);
        _app.Data.Appointments.Should().BeEmpty();
    }

    private sealed class KeyAppearsAfterInsert(Func<IdempotencyRecord?> find) : IIdempotencyStore
    {
        public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken) => Task.FromResult(find());
    }
}
