using Microsoft.Data.SqlClient;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Repositories;

/// <summary>What a database exception means for the Application.</summary>
internal enum SqlErrorKind
{
    /// <summary>Not a case we handle: rethrow it.</summary>
    Unknown,

    /// <summary>Duplicate key on <see cref="DbConstraintNames.UniqueResourceSlot"/>: a slot is already taken.</summary>
    SlotTaken,

    /// <summary>Duplicate key on <see cref="DbConstraintNames.IdempotencyKeysPrimaryKey"/>.</summary>
    IdempotencyKeyTaken,

    /// <summary>Deadlock, timeout or another short-lived error: worth one retry.</summary>
    Transient,
}

/// <summary>
/// The one place that turns SQL Server error numbers into meanings (SlotTaken, IdempotencyKeyTaken, Transient).
/// </summary>
internal static class SqlErrorClassifier
{
    /// <summary>Cannot insert duplicate key row in object with unique index.</summary>
    public const int DuplicateKeyInUniqueIndex = 2601;

    /// <summary>Violation of PRIMARY KEY or UNIQUE constraint.</summary>
    public const int DuplicateKeyInConstraint = 2627;

    /// <summary>Transaction was chosen as the deadlock victim.</summary>
    public const int Deadlock = 1205;

    /// <summary>Client-side command timeout (SqlClient reports it as number -2).</summary>
    public const int Timeout = -2;

    /// <summary>Error numbers that usually go away on retry (deadlock, timeouts, lock timeout, Azure SQL throttling).</summary>
    private static readonly HashSet<int> TransientNumbers =
    [
        Deadlock,
        Timeout,
        1222,   // lock request time out period exceeded
        40197,  // service error processing the request
        40501,  // service is busy
        40613,  // database not currently available
        49918,  // not enough resources
        49919,  // too many create/update operations
        49920,  // too many operations
    ];

    public static SqlErrorKind Classify(Exception exception)
    {
        var sql = FindSqlException(exception);
        if (sql is null)
        {
            return exception is TimeoutException || exception.InnerException is TimeoutException
                ? SqlErrorKind.Transient
                : SqlErrorKind.Unknown;
        }

        if (sql.Number is DuplicateKeyInUniqueIndex or DuplicateKeyInConstraint)
        {
            if (Mentions(sql, DbConstraintNames.UniqueResourceSlot))
            {
                return SqlErrorKind.SlotTaken;
            }

            if (Mentions(sql, DbConstraintNames.IdempotencyKeysPrimaryKey))
            {
                return SqlErrorKind.IdempotencyKeyTaken;
            }

            return SqlErrorKind.Unknown;
        }

        return sql.Errors.Cast<SqlError>().Any(e => TransientNumbers.Contains(e.Number)) || TransientNumbers.Contains(sql.Number)
            ? SqlErrorKind.Transient
            : SqlErrorKind.Unknown;
    }

    private static SqlException? FindSqlException(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql)
            {
                return sql;
            }
        }

        return null;
    }

    private static bool Mentions(SqlException exception, string name) =>
        exception.Message.Contains($"'{name}'", StringComparison.OrdinalIgnoreCase);
}
