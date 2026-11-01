using Scheduler.Domain.Entities;

namespace Scheduler.Application.Abstractions;

/// <summary>Reads service bays.</summary>
public interface IServiceBayRepository
{
    /// <summary>All bays of the dealership (active AND inactive). Read only. The Application filters them.</summary>
    Task<IReadOnlyList<ServiceBay>> GetByDealershipAsync(int dealershipId, CancellationToken cancellationToken);
}
