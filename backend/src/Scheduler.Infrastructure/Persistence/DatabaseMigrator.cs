using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Scheduler.Infrastructure.Persistence;

/// <summary>
/// Applies EF migrations (which include the seed data) at startup when <c>Database:MigrateOnStartup</c> is true.
/// It retries a few times while SQL Server is still starting (for example right after <c>docker compose up</c>).
/// </summary>
public static class DatabaseMigrator
{
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    private const int MaxAttempts = 10;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan MigrationCommandTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Runs the migrations if the setting is on; does nothing otherwise.</summary>
    public static async Task MigrateDatabaseIfEnabledAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        var configuration = services.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>(MigrateOnStartupKey))
        {
            return;
        }

        await MigrateAsync(services, cancellationToken);
    }

    /// <summary>Applies all pending migrations.</summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrator).FullName!);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
                db.Database.SetCommandTimeout(MigrationCommandTimeout);
                await db.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Database migrations applied");
                return;
            }
            catch (SqlException ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(
                    ex,
                    "Database not ready (attempt {Attempt} of {MaxAttempts}); retrying in {Delay}",
                    attempt,
                    MaxAttempts,
                    RetryDelay);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }
}
