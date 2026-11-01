using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Abstractions;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Repositories;

/// <summary>Saves the tracked changes of the request's <see cref="SchedulerDbContext"/> (one transaction per call).</summary>
internal sealed class UnitOfWork(SchedulerDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The row was changed by another request.", ex);
        }
        catch (Exception ex) when (SqlErrorClassifier.Classify(ex) == SqlErrorKind.Transient)
        {
            throw new TransientDatabaseException("The database was busy while saving changes.", ex);
        }
    }
}
