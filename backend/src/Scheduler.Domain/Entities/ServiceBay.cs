using Scheduler.Domain.Common;
using Scheduler.Domain.Enums;

namespace Scheduler.Domain.Entities;

/// <summary>A work bay at one dealership.</summary>
public sealed class ServiceBay : Entity
{
    private ServiceBay()
    {
    }

    public int DealershipId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public BayType BayType { get; private set; }

    public bool IsActive { get; private set; }

    public static ServiceBay Create(int dealershipId, string name, BayType bayType) => new()
    {
        DealershipId = Guard.Positive(dealershipId, nameof(DealershipId)),
        Name = Guard.NotBlank(name, nameof(Name)),
        BayType = bayType,
        IsActive = true,
    };

    /// <summary>True when this bay is exactly the required type. This does not look at <see cref="IsActive"/>.</summary>
    public bool Matches(BayType requiredBayType) => BayType == requiredBayType;

    /// <summary>True when the bay is active, at the dealership and of the required type.</summary>
    public bool CanHost(ServiceType serviceType, int dealershipId)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return IsActive && DealershipId == dealershipId && Matches(serviceType.RequiredBayType);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
