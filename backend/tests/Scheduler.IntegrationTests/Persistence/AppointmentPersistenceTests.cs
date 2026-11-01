using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.IntegrationTests.Infrastructure;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.IntegrationTests.Persistence;

/// <summary>
/// Saves appointments to a real SQL Server and checks the slot rows and the unique slot index.
/// Every test uses its own date, so tests sharing the database do not collide.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AppointmentPersistenceTests(SqlServerFixture fixture)
{
    private const int OilChange = SeedData.ServiceTypeIds.OilChangeDistrict1; // 60 min, General, Standard bay
    private const int BrakeInspection = SeedData.ServiceTypeIds.BrakeInspection; // 90 min, Brakes, Lift bay
    private const int TyreRotation = SeedData.ServiceTypeIds.TyreRotation; // 30 min, Tyres, Lift bay
    private const int Nam = SeedData.TechnicianIds.Nam;
    private const int Bay1 = SeedData.BayIds.Bay1Standard;
    private const int Bay2 = SeedData.BayIds.Bay2Lift;

    [Fact]
    public async Task Saving_an_appointment_writes_it_and_its_slots_in_one_SaveChanges()
    {
        var day = new DateOnly(2030, 3, 4);
        var start = AppointmentFactory.LocalToUtc(day, 9);
        int id;

        await using (var db = fixture.CreateDbContext())
        {
            var appointment = await AppointmentFactory.CreateAsync(db, BrakeInspection, Nam, Bay2, start);
            db.Appointments.Add(appointment);
            (await db.SaveChangesAsync()).Should().Be(1 + (AppointmentFactory.RowsPerSlot * (90 / 15)));
            id = appointment.Id;
            id.Should().BePositive();
            appointment.RowVersion.Should().NotBeEmpty();
        }

        await using (var db = fixture.CreateDbContext())
        {
            var saved = await db.Appointments.AsNoTracking().Include(a => a.ResourceSlots).SingleAsync(a => a.Id == id);
            saved.Status.Should().Be(AppointmentStatus.Confirmed);
            saved.StartUtc.Should().Be(start);
            saved.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
            saved.EndUtc.Should().Be(start.AddMinutes(90));
            saved.EndUtc.Kind.Should().Be(DateTimeKind.Utc);
            saved.CreatedUtc.Kind.Should().Be(DateTimeKind.Utc);
            saved.DurationSnapshot.Should().Be(90);
            saved.RequiredSkillSnapshot.Should().Be(Skill.Brakes);
            saved.RequiredBayTypeSnapshot.Should().Be(BayType.Lift);

            saved.ResourceSlots.Should().HaveCount(AppointmentFactory.RowsPerSlot * 6);
            saved.ResourceSlots.Should().OnlyContain(s => s.SlotStartUtc.Kind == DateTimeKind.Utc);
            saved.ResourceSlots.Where(s => s.ResourceType == ResourceType.Technician)
                .Select(s => s.SlotStartUtc).Should().BeEquivalentTo(Enumerable.Range(0, 6).Select(i => start.AddMinutes(15 * i)));
            saved.ResourceSlots.Where(s => s.ResourceType == ResourceType.Bay)
                .Should().HaveCount(6).And.OnlyContain(s => s.ResourceId == Bay2);
        }
    }

    [Fact]
    public async Task A_second_appointment_that_reuses_a_technician_slot_is_rejected_and_nothing_is_saved()
    {
        var day = new DateOnly(2030, 3, 5);
        await using (var db = fixture.CreateDbContext())
        {
            // Nam + Bay 1, 10:00-11:00 local.
            db.Appointments.Add(await AppointmentFactory.CreateAsync(db, OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 10)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            // Nam + Bay 2 + the other vehicle, 10:45-11:15 local: only the 10:45 technician slot clashes.
            var clash = await AppointmentFactory.CreateAsync(db, TyreRotation, Nam, Bay2, AppointmentFactory.LocalToUtc(day, 10, 45), vehicleId: 2);
            db.Appointments.Add(clash);

            var act = () => db.SaveChangesAsync();

            var thrown = await act.Should().ThrowAsync<DbUpdateException>();
            var sql = thrown.Which.InnerException.Should().BeOfType<SqlException>().Subject;
            sql.Number.Should().BeOneOf(2601, 2627);
            sql.Message.Should().Contain("UX_ResourceSlots_Resource_Slot");
        }

        await using (var check = fixture.CreateDbContext())
        {
            var dayStart = AppointmentFactory.LocalToUtc(day, 0);
            var dayEnd = dayStart.AddDays(1);
            (await check.Appointments.CountAsync(a => a.StartUtc >= dayStart && a.StartUtc < dayEnd)).Should().Be(1);
            (await check.ResourceSlots.CountAsync(s => s.SlotStartUtc >= dayStart && s.SlotStartUtc < dayEnd)).Should().Be(AppointmentFactory.RowsPerSlot * 4);
            (await check.ResourceSlots.CountAsync(s =>
                s.ResourceType == ResourceType.Bay && s.ResourceId == Bay2 && s.SlotStartUtc >= dayStart && s.SlotStartUtc < dayEnd))
                .Should().Be(0);
        }
    }

    [Fact]
    public async Task Touching_appointments_for_the_same_technician_and_bay_both_save()
    {
        var day = new DateOnly(2030, 3, 6);
        await using var db = fixture.CreateDbContext();

        db.Appointments.Add(await AppointmentFactory.CreateAsync(db, OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 10)));
        await db.SaveChangesAsync();

        db.Appointments.Add(await AppointmentFactory.CreateAsync(db, OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 11)));
        var act = () => db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
        var dayStart = AppointmentFactory.LocalToUtc(day, 0);
        (await db.ResourceSlots.CountAsync(s => s.SlotStartUtc >= dayStart && s.SlotStartUtc < dayStart.AddDays(1))).Should().Be(AppointmentFactory.RowsPerSlot * 8);
    }

    [Fact]
    public async Task Cancel_deletes_the_slots_keeps_the_appointment_and_frees_the_time()
    {
        var day = new DateOnly(2030, 3, 7);
        var start = AppointmentFactory.LocalToUtc(day, 14);
        int id;

        await using (var db = fixture.CreateDbContext())
        {
            var appointment = await AppointmentFactory.CreateAsync(db, OilChange, Nam, Bay1, start);
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            id = appointment.Id;
        }

        await using (var db = fixture.CreateDbContext())
        {
            var appointment = await db.Appointments.Include(a => a.ResourceSlots).SingleAsync(a => a.Id == id);
            appointment.ResourceSlots.Should().HaveCount(AppointmentFactory.RowsPerSlot * 4);

            appointment.Cancel(FixedClock.DefaultNow);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            var cancelled = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);
            cancelled.Status.Should().Be(AppointmentStatus.Cancelled);
            (await db.ResourceSlots.CountAsync(s => s.AppointmentId == id)).Should().Be(0);

            // The same technician, bay and time can be booked again.
            var rebooked = await AppointmentFactory.CreateAsync(db, OilChange, Nam, Bay1, start);
            db.Appointments.Add(rebooked);
            await db.SaveChangesAsync();

            rebooked.Id.Should().NotBe(id);
            (await db.ResourceSlots.CountAsync(s => s.AppointmentId == rebooked.Id)).Should().Be(AppointmentFactory.RowsPerSlot * 4);
        }
    }

    [Fact]
    public async Task Check_constraint_rejects_an_end_before_the_start()
    {
        await using var db = fixture.CreateDbContext();

        var act = () => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Appointments (CustomerId, VehicleId, DealershipId, ServiceTypeId, TechnicianId, ServiceBayId, " +
            "StartUtc, EndUtc, Status, CreatedUtc, DurationSnapshot, RequiredSkillSnapshot, RequiredBayTypeSnapshot) " +
            "VALUES (1, 1, 1, 1, 1, 1, '2030-03-08T05:00:00', '2030-03-08T04:00:00', 'Confirmed', '2030-01-01', 60, 'General', 'Standard')");

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(547);
    }
}
