namespace Scheduler.Domain.Enums;

/// <summary>
/// How experienced a technician is in a skill. The order matters:
/// Trainee &lt; Qualified &lt; Senior &lt; Master. Compare tiers by their enum value.
/// </summary>
public enum SkillTier
{
    Trainee = 0,
    Qualified = 1,
    Senior = 2,
    Master = 3,
}
