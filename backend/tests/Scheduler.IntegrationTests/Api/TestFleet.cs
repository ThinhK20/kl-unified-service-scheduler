using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Entities;
using Scheduler.IntegrationTests.Infrastructure;

namespace Scheduler.IntegrationTests.Api;

/// <summary>A test customer with many cars, for races where every request must use a different vehicle.</summary>
internal static class TestFleet
{
    private const string VinPrefix = "TESTFLEET";

    /// <summary>Creates (once) a customer with <paramref name="count"/> vehicles and returns their ids.</summary>
    public static async Task<(int CustomerId, IReadOnlyList<int> VehicleIds)> EnsureAsync(SqlServerFixture fixture, int count)
    {
        await using var db = fixture.CreateDbContext();
        var existing = await db.Vehicles.AsNoTracking().Where(v => v.Vin.StartsWith(VinPrefix)).OrderBy(v => v.Id).ToListAsync();
        int customerId;
        if (existing.Count > 0)
        {
            customerId = existing[0].CustomerId;
        }
        else
        {
            var customer = Customer.Create("Fleet Customer", "fleet@example.com", "+84 900 000 020");
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        for (var i = existing.Count; i < count; i++)
        {
            db.Vehicles.Add(Vehicle.Create(customerId, $"{VinPrefix}{i:00000000}", "Toyota", "Vios", 2022));
        }

        await db.SaveChangesAsync();
        var ids = await db.Vehicles.AsNoTracking().Where(v => v.Vin.StartsWith(VinPrefix)).OrderBy(v => v.Id).Select(v => v.Id).ToListAsync();
        return (customerId, ids.Take(count).ToList());
    }
}
