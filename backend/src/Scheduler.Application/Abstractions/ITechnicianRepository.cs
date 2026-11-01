using Scheduler.Domain.Entities;

namespace Scheduler.Application.Abstractions;

/// <summary>Reads technicians.</summary>
public interface ITechnicianRepository
{
    /// <summary>
    /// All technicians of the dealership (active AND inactive) with their Skills loaded. Read only.
    /// The Application filters with domain rules (IsActive, IsQualifiedFor, ShiftCovers), because tiers are stored
    /// as strings and must be compared by enum order.
    /// </summary>
    Task<IReadOnlyList<Technician>> GetByDealershipWithSkillsAsync(int dealershipId, CancellationToken cancellationToken);
}
