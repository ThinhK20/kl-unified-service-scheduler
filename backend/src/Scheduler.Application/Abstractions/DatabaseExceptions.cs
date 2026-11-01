namespace Scheduler.Application.Abstractions;

/// <summary>
/// A short-lived database problem (deadlock 1205, timeout). Repositories and the unit of work throw this
/// instead of provider exceptions, so the Application can retry without knowing about SQL Server.
/// </summary>
public sealed class TransientDatabaseException : Exception
{
    public TransientDatabaseException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The row was changed by someone else since it was read (rowversion mismatch).
/// Thrown by <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
