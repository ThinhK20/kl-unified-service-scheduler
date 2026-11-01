using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Abstractions;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Domain.ValueObjects;
using Scheduler.Infrastructure.Persistence;
using Scheduler.Infrastructure.Seeding;
using Scheduler.IntegrationTests.Infrastructure;
using Scheduler.IntegrationTests.Persistence;

namespace Scheduler.IntegrationTests.Repositories;

/// <summary>Repository behaviour on a real SQL Server. Every test uses its own date in 2030.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class AppointmentRepositoryTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const int OilChange = SeedData.ServiceTypeIds.OilChangeDistrict1; // 60 min, General, Standard
    private const int TyreRotation = SeedData.ServiceTypeIds.TyreRotation; // 30 min, Tyres, Lift
    private const int EvCheck = SeedData.ServiceTypeIds.EvBatteryCheck; // 60 min, EV Senior, EV bay
    private const int Nam = SeedData.TechnicianIds.Nam;
    private const int Lan = SeedData.TechnicianIds.Lan;
    private const int Bay1 = SeedData.BayIds.Bay1Standard;
    private const int Bay2 = SeedData.BayIds.Bay2Lift;
    private const int Bay3 = SeedData.BayIds.Bay3Ev;

    private RepositoryHarness _harness = null!;

    public Task InitializeAsync()
    {
        _harness = new RepositoryHarness(fixture);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task TryAdd_saves_the_appointment_its_slots_and_the_idempotency_key_together()
    {
        var start = AppointmentFactory.LocalToUtc(new DateOnly(2030, 4, 1), 9);
        var key = NewKey();

        await using (var scope = _harness.CreateScope())
        {
            var appointment = await scope.NewAppointmentAsync(OilChange, Nam, Bay1, start);
            var result = await scope.Appointments.TryAddWithSlotsAsync(appointment, Entry(key), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Id.Should().BePositive();
        }

        await using var db = fixture.CreateDbContext();
        var saved = await db.Appointments.Include(a => a.ResourceSlots).SingleAsync(a => a.StartUtc == start);
        saved.ResourceSlots.Should().HaveCount(8);

        await using var read = _harness.CreateScope();
        var record = await read.Idempotency.FindAsync(key, CancellationToken.None);
        record.Should().NotBeNull();
        record!.AppointmentId.Should().Be(saved.Id);
        record.RequestHash.Should().Be(Hash);
        record.ResponseJson.Should().Be($"{{\"id\":{saved.Id}}}");
        record.CreatedUtc.Should().Be(FixedClock.DefaultNow);
        record.CreatedUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task TryAdd_returns_SlotTaken_and_saves_nothing_including_the_key()
    {
        var day = new DateOnly(2030, 4, 2);
        await BookAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 10));
        var key = NewKey();

        await using (var scope = _harness.CreateScope())
        {
            // Nam is busy 10:00-11:00; this tyre rotation needs him at 10:30.
            var clash = await scope.NewAppointmentAsync(TyreRotation, Nam, Bay2, AppointmentFactory.LocalToUtc(day, 10, 30));
            var result = await scope.Appointments.TryAddWithSlotsAsync(clash, Entry(key), CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be(RepositoryErrors.SlotTaken);
        }

        await using var db = fixture.CreateDbContext();
        (await CountAppointmentsOnAsync(db, day)).Should().Be(1);
        (await db.ResourceSlots.CountAsync(s => s.ResourceType == ResourceType.Bay && s.ResourceId == Bay2 && s.SlotStartUtc >= DayStart(day) && s.SlotStartUtc < DayStart(day).AddDays(1)))
            .Should().Be(0);
        (await db.IdempotencyKeys.AnyAsync(k => k.Key == key)).Should().BeFalse();
    }

    [Fact]
    public async Task TryAdd_returns_IdempotencyKeyTaken_for_a_duplicate_key_and_rolls_back_the_appointment()
    {
        var day = new DateOnly(2030, 4, 3);
        var key = NewKey();
        await BookAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 9), key);

        await using (var scope = _harness.CreateScope())
        {
            // A free slot (14:00), but the same key.
            var second = await scope.NewAppointmentAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 14));
            var result = await scope.Appointments.TryAddWithSlotsAsync(second, Entry(key), CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be(RepositoryErrors.IdempotencyKeyTaken);
        }

        await using var db = fixture.CreateDbContext();
        (await CountAppointmentsOnAsync(db, day)).Should().Be(1, "the second appointment was saved before the key and must be rolled back");
        var afternoon = AppointmentFactory.LocalToUtc(day, 14);
        var nextDay = DayStart(day).AddDays(1);
        (await db.ResourceSlots.CountAsync(s => s.SlotStartUtc >= afternoon && s.SlotStartUtc < nextDay)).Should().Be(0);
    }

    [Fact]
    public async Task After_SlotTaken_the_same_scope_can_save_the_next_pair()
    {
        var day = new DateOnly(2030, 4, 4);
        await BookAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 10));
        var start = AppointmentFactory.LocalToUtc(day, 10);
        var key = NewKey();

        await using (var scope = _harness.CreateScope())
        {
            var repository = scope.Appointments;

            var first = await scope.NewAppointmentAsync(TyreRotation, Nam, Bay2, start);
            (await repository.TryAddWithSlotsAsync(first, Entry(key), CancellationToken.None)).Error.Should().Be(RepositoryErrors.SlotTaken);

            var next = await scope.NewAppointmentAsync(TyreRotation, Lan, Bay2, start);
            var result = await repository.TryAddWithSlotsAsync(next, Entry(key), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            scope.Get<SchedulerDbContext>().ChangeTracker.Entries<Appointment>().Should().ContainSingle()
                .Which.Entity.Should().BeSameAs(next);
        }

        await using var db = fixture.CreateDbContext();
        var saved = await db.Appointments.Include(a => a.ResourceSlots).SingleAsync(a => a.StartUtc == start && a.TechnicianId == Lan);
        saved.ResourceSlots.Should().HaveCount(4);
        (await db.IdempotencyKeys.SingleAsync(k => k.Key == key)).AppointmentId.Should().Be(saved.Id);
    }

    [Fact]
    public async Task GetTakenSlots_returns_only_the_asked_resources_inside_the_half_open_window()
    {
        var day = new DateOnly(2030, 4, 5);
        await BookAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 10)); // Nam + Bay1 10:00-11:00
        await BookAsync(EvCheck, Lan, Bay3, AppointmentFactory.LocalToUtc(day, 13)); // Lan + Bay3 13:00-14:00
        await BookAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day.AddDays(1), 10, 30)); // next day: outside

        await using var scope = _harness.CreateScope();
        var window = TimeRange.Create(AppointmentFactory.LocalToUtc(day, 10, 30), AppointmentFactory.LocalToUtc(day, 13, 15));

        var slots = await scope.Appointments.GetTakenSlotsAsync([Nam], [Bay3], window, CancellationToken.None);

        slots.Should().BeEquivalentTo(
            [
                new TakenSlot(ResourceType.Technician, Nam, AppointmentFactory.LocalToUtc(day, 10, 30)),
                new TakenSlot(ResourceType.Technician, Nam, AppointmentFactory.LocalToUtc(day, 10, 45)),
                new TakenSlot(ResourceType.Bay, Bay3, AppointmentFactory.LocalToUtc(day, 13)),
            ],
            options => options.WithStrictOrdering());
        slots.Should().OnlyContain(s => s.SlotStartUtc.Kind == DateTimeKind.Utc);

        (await scope.Appointments.GetTakenSlotsAsync([], [], window, CancellationToken.None)).Should().BeEmpty();
        // Ids are per resource type: technician 1 is not bay 1.
        (await scope.Appointments.GetTakenSlotsAsync([], [Nam], window, CancellationToken.None))
            .Should().OnlyContain(s => s.ResourceType == ResourceType.Bay);
    }

    [Fact]
    public async Task CountConfirmedByTechnician_counts_confirmed_appointments_starting_in_the_window()
    {
        var day = new DateOnly(2030, 4, 7);
        await BookAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 8));
        await BookAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 9));
        var cancelledId = await BookAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 10));
        await BookAsync(EvCheck, Lan, Bay3, AppointmentFactory.LocalToUtc(day, 11));
        await BookAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day.AddDays(1), 8)); // next day

        await using (var scope = _harness.CreateScope())
        {
            var toCancel = await scope.Appointments.GetForCancelAsync(cancelledId, CancellationToken.None);
            toCancel!.Cancel();
            await scope.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using var read = _harness.CreateScope();
        var window = TimeRange.Create(DayStart(day), DayStart(day).AddDays(1));
        var counts = await read.Appointments.CountConfirmedByTechnicianAsync(
            [Nam, Lan, SeedData.TechnicianIds.Minh], window, CancellationToken.None);

        counts.Should().BeEquivalentTo(new Dictionary<int, int> { [Nam] = 2, [Lan] = 1 });
    }

    [Fact]
    public async Task GetForCancel_then_unit_of_work_cancels_frees_the_slots_and_allows_a_rebook()
    {
        var day = new DateOnly(2030, 4, 9);
        var start = AppointmentFactory.LocalToUtc(day, 15);
        var id = await BookAsync(OilChange, Nam, Bay1, start);

        await using (var scope = _harness.CreateScope())
        {
            var appointment = await scope.Appointments.GetForCancelAsync(id, CancellationToken.None);

            appointment.Should().NotBeNull();
            appointment!.ResourceSlots.Should().HaveCount(8);
            appointment.Vehicle.Should().NotBeNull();
            appointment.Dealership.Should().NotBeNull();
            appointment.ServiceType.Should().NotBeNull();
            appointment.Technician.Should().NotBeNull();
            appointment.ServiceBay.Should().NotBeNull();

            appointment.Cancel();
            await scope.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using (var scope = _harness.CreateScope())
        {
            var cancelled = await scope.Appointments.GetByIdWithDetailsAsync(id, CancellationToken.None);
            cancelled!.Status.Should().Be(AppointmentStatus.Cancelled);
            cancelled.Technician!.Name.Should().Be("Nam");

            var rebook = await scope.NewAppointmentAsync(OilChange, Nam, Bay1, start);
            (await scope.Appointments.TryAddWithSlotsAsync(rebook, null, CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        await using var db = fixture.CreateDbContext();
        (await db.ResourceSlots.CountAsync(s => s.AppointmentId == id)).Should().Be(0);
    }

    [Fact]
    public async Task A_second_cancel_of_the_same_loaded_appointment_is_a_concurrency_conflict()
    {
        var id = await BookAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(new DateOnly(2030, 4, 10), 9));

        await using var first = _harness.CreateScope();
        await using var second = _harness.CreateScope();
        var a = await first.Appointments.GetForCancelAsync(id, CancellationToken.None);
        var b = await second.Appointments.GetForCancelAsync(id, CancellationToken.None);

        a!.Cancel();
        await first.UnitOfWork.SaveChangesAsync(CancellationToken.None);

        b!.Cancel();
        var act = () => second.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task GetByCustomer_returns_newest_first_with_details()
    {
        var day = new DateOnly(2030, 4, 11);
        var early = await BookAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 8));
        var late = await BookAsync(OilChange, Nam, Bay1, AppointmentFactory.LocalToUtc(day, 16));

        await using var scope = _harness.CreateScope();
        var list = await scope.Appointments.GetByCustomerWithDetailsAsync(SeedData.DemoCustomerId, CancellationToken.None);

        list.Should().BeInDescendingOrder(a => a.StartUtc);
        list.Select(a => a.Id).Should().ContainInOrder(late, early);
        list.Should().OnlyContain(a => a.Vehicle != null && a.ServiceBay != null && a.Dealership != null);
        (await scope.Appointments.GetByCustomerWithDetailsAsync(999, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Ten_parallel_requests_for_the_same_slot_give_exactly_one_success()
    {
        const int requests = 10;
        var day = new DateOnly(2030, 4, 12);
        var start = AppointmentFactory.LocalToUtc(day, 11);

        // Prepare every request (own scope = own DbContext) first, then fire them together.
        var scopes = new List<RepositoryScope>();
        var appointments = new List<Appointment>();
        for (var i = 0; i < requests; i++)
        {
            var scope = _harness.CreateScope();
            scopes.Add(scope);
            appointments.Add(await scope.NewAppointmentAsync(OilChange, Nam, Bay1, start));
        }

        try
        {
            using var gate = new ManualResetEventSlim(false);
            var tasks = Enumerable.Range(0, requests).Select(i => Task.Run(async () =>
            {
                gate.Wait();
                return await scopes[i].Appointments.TryAddWithSlotsAsync(appointments[i], Entry(NewKey()), CancellationToken.None);
            })).ToList();
            gate.Set();
            var results = await Task.WhenAll(tasks);

            results.Count(r => r.IsSuccess).Should().Be(1);
            results.Where(r => r.IsFailure).Should().HaveCount(requests - 1)
                .And.OnlyContain(r => r.Error == RepositoryErrors.SlotTaken);
        }
        finally
        {
            foreach (var scope in scopes)
            {
                await scope.DisposeAsync();
            }
        }

        await using var db = fixture.CreateDbContext();
        (await CountAppointmentsOnAsync(db, day)).Should().Be(1);
        (await db.ResourceSlots.CountAsync(s => s.SlotStartUtc >= DayStart(day) && s.SlotStartUtc < DayStart(day).AddDays(1))).Should().Be(8);
    }

    private const string Hash = "0000000000000000000000000000000000000000000000000000000000000000";

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private static IdempotencyEntry Entry(string key) =>
        new(key, Hash, FixedClock.DefaultNow, saved => $"{{\"id\":{saved.Id}}}");

    private static DateTime DayStart(DateOnly day) => AppointmentFactory.LocalToUtc(day, 0);

    private static Task<int> CountAppointmentsOnAsync(SchedulerDbContext db, DateOnly day)
    {
        var start = DayStart(day);
        var end = start.AddDays(1);
        return db.Appointments.CountAsync(a => a.StartUtc >= start && a.StartUtc < end);
    }

    private async Task<int> BookAsync(int serviceTypeId, int technicianId, int bayId, DateTime startUtc, string? key = null)
    {
        await using var scope = _harness.CreateScope();
        var appointment = await scope.NewAppointmentAsync(serviceTypeId, technicianId, bayId, startUtc);
        var result = await scope.Appointments.TryAddWithSlotsAsync(appointment, key is null ? null : Entry(key), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Value.Id;
    }
}
