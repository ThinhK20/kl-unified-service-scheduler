using Scheduler.Application.Abstractions;

namespace Scheduler.Infrastructure.Repositories;

/// <summary>
/// Runs a read query and turns a transient database error (deadlock, timeout) into a
/// <see cref="TransientDatabaseException"/>, so the booking handler's single retry and DATABASE_BUSY cover reads too.
/// Every other exception passes through unchanged.
/// </summary>
internal static class DbCall
{
    public static async Task<T> RunAsync<T>(Func<Task<T>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            return await query();
        }
        catch (Exception ex) when (ex is not TransientDatabaseException && SqlErrorClassifier.Classify(ex) == SqlErrorKind.Transient)
        {
            throw new TransientDatabaseException("The database was busy while reading data.", ex);
        }
    }
}
