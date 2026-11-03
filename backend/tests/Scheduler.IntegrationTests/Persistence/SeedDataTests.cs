using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Enums;
using Scheduler.Infrastructure.Persistence;
using Scheduler.IntegrationTests.Infrastructure;

namespace Scheduler.IntegrationTests.Persistence;

[Collection(SqlServerCollection.Name)]
public sealed class SeedDataTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Migration_creates_the_seed_rows()
    {
        await using var db = fixture.CreateDbContext();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.Dealerships.CountAsync()).Should().Be(2);
        (await db.ServiceTypes.CountAsync()).Should().Be(5);
        (await db.Technicians.CountAsync()).Should().Be(5);
        (await db.Technicians.CountAsync(t => !t.IsActive)).Should().Be(1);
        (await db.ServiceBays.CountAsync()).Should().Be(5);
        (await db.ServiceBays.CountAsync(b => !b.IsActive)).Should().Be(1);
        (await db.Customers.CountAsync()).Should().Be(3);
        (await db.Vehicles.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task Seed_rows_have_the_spec_values()
    {
        await using var db = fixture.CreateDbContext();

        var district1 = await db.Dealerships.SingleAsync(d => d.Id == 1);
        district1.Name.Should().Be("District 1 Service Centre");
        district1.TimeZoneId.Should().Be("Asia/Ho_Chi_Minh");
        district1.OpenTime.Should().Be(new TimeOnly(8, 0));
        district1.CloseTime.Should().Be(new TimeOnly(17, 0));
        district1.TimeZone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(7));

        var brakes = await db.ServiceTypes.SingleAsync(s => s.Id == 2);
        brakes.DurationMinutes.Should().Be(90);
        brakes.RequiredSkill.Should().Be(Skill.Brakes);
        brakes.MinimumTier.Should().Be(SkillTier.Qualified);
        brakes.RequiredBayType.Should().Be(BayType.Lift);

        var tuan = await db.Technicians.Include(t => t.Skills).SingleAsync(t => t.Id == 5);
        tuan.IsActive.Should().BeFalse();
        tuan.TierFor(Skill.EV).Should().Be(SkillTier.Master);

        var minh = await db.Technicians.Include(t => t.Skills).SingleAsync(t => t.Id == 3);
        minh.ShiftStart.Should().Be(new TimeOnly(12, 0));
        minh.Skills.Should().HaveCount(2);

        var bay4 = await db.ServiceBays.SingleAsync(b => b.Id == 5);
        bay4.Name.Should().Be("Bay 4");
        bay4.IsActive.Should().BeFalse();

        (await db.Vehicles.SingleAsync(v => v.Id == 2)).Vin.Should().Be("7FARW2H58PE000002");
    }

    [Fact]
    public async Task Seed_has_the_three_demo_customers_and_their_five_vehicles()
    {
        await using var db = fixture.CreateDbContext();

        var customers = await db.Customers.AsNoTracking().Where(c => c.Id <= 3).OrderBy(c => c.Id).ToListAsync();
        customers.Select(c => (c.Id, c.Name, c.Email, c.Phone)).Should().Equal(
            (1, "An Nguyen", "an.nguyen@example.com", "+84 900 000 001"),
            (2, "Binh Tran", "binh.tran@example.com", "+84 900 000 002"),
            (3, "Chi Le", "chi.le@example.com", "+84 900 000 003"));

        var vehicles = await db.Vehicles.AsNoTracking().Where(v => v.Id <= 5).OrderBy(v => v.Id).ToListAsync();
        vehicles.Select(v => (v.Id, v.CustomerId, v.Vin, v.Make, v.Model, v.Year)).Should().Equal(
            (1, 1, "JTDBT923X71000001", "Toyota", "Vios", 2021),
            (2, 1, "7FARW2H58PE000002", "Honda", "CR-V", 2023),
            (3, 2, "RLLV8EAA5RV000003", "VinFast", "VF 8", 2024),
            (4, 3, "JM3KFBCM1N0000004", "Mazda", "CX-5", 2022),
            (5, 3, "KNABE511AL0000005", "Kia", "Morning", 2020));
    }

    [Fact]
    public async Task All_migrations_apply_cleanly_on_an_empty_database()
    {
        var connectionString = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = "SchedulerEmptyMigrate" }.ConnectionString;
        var options = new DbContextOptionsBuilder<SchedulerDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new SchedulerDbContext(options);
        await db.Database.EnsureDeletedAsync();
        try
        {
            await db.Database.MigrateAsync();

            (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
            (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(m => m.EndsWith("_ThreeDemoCustomers", StringComparison.Ordinal));
            (await db.Customers.CountAsync()).Should().Be(3);
            (await db.Vehicles.CountAsync()).Should().Be(5);
            (await db.Vehicles.CountAsync(v => v.CustomerId == 3)).Should().Be(2);

            // Identity continues after the seeded ids, so new rows never clash with them.
            var customer = Scheduler.Domain.Entities.Customer.Create("New Customer", "new@example.com", "+84 900 000 099");
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customer.Id.Should().BeGreaterThan(3);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
