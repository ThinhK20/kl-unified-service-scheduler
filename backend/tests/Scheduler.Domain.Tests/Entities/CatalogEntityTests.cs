using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Domain.Errors;
using Scheduler.Domain.Tests.TestSupport;

namespace Scheduler.Domain.Tests.Entities;

public class CatalogEntityTests
{
    [Fact]
    public void ServiceType_duration_is_rounded_up_to_the_grid()
    {
        var service = ServiceType.Create(1, "Quick check", 50, Skill.General, SkillTier.Trainee, BayType.Standard);

        service.DurationMinutes.Should().Be(60);
        service.Duration.Should().Be(TimeSpan.FromMinutes(60));
        service.IsActive.Should().BeTrue();
    }

    [Fact]
    public void ServiceType_rejects_zero_duration()
    {
        var act = () => ServiceType.Create(1, "Nothing", 0, Skill.General, SkillTier.Trainee, BayType.Standard);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ServiceType_is_offered_only_at_its_dealership()
    {
        var service = Seed.OilChange(dealershipId: 1);

        service.IsOfferedAt(1).Should().BeTrue();
        service.IsOfferedAt(2).Should().BeFalse();
    }

    [Theory]
    [InlineData(BayType.Standard, true)]
    [InlineData(BayType.Lift, false)]
    [InlineData(BayType.EV, false)]
    public void Bay_matches_only_its_exact_type(BayType required, bool expected)
    {
        Seed.Bay1().Matches(required).Should().Be(expected);
    }

    [Fact]
    public void Inactive_bay_cannot_host()
    {
        var bay = Seed.Bay1();
        bay.Deactivate();

        bay.CanHost(Seed.OilChange(), dealershipId: 1).Should().BeFalse();
    }

    [Fact]
    public void Vehicle_is_owned_by_its_customer_only()
    {
        var vehicle = Seed.Vios(customerId: 1);

        vehicle.IsOwnedBy(1).Should().BeTrue();
        vehicle.IsOwnedBy(2).Should().BeFalse();
    }

    [Fact]
    public void Blank_name_is_rejected()
    {
        var act = () => Customer.Create(" ", "demo@example.com", "+84 900 000 000");

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCodes.InvalidRequest);
    }
}
