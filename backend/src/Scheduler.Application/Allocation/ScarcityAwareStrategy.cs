using Scheduler.Domain.Entities;

namespace Scheduler.Application.Allocation;

/// <summary>
/// Default strategy (SPEC section 0c). Protects scarce specialists: a technician who is the only one able to do
/// another service is tried last, so they stay free for that service. Technician order:
/// <list type="number">
/// <item>fewest OTHER active services of the dealership for which this technician is the ONLY active qualified
/// technician ("sole cover"). This is counted over ALL active technicians of the dealership, not only the ones
/// free or on shift right now, so the answer does not change during the day;</item>
/// <item>fewest OTHER active services of the dealership they are qualified for (skill and minimum tier);</item>
/// <item>lowest tier in the required skill;</item>
/// <item>fewest confirmed appointments that local day;</item>
/// <item>lowest Id.</item>
/// </list>
/// Bays in Id order. Pairs are technician-major, and every pair is returned (same contract as
/// <see cref="LeastQualifiedFirstStrategy"/>).
/// </summary>
public sealed class ScarcityAwareStrategy : IResourceAllocationStrategy
{
    public IReadOnlyList<ResourcePair> Order(AllocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var service = request.ServiceType;

        var otherServices = request.ActiveServiceTypes.Where(s => s.Id != service.Id).ToList();
        var qualifiedCountByService = otherServices.ToDictionary(
            s => s.Id,
            s => request.DealershipTechnicians.Count(t => t.IsActive && IsQualified(t, s)));

        int SoleCover(Technician t) =>
            otherServices.Count(s => IsQualified(t, s) && qualifiedCountByService[s.Id] == 1);

        int OtherQualified(Technician t) => otherServices.Count(s => IsQualified(t, s));

        var technicians = request.Technicians
            // A technician not qualified for this service should not be here; keep it (never drop pairs) but put it last.
            .OrderBy(t => IsQualified(t, service) ? 0 : 1)
            .ThenBy(SoleCover)
            .ThenBy(OtherQualified)
            .ThenBy(t => t.TierFor(service.RequiredSkill) is { } tier ? (int)tier : int.MaxValue)
            .ThenBy(t => request.ConfirmedAppointmentsByTechnician.GetValueOrDefault(t.Id))
            .ThenBy(t => t.Id);

        var bays = request.ServiceBays.OrderBy(b => b.Id).ToList();

        return technicians
            .SelectMany(technician => bays.Select(bay => new ResourcePair(technician, bay)))
            .ToList();
    }

    private static bool IsQualified(Technician technician, ServiceType serviceType) =>
        technician.IsQualifiedFor(serviceType.RequiredSkill, serviceType.MinimumTier);
}
