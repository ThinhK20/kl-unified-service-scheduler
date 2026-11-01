using Scheduler.Domain.Entities;

namespace Scheduler.Application.Allocation;

/// <summary>
/// Decides in which order (technician, bay) pairs are tried. New allocation rules are new strategies (open/closed).
/// Contract (Liskov): return EVERY pair of the given technicians and bays exactly once, only in a different order.
/// Never drop or add a pair.
/// </summary>
public interface IResourceAllocationStrategy
{
    IReadOnlyList<ResourcePair> Order(AllocationRequest request);
}

/// <summary>Free, valid candidates for one time range.</summary>
/// <param name="ServiceType">The service being booked.</param>
/// <param name="Technicians">Free, active, qualified technicians on shift.</param>
/// <param name="ServiceBays">Free, active bays of the required type.</param>
/// <param name="ConfirmedAppointmentsByTechnician">Confirmed appointments that local day, per technician id (missing = 0).</param>
public sealed record AllocationRequest(
    ServiceType ServiceType,
    IReadOnlyList<Technician> Technicians,
    IReadOnlyList<ServiceBay> ServiceBays,
    IReadOnlyDictionary<int, int> ConfirmedAppointmentsByTechnician);

/// <summary>One technician and one bay to try together.</summary>
public sealed record ResourcePair(Technician Technician, ServiceBay ServiceBay);
