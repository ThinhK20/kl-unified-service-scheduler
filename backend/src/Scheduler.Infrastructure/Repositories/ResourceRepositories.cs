using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Abstractions;
using Scheduler.Domain.Entities;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Repositories;

/// <summary>Technicians of a dealership, active and inactive, with skills. Read only.</summary>
internal sealed class TechnicianRepository(SchedulerDbContext db) : ITechnicianRepository
{
    public async Task<IReadOnlyList<Technician>> GetByDealershipWithSkillsAsync(int dealershipId, CancellationToken cancellationToken) =>
        await db.Technicians
            .AsNoTracking()
            .Include(t => t.Skills)
            .Where(t => t.DealershipId == dealershipId)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);
}

/// <summary>Service bays of a dealership, active and inactive. Read only.</summary>
internal sealed class ServiceBayRepository(SchedulerDbContext db) : IServiceBayRepository
{
    public async Task<IReadOnlyList<ServiceBay>> GetByDealershipAsync(int dealershipId, CancellationToken cancellationToken) =>
        await db.ServiceBays
            .AsNoTracking()
            .Where(b => b.DealershipId == dealershipId)
            .OrderBy(b => b.Id)
            .ToListAsync(cancellationToken);
}
