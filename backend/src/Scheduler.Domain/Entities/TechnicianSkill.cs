using Scheduler.Domain.Enums;

namespace Scheduler.Domain.Entities;

/// <summary>One skill of a technician and the tier they have in it. Key: (TechnicianId, Skill).</summary>
public sealed class TechnicianSkill
{
    private TechnicianSkill()
    {
    }

    internal TechnicianSkill(Skill skill, SkillTier tier)
    {
        Skill = skill;
        Tier = tier;
    }

    public int TechnicianId { get; private set; }

    public Skill Skill { get; private set; }

    public SkillTier Tier { get; private set; }

    internal void ChangeTier(SkillTier tier) => Tier = tier;
}
