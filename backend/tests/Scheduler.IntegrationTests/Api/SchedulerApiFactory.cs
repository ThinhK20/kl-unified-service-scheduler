using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scheduler.Application.Abstractions;
using Scheduler.IntegrationTests.Infrastructure;

namespace Scheduler.IntegrationTests.Api;

/// <summary>
/// The real API in memory, on the shared SQL Server container, with a <see cref="FixedClock"/>.
/// One instance per test run (see <see cref="For"/>), because xUnit creates a test class per test.
/// </summary>
public sealed class SchedulerApiFactory : WebApplicationFactory<Program>
{
    private static readonly object Gate = new();
    private static SchedulerApiFactory? _shared;

    private readonly string _connectionString;

    private SchedulerApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>The clock the API uses. Tests in the collection run one at a time, so they may change it.</summary>
    public FixedClock Clock { get; } = new();

    /// <summary>The shared factory for this test run's database.</summary>
    public static SchedulerApiFactory For(SqlServerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        lock (Gate)
        {
            _shared ??= new SchedulerApiFactory(fixture.ConnectionString);
            _shared.Clock.UtcNow = FixedClock.DefaultNow;
            return _shared;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Scheduler"] = _connectionString,
            ["Database:MigrateOnStartup"] = "false", // the fixture already migrated
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }
}
