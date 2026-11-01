using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Scheduler.Infrastructure.Persistence;

/// <summary>
/// Used only by the <c>dotnet ef</c> tools, so migrations can be added without starting the API.
/// The connection string comes from the <c>ConnectionStrings__Scheduler</c> environment variable, or a local default.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SchedulerDbContext>
{
    private const string DefaultConnectionString =
        "Server=localhost,1433;Database=Scheduler;User Id=sa;Password=Your_strong_Passw0rd;TrustServerCertificate=True";

    public SchedulerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Scheduler") ?? DefaultConnectionString;
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new SchedulerDbContext(options);
    }
}
