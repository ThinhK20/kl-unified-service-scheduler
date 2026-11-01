namespace Scheduler.Application.Abstractions;

/// <summary>Saves tracked changes (used by cancel).</summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Saves all tracked changes in one transaction. Throws <see cref="ConcurrencyConflictException"/> on a
    /// rowversion mismatch and <see cref="TransientDatabaseException"/> on deadlocks / timeouts.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
