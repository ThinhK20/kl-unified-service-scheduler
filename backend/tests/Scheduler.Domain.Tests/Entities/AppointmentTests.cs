using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Domain.Errors;
using Scheduler.Domain.Tests.TestSupport;

namespace Scheduler.Domain.Tests.Entities;

public class AppointmentTests
{
    // 10:30 local (UTC+7) on 10 Nov 2026.
    private static readonly DateTime Start = Utc.At(3, 30);
    private static readonly DateTime Now = Utc.At(0, 0);

    private Vehicle _vehicle = Seed.Vios();
    private Dealership _dealership = Seed.District1();
    private ServiceType _service = Seed.OilChange();
    private Technician _technician = Seed.Nam();
    private ServiceBay _bay = Seed.Bay1();

    private Appointment Create(DateTime? start = null, DateTime? now = null, int customerId = 1) =>
        Appointment.Create(customerId, _vehicle, _dealership, _service, _technician, _bay, start ?? Start, now ?? Now);

    private void ShouldFailWith(Func<Appointment> act, DomainError expected) =>
        act.Should().Throw<DomainException>().Which.Error.Should().Be(expected);

    [Fact]
    public void Create_makes_a_confirmed_appointment_with_snapshots()
    {
        var appointment = Create();

        appointment.Status.Should().Be(AppointmentStatus.Confirmed);
        appointment.StartUtc.Should().Be(Start);
        appointment.EndUtc.Should().Be(Start.AddMinutes(60));
        appointment.CreatedUtc.Should().Be(Now);
        appointment.CustomerId.Should().Be(1);
        appointment.VehicleId.Should().Be(_vehicle.Id);
        appointment.DealershipId.Should().Be(_dealership.Id);
        appointment.ServiceTypeId.Should().Be(_service.Id);
        appointment.TechnicianId.Should().Be(_technician.Id);
        appointment.ServiceBayId.Should().Be(_bay.Id);
        appointment.DurationSnapshot.Should().Be(60);
        appointment.RequiredSkillSnapshot.Should().Be(Skill.General);
        appointment.RequiredBayTypeSnapshot.Should().Be(BayType.Standard);
        appointment.CanBeCancelled.Should().BeTrue();
    }

    [Fact]
    public void Create_reserves_4_technician_4_bay_and_4_vehicle_slots_for_60_minutes()
    {
        var appointment = Create();

        var expectedStarts = new[] { Start, Start.AddMinutes(15), Start.AddMinutes(30), Start.AddMinutes(45) };
        appointment.ResourceSlots.Should().HaveCount(12);
        appointment.ResourceSlots.Where(s => s.ResourceType == ResourceType.Vehicle)
            .Select(s => s.SlotStartUtc).Should().Equal(expectedStarts);
        appointment.ResourceSlots.Where(s => s.ResourceType == ResourceType.Vehicle)
            .Should().OnlyContain(s => s.ResourceId == _vehicle.Id);
        appointment.ResourceSlots.Where(s => s.ResourceType == ResourceType.Technician)
            .Select(s => s.SlotStartUtc).Should().Equal(expectedStarts);
        appointment.ResourceSlots.Where(s => s.ResourceType == ResourceType.Bay)
            .Select(s => s.SlotStartUtc).Should().Equal(expectedStarts);
        appointment.ResourceSlots.Where(s => s.ResourceType == ResourceType.Technician)
            .Should().OnlyContain(s => s.ResourceId == _technician.Id);
        appointment.ResourceSlots.Where(s => s.ResourceType == ResourceType.Bay)
            .Should().OnlyContain(s => s.ResourceId == _bay.Id);
        appointment.ResourceSlots.Should().OnlyContain(s => s.Appointment == appointment);
    }

    [Fact]
    public void Snapshots_do_not_change_when_service_rules_change_later()
    {
        var appointment = Create();

        _service.Deactivate();

        appointment.DurationSnapshot.Should().Be(60);
        appointment.Status.Should().Be(AppointmentStatus.Confirmed);
    }

    [Fact]
    public void Create_rejects_start_off_grid() =>
        ShouldFailWith(() => Create(start: Utc.At(3, 10)), DomainErrors.Time.NotOnGrid);

    [Fact]
    public void Create_rejects_start_with_seconds() =>
        ShouldFailWith(() => Create(start: Utc.At(3, 30, 5)), DomainErrors.Time.NotOnGrid);

    [Fact]
    public void Create_rejects_start_in_the_past() =>
        ShouldFailWith(() => Create(now: Start.AddMinutes(1)), DomainErrors.Time.StartNotInFuture);

    [Fact]
    public void Create_rejects_start_equal_to_now() =>
        ShouldFailWith(() => Create(now: Start), DomainErrors.Time.StartNotInFuture);

    [Fact]
    public void Create_rejects_vehicle_of_another_customer() =>
        ShouldFailWith(() => Create(customerId: 2), DomainErrors.Appointment.VehicleNotOwnedByCustomer);

    [Fact]
    public void Create_rejects_inactive_service()
    {
        _service.Deactivate();
        ShouldFailWith(() => Create(), DomainErrors.Appointment.ServiceTypeInactive);
    }

    [Fact]
    public void Create_rejects_inactive_dealership()
    {
        _dealership.Deactivate();
        ShouldFailWith(() => Create(), DomainErrors.Appointment.DealershipInactive);
    }

    [Fact]
    public void Create_rejects_service_from_another_dealership()
    {
        _service = Seed.OilChange(dealershipId: 2);
        ShouldFailWith(() => Create(), DomainErrors.Appointment.ServiceNotOfferedAtDealership);
    }

    [Fact]
    public void Create_allows_a_job_that_ends_exactly_at_close()
    {
        // 16:00-17:00 local
        var appointment = Create(start: Utc.At(9, 0));

        appointment.EndUtc.Should().Be(Utc.At(10, 0));
    }

    [Fact]
    public void Create_rejects_a_job_that_ends_after_close() =>
        // 16:15-17:15 local
        ShouldFailWith(() => Create(start: Utc.At(9, 15)), DomainErrors.Appointment.OutsideOpeningHours);

    [Fact]
    public void Create_rejects_unqualified_technician()
    {
        _technician = Technician.Create(1, "Nobody", new TimeOnly(8, 0), new TimeOnly(17, 0))
            .WithSkill(Skill.Brakes, SkillTier.Master).WithId(9);
        ShouldFailWith(() => Create(), DomainErrors.Appointment.TechnicianNotUsable);
    }

    [Fact]
    public void Create_rejects_technician_off_shift()
    {
        // Shift 12:00-17:00 local; booking is 10:30 local.
        _technician = Technician.Create(1, "Minh", new TimeOnly(12, 0), new TimeOnly(17, 0))
            .WithSkill(Skill.General, SkillTier.Trainee).WithId(3);
        ShouldFailWith(() => Create(), DomainErrors.Appointment.TechnicianNotUsable);
    }

    [Fact]
    public void Create_rejects_bay_of_wrong_type()
    {
        _bay = ServiceBay.Create(1, "Bay 2", BayType.Lift).WithId(2);
        ShouldFailWith(() => Create(), DomainErrors.Appointment.BayNotUsable);
    }

    [Fact]
    public void Create_rejects_bay_at_another_dealership()
    {
        _bay = Seed.Bay1(dealershipId: 2);
        ShouldFailWith(() => Create(), DomainErrors.Appointment.BayNotUsable);
    }

    [Fact]
    public void Cancel_confirmed_appointment_sets_cancelled_and_releases_slots()
    {
        var appointment = Create();

        appointment.Cancel(Now);

        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.ResourceSlots.Should().BeEmpty();
        appointment.CanBeCancelled.Should().BeFalse();
    }

    [Fact]
    public void Cancel_twice_is_rejected_with_not_cancellable()
    {
        var appointment = Create();
        appointment.Cancel(Now);

        var act = () => appointment.Cancel(Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCodes.AppointmentNotCancellable);
    }

    [Fact]
    public void Cancel_after_the_start_is_rejected_with_not_cancellable()
    {
        var appointment = Create();

        var act = () => appointment.Cancel(Start.AddMinutes(10));

        act.Should().Throw<DomainException>().Which.Error.Should().Be(DomainErrors.Appointment.AlreadyStarted);
        appointment.Status.Should().Be(AppointmentStatus.Confirmed);
        appointment.ResourceSlots.Should().HaveCount(12);
    }

    [Fact]
    public void Cancel_exactly_at_the_start_is_rejected()
    {
        var appointment = Create();

        var act = () => appointment.Cancel(Start);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCodes.AppointmentNotCancellable);
    }

    [Fact]
    public void Cancel_one_minute_before_the_start_is_allowed()
    {
        var appointment = Create();

        appointment.Cancel(Start.AddMinutes(-1));

        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
    }

    [Fact]
    public void ResourceSlot_For_rejects_off_grid_start()
    {
        var appointment = Create();

        var act = () => ResourceSlot.For(ResourceType.Bay, 1, Utc.At(3, 31), appointment);

        act.Should().Throw<DomainException>().Which.Error.Should().Be(DomainErrors.Time.NotOnGrid);
    }
}
