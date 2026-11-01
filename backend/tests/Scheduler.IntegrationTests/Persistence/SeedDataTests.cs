using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Enums;
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
        (await db.Customers.CountAsync()).Should().Be(1);
        (await db.Vehicles.CountAsync()).Should().Be(2);
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
}
