using Scheduler.Domain.Common;
using Scheduler.Domain.Enums;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Domain.Entities;

/// <summary>
/// A service a dealership offers, with the rules to book it: duration, skill, minimum tier and bay type.
/// </summary>
public sealed class ServiceType : Entity
{
    private ServiceType()
    {
    }

    public int DealershipId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Duration in minutes, always a multiple of <see cref="SlotGrid.SlotMinutes"/>.</summary>
    public int DurationMinutes { get; private set; }

    public Skill RequiredSkill { get; private set; }

    public SkillTier MinimumTier { get; private set; }

    public BayType RequiredBayType { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Duration as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);

    /// <summary>Creates a service type. The duration is rounded up to the next 15 minutes.</summary>
    public static ServiceType Create(
        int dealershipId,
        string name,
        int durationMinutes,
        Skill requiredSkill,
        SkillTier minimumTier,
        BayType requiredBayType) => new()
    {
        DealershipId = Guard.Positive(dealershipId, nameof(DealershipId)),
        Name = Guard.NotBlank(name, nameof(Name)),
        DurationMinutes = SlotGrid.RoundUpToGrid(durationMinutes),
        RequiredSkill = requiredSkill,
        MinimumTier = minimumTier,
        RequiredBayType = requiredBayType,
        IsActive = true,
    };

    /// <summary>True when this service is offered at the dealership.</summary>
    public bool IsOfferedAt(int dealershipId) => DealershipId == dealershipId;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
