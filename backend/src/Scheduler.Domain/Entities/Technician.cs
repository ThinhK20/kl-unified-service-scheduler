using Scheduler.Domain.Common;
using Scheduler.Domain.Enums;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Domain.Entities;

/// <summary>
/// A technician at one dealership. The shift is in dealership local time and the same every day.
/// </summary>
public sealed class Technician : Entity
{
    private readonly List<TechnicianSkill> _skills = [];

    private Technician()
    {
    }

    public int DealershipId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Local shift start.</summary>
    public TimeOnly ShiftStart { get; private set; }

    /// <summary>Local shift end. Work may end exactly at this time.</summary>
    public TimeOnly ShiftEnd { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<TechnicianSkill> Skills => _skills.AsReadOnly();

    private DailyWindow Shift => new(ShiftStart, ShiftEnd);

    public static Technician Create(int dealershipId, string name, TimeOnly shiftStart, TimeOnly shiftEnd)
    {
        _ = new DailyWindow(shiftStart, shiftEnd);
        return new Technician
        {
            DealershipId = Guard.Positive(dealershipId, nameof(DealershipId)),
            Name = Guard.NotBlank(name, nameof(Name)),
            ShiftStart = shiftStart,
            ShiftEnd = shiftEnd,
            IsActive = true,
        };
    }

    /// <summary>Adds a skill, or changes the tier if the technician already has it.</summary>
    public Technician WithSkill(Skill skill, SkillTier tier)
    {
        var existing = _skills.Find(s => s.Skill == skill);
        if (existing is null)
        {
            _skills.Add(new TechnicianSkill(skill, tier));
        }
        else
        {
            existing.ChangeTier(tier);
        }

        return this;
    }

    /// <summary>The technician tier in the skill, or null if they do not have it.</summary>
    public SkillTier? TierFor(Skill skill) => _skills.Find(s => s.Skill == skill)?.Tier;

    /// <summary>True when the technician has the skill at <paramref name="minimumTier"/> or higher.</summary>
    public bool IsQualifiedFor(Skill skill, SkillTier minimumTier) => TierFor(skill) is { } tier && tier >= minimumTier;

    /// <summary>
    /// True when the whole UTC range is inside the shift on the local day of its start.
    /// This does not look at <see cref="IsActive"/>.
    /// </summary>
    public bool ShiftCovers(TimeRange range, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(timeZone);
        return Shift.Covers(range, timeZone);
    }

    /// <summary>
    /// True when the technician is active, works at the dealership, is qualified for the service
    /// and is on shift for the whole range.
    /// </summary>
    public bool CanDo(ServiceType serviceType, Dealership dealership, TimeRange range)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(dealership);
        return IsActive
            && DealershipId == dealership.Id
            && IsQualifiedFor(serviceType.RequiredSkill, serviceType.MinimumTier)
            && ShiftCovers(range, dealership.TimeZone);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
