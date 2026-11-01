using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Abstractions;
using Scheduler.Domain.Entities;
using Scheduler.Infrastructure;
using Scheduler.IntegrationTests.Infrastructure;

namespace Scheduler.IntegrationTests.Repositories;

/// <summary>
/// Builds the real Infrastructure service registrations (<see cref="DependencyInjection.AddInfrastructure"/>)
/// against the test database. Each <see cref="RepositoryScope"/> is like one HTTP request: one scoped DbContext
/// shared by all repositories.
/// </summary>
internal sealed class RepositoryHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    public RepositoryHarness(SqlServerFixture fixture)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = fixture.ConnectionString,
            })
            .Build();

        _services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public RepositoryScope CreateScope() => new(_services.CreateAsyncScope());

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}

internal sealed class RepositoryScope(AsyncServiceScope scope) : IAsyncDisposable
{
    public IAppointmentRepository Appointments => Get<IAppointmentRepository>();

    public IUnitOfWork UnitOfWork => Get<IUnitOfWork>();

    public IIdempotencyStore Idempotency => Get<IIdempotencyStore>();

    public T Get<T>()
        where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>
    /// Loads the seeded entities through the repositories (no tracking, as the booking handler does) and builds
    /// an appointment with the domain factory.
    /// </summary>
    public async Task<Appointment> NewAppointmentAsync(int serviceTypeId, int technicianId, int bayId, DateTime startUtc, int vehicleId = 1)
    {
        var ct = CancellationToken.None;
        var serviceType = (await Get<IServiceTypeRepository>().GetByIdAsync(serviceTypeId, ct))!;
        var dealership = (await Get<IDealershipRepository>().GetByIdAsync(serviceType.DealershipId, ct))!;
        var vehicle = (await Get<IVehicleRepository>().GetByIdAsync(vehicleId, ct))!;
        var technician = (await Get<ITechnicianRepository>().GetByDealershipWithSkillsAsync(dealership.Id, ct)).Single(t => t.Id == technicianId);
        var bay = (await Get<IServiceBayRepository>().GetByDealershipAsync(dealership.Id, ct)).Single(b => b.Id == bayId);
        return Appointment.Create(vehicle.CustomerId, vehicle, dealership, serviceType, technician, bay, startUtc, FixedClock.DefaultNow);
    }

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
