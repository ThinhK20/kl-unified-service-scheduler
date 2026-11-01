using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Abstractions;
using Scheduler.Domain.Entities;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Repositories;

internal sealed class DealershipRepository(SchedulerDbContext db) : IDealershipRepository
{
    public Task<Dealership?> GetByIdAsync(int dealershipId, CancellationToken cancellationToken) =>
        db.Dealerships.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dealershipId, cancellationToken);

    public async Task<IReadOnlyList<Dealership>> GetActiveAsync(CancellationToken cancellationToken) =>
        await db.Dealerships.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Id).ToListAsync(cancellationToken);
}

internal sealed class ServiceTypeRepository(SchedulerDbContext db) : IServiceTypeRepository
{
    public Task<ServiceType?> GetByIdAsync(int serviceTypeId, CancellationToken cancellationToken) =>
        db.ServiceTypes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceTypeId, cancellationToken);

    public async Task<IReadOnlyList<ServiceType>> GetActiveAsync(int? dealershipId, CancellationToken cancellationToken)
    {
        var query = db.ServiceTypes.AsNoTracking().Where(s => s.IsActive);
        if (dealershipId is { } id)
        {
            query = query.Where(s => s.DealershipId == id);
        }

        return await query.OrderBy(s => s.Id).ToListAsync(cancellationToken);
    }
}

internal sealed class VehicleRepository(SchedulerDbContext db) : IVehicleRepository
{
    public Task<Vehicle?> GetByIdAsync(int vehicleId, CancellationToken cancellationToken) =>
        db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);

    public async Task<IReadOnlyList<Vehicle>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken) =>
        await db.Vehicles.AsNoTracking().Where(v => v.CustomerId == customerId).OrderBy(v => v.Id).ToListAsync(cancellationToken);
}

internal sealed class CustomerRepository(SchedulerDbContext db) : ICustomerRepository
{
    public Task<bool> ExistsAsync(int customerId, CancellationToken cancellationToken) =>
        db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken);
}
