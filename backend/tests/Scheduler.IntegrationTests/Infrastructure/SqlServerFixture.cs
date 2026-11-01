using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Scheduler.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Scheduler.IntegrationTests.Infrastructure;

/// <summary>
/// One SQL Server container for the whole test run (shared through <see cref="SqlServerCollection"/>).
/// The migrations (with seed data) are applied once. Tests must not depend on an empty Appointments table:
/// each test books on its own date so tests do not collide.
/// Run with TESTCONTAINERS_RYUK_DISABLED=true when Docker Hub is not reachable; this fixture disposes the container itself.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-latest";

    private readonly MsSqlContainer _container = new MsSqlBuilder().WithImage(Image).Build();

    /// <summary>Connection string to the migrated "Scheduler" database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "Scheduler",
            TrustServerCertificate = true,
        }.ConnectionString;

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    /// <summary>A new context on the test database. The caller disposes it.</summary>
    public SchedulerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new SchedulerDbContext(options);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
