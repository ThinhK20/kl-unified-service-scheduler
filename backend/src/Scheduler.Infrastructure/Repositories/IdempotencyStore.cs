using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Abstractions;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Repositories;

/// <summary>Reads saved Idempotency-Key rows straight from the database (no tracking, no cache).</summary>
internal sealed class IdempotencyStore(SchedulerDbContext db) : IIdempotencyStore
{
    public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        return DbCall.RunAsync(() => db.IdempotencyKeys
            .AsNoTracking()
            .Where(k => k.Key == key)
            .Select(k => new IdempotencyRecord(k.Key, k.RequestHash, k.AppointmentId, k.ResponseJson, k.CreatedUtc))
            .FirstOrDefaultAsync(cancellationToken));
    }
}
