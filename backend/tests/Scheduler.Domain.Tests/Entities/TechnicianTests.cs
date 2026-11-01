using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Domain.Tests.TestSupport;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Domain.Tests.Entities;

public class TechnicianTests
{
    private static readonly TimeZoneInfo HoChiMinh = TimeZoneInfo.FindSystemTimeZoneById(Seed.HoChiMinh);

    [Theory]
    [InlineData(SkillTier.Trainee, true)]
    [InlineData(SkillTier.Qualified, true)]
    [InlineData(SkillTier.Senior, true)]
    [InlineData(SkillTier.Master, false)]
    public void Senior_is_qualified_up_to_senior(SkillTier minimumTier, bool expected)
    {
        var technician = Technician.Create(1, "Lan", new TimeOnly(8, 0), new TimeOnly(17, 0))
            .WithSkill(Skill.EV, SkillTier.Senior);

        technician.IsQualifiedFor(Skill.EV, minimumTier).Should().Be(expected);
    }

    [Fact]
    public void Missing_skill_is_not_qualified_even_for_trainee()
    {
        var technician = Seed.Nam();

        technician.IsQualifiedFor(Skill.EV, SkillTier.Trainee).Should().BeFalse();
        technician.TierFor(Skill.EV).Should().BeNull();
    }

    [Fact]
    public void WithSkill_twice_changes_the_tier_instead_of_adding()
    {
        var technician = Technician.Create(1, "Minh", new TimeOnly(12, 0), new TimeOnly(17, 0))
            .WithSkill(Skill.Brakes, SkillTier.Trainee)
            .WithSkill(Skill.Brakes, SkillTier.Qualified);

        technician.Skills.Should().ContainSingle();
        technician.TierFor(Skill.Brakes).Should().Be(SkillTier.Qualified);
    }

    [Fact]
    public void Tiers_are_ordered()
    {
        (SkillTier.Trainee < SkillTier.Qualified).Should().BeTrue();
        (SkillTier.Qualified < SkillTier.Senior).Should().BeTrue();
        (SkillTier.Senior < SkillTier.Master).Should().BeTrue();
    }

    // Minh works 12:00-17:00 local = 05:00-10:00 UTC.
    private static Technician Minh() => Technician.Create(1, "Minh", new TimeOnly(12, 0), new TimeOnly(17, 0));

    [Fact]
    public void Shift_covers_range_inside_local_shift()
    {
        Minh().ShiftCovers(TimeRange.Create(Utc.At(5, 0), Utc.At(6, 30)), HoChiMinh).Should().BeTrue();
    }

    [Fact]
    public void Shift_covers_range_ending_exactly_at_shift_end()
    {
        Minh().ShiftCovers(TimeRange.Create(Utc.At(9, 0), Utc.At(10, 0)), HoChiMinh).Should().BeTrue();
    }

    [Fact]
    public void Shift_does_not_cover_range_ending_one_minute_after_shift_end()
    {
        Minh().ShiftCovers(TimeRange.Create(Utc.At(9, 1), Utc.At(10, 1)), HoChiMinh).Should().BeFalse();
    }

    [Fact]
    public void Shift_does_not_cover_range_starting_before_shift()
    {
        // 11:45-12:45 local
        Minh().ShiftCovers(TimeRange.Create(Utc.At(4, 45), Utc.At(5, 45)), HoChiMinh).Should().BeFalse();
    }

    [Fact]
    public void Shift_is_checked_in_local_time_not_utc()
    {
        // 12:00-13:00 UTC is inside 12:00-17:00 if read as UTC, but it is 19:00-20:00 local.
        Minh().ShiftCovers(TimeRange.Create(Utc.At(12, 0), Utc.At(13, 0)), HoChiMinh).Should().BeFalse();
    }

    [Fact]
    public void Shift_is_checked_with_utc_when_given_utc_zone()
    {
        Minh().ShiftCovers(TimeRange.Create(Utc.At(12, 0), Utc.At(13, 0)), TimeZoneInfo.Utc).Should().BeTrue();
    }

    [Fact]
    public void CanDo_is_false_for_inactive_technician()
    {
        var technician = Seed.Nam();
        technician.Deactivate();

        technician.CanDo(Seed.OilChange(), Seed.District1(), TimeRange.Create(Utc.At(3, 0), Utc.At(4, 0)))
            .Should().BeFalse();
    }

    [Fact]
    public void CanDo_is_false_for_technician_at_another_dealership()
    {
        var technician = Seed.Nam(dealershipId: 2);

        technician.CanDo(Seed.OilChange(), Seed.District1(), TimeRange.Create(Utc.At(3, 0), Utc.At(4, 0)))
            .Should().BeFalse();
    }

    [Fact]
    public void CanDo_is_true_for_active_qualified_technician_on_shift()
    {
        Seed.Nam().CanDo(Seed.OilChange(), Seed.District1(), TimeRange.Create(Utc.At(3, 0), Utc.At(4, 0)))
            .Should().BeTrue();
    }
}
