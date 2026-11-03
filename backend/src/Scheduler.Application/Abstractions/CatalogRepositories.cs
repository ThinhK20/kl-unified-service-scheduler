using Scheduler.Domain.Entities;

namespace Scheduler.Application.Abstractions;

/// <summary>Reads dealerships. Read only.</summary>
public interface IDealershipRepository
{
    Task<Dealership?> GetByIdAsync(int dealershipId, CancellationToken cancellationToken);

    /// <summary>Active dealerships, ordered by Id.</summary>
    Task<IReadOnlyList<Dealership>> GetActiveAsync(CancellationToken cancellationToken);
}

/// <summary>Reads service types. Read only.</summary>
public interface IServiceTypeRepository
{
    Task<ServiceType?> GetByIdAsync(int serviceTypeId, CancellationToken cancellationToken);

    /// <summary>Active service types, ordered by Id; only those of <paramref name="dealershipId"/> when it is given.</summary>
    Task<IReadOnlyList<ServiceType>> GetActiveAsync(int? dealershipId, CancellationToken cancellationToken);

    /// <summary>All service types of the dealership (active AND inactive), ordered by Id.</summary>
    Task<IReadOnlyList<ServiceType>> GetByDealershipAsync(int dealershipId, CancellationToken cancellationToken);
}

/// <summary>Reads vehicles. Read only.</summary>
public interface IVehicleRepository
{
    Task<Vehicle?> GetByIdAsync(int vehicleId, CancellationToken cancellationToken);

    /// <summary>Vehicles of the customer, ordered by Id.</summary>
    Task<IReadOnlyList<Vehicle>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken);
}

/// <summary>Reads customers.</summary>
public interface ICustomerRepository
{
    Task<bool> ExistsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>All customers, ordered by Id. Read only.</summary>
    Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken);
}
