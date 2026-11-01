namespace Scheduler.Application.Allocation;

/// <summary>
/// Default strategy (SPEC section 5 step 5). Keeps senior people free for harder jobs:
/// technicians with the LOWEST tier in the required skill first, then fewest confirmed appointments that local day,
/// then lowest Id. Bays in Id order (all matching bays are equal, because the match is exact).
/// Pairs are technician-major: every bay is tried with the first technician before the next technician.
/// </summary>
public sealed class LeastQualifiedFirstStrategy : IResourceAllocationStrategy
{
    public IReadOnlyList<ResourcePair> Order(AllocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var skill = request.ServiceType.RequiredSkill;

        var technicians = request.Technicians
            // A technician without the skill should not be here; keep it (never drop pairs) but put it last.
            .OrderBy(t => t.TierFor(skill) is { } tier ? (int)tier : int.MaxValue)
            .ThenBy(t => request.ConfirmedAppointmentsByTechnician.GetValueOrDefault(t.Id))
            .ThenBy(t => t.Id);

        var bays = request.ServiceBays.OrderBy(b => b.Id).ToList();

        return technicians
            .SelectMany(technician => bays.Select(bay => new ResourcePair(technician, bay)))
            .ToList();
    }
}
