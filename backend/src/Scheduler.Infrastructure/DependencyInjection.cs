using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Abstractions;
using Scheduler.Infrastructure.Persistence;
using Scheduler.Infrastructure.Repositories;
using Scheduler.Infrastructure.Time;

namespace Scheduler.Infrastructure;

/// <summary>Registers Infrastructure services (database, repositories, unit of work, clock).</summary>
public static class DependencyInjection
{
    public const string ConnectionStringName = "Scheduler";

    public const string CommandTimeoutKey = "Database:CommandTimeoutSeconds";

    private const int DefaultCommandTimeoutSeconds = 15;

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var commandTimeout = configuration.GetValue(CommandTimeoutKey, DefaultCommandTimeoutSeconds);

        services.AddDbContext<SchedulerDbContext>((serviceProvider, options) =>
        {
            // Read the connection string when the context is built, so tests can override configuration late.
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

            // No EnableRetryOnFailure: the booking code uses its own transactions and its own retry,
            // and the retrying execution strategy does not allow user-started transactions.
            options.UseSqlServer(connectionString, sql => sql.CommandTimeout(commandTimeout));
        });

        // One scoped DbContext per request; every repository and the unit of work share it.
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<ITechnicianRepository, TechnicianRepository>();
        services.AddScoped<IServiceBayRepository, ServiceBayRepository>();
        services.AddScoped<IDealershipRepository, DealershipRepository>();
        services.AddScoped<IServiceTypeRepository, ServiceTypeRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
