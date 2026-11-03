using Scheduler.Application.Catalog.Dtos;
using Scheduler.Application.Catalog.Queries;
using Scheduler.Application.Common;
using Scheduler.Application.Tests.TestSupport;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Tests.Catalog;

/// <summary>SPEC section 6: GET /api/dealerships/{id}/setup.</summary>
public class DealershipSetupTests
{
    private readonly TestApp _app = new();

    private async Task<DealershipSetupDto> SetupAsync(int dealershipId)
    {
        var result = await _app.DealershipSetupHandler().HandleAsync(new GetDealershipSetupQuery(dealershipId), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    [Fact]
    public async Task Dealership_1_lists_everything_with_who_and_which_bays_can_do_each_service()
    {
        var dto = await SetupAsync(1);

        dto.Dealership.Should().Be(new DealershipSetupInfoDto(1, "District 1 Service Centre", "Asia/Ho_Chi_Minh", "08:00", "17:00", true));
        dto.ServiceTypes.Select(s => (s.Id, s.QualifiedTechnicianIds.ToArray(), s.MatchingBayIds.ToArray())).Should().BeEquivalentTo(
            new[]
            {
                (1, new[] { 1, 2, 3 }, new[] { 1 }), // Tuan is qualified but inactive
                (2, new[] { 1, 3 }, new[] { 2 }),    // Bay 4 is a lift but inactive
                (3, new[] { 1, 2 }, new[] { 2 }),
                (4, new[] { 2 }, new[] { 3 }),       // only Lan
            },
            options => options.WithStrictOrdering());
        dto.ServiceTypes[3].Should().BeEquivalentTo(new { Name = "EV battery check", DurationMinutes = 60, RequiredSkill = "EV", MinimumTier = "Senior", RequiredBayType = "EV", IsActive = true });
        dto.Technicians.Select(t => (t.Name, t.IsActive)).Should().Equal(("Lan", true), ("Minh", true), ("Nam", true), ("Tuan", false));
        dto.Technicians[0].Should().BeEquivalentTo(new { Id = 2, ShiftStart = "08:00", ShiftEnd = "17:00" });
        dto.Technicians[0].Skills.Should().Equal(new SetupSkillDto("General", "Qualified"), new SetupSkillDto("Tyres", "Qualified"), new SetupSkillDto("EV", "Senior"));
        dto.Technicians[1].ShiftStart.Should().Be("12:00");
        dto.ServiceBays.Select(b => (b.Id, b.Name, b.BayType, b.IsActive)).Should().Equal(
            (1, "Bay 1", "Standard", true), (2, "Bay 2", "Lift", true), (3, "Bay 3", "EV", true), (5, "Bay 4", "Lift", false));
    }

    [Fact]
    public async Task Skills_follow_the_skill_enum_order_not_the_order_they_were_added()
    {
        var technician = Technician.Create(1, "Zed", new TimeOnly(9, 0), new TimeOnly(15, 0))
            .WithSkill(Skill.Diagnostics, SkillTier.Master).WithSkill(Skill.EV, SkillTier.Trainee).WithSkill(Skill.General, SkillTier.Senior).WithId(50);
        _app.Data.Technicians.Add(technician);

        var dto = await SetupAsync(1);

        dto.Technicians.Single(t => t.Id == 50).Skills.Select(s => s.Skill).Should().Equal("General", "EV", "Diagnostics");
    }

    [Fact]
    public async Task Inactive_items_are_listed_last_by_name_and_never_counted_as_qualified_or_matching()
    {
        _app.Data.GetTechnician(2).Deactivate();          // Lan
        _app.Data.ServiceBays.Single(b => b.Id == 3).Deactivate(); // Bay 3 (EV)
        _app.Data.GetServiceType(4).Deactivate();
        _app.Data.Technicians.Add(Technician.Create(1, "anh", new TimeOnly(8, 0), new TimeOnly(17, 0)).WithSkill(Skill.General, SkillTier.Trainee).WithId(60));

        var dto = await SetupAsync(1);

        dto.Technicians.Select(t => t.Name).Should().Equal("anh", "Minh", "Nam", "Lan", "Tuan"); // case-insensitive names
        dto.ServiceBays.Select(b => b.Name).Should().Equal("Bay 1", "Bay 2", "Bay 3", "Bay 4");
        dto.ServiceBays.Select(b => b.IsActive).Should().Equal(true, true, false, false);
        var ev = dto.ServiceTypes.Single(s => s.Id == 4);
        ev.IsActive.Should().BeFalse("inactive service types are listed too");
        ev.QualifiedTechnicianIds.Should().BeEmpty();
        ev.MatchingBayIds.Should().BeEmpty();
        dto.ServiceTypes.Single(s => s.Id == 1).QualifiedTechnicianIds.Should().Equal(1, 3, 60);
    }

    [Fact]
    public async Task Same_name_is_ordered_by_id()
    {
        _app.Data.Technicians.Add(Technician.Create(1, "Nam", new TimeOnly(8, 0), new TimeOnly(17, 0)).WithId(40));

        var dto = await SetupAsync(1);

        dto.Technicians.Where(t => t.Name == "Nam").Select(t => t.Id).Should().Equal(1, 40);
    }

    [Fact]
    public async Task An_inactive_dealership_is_still_returned()
    {
        _app.Data.GetDealership(2).Deactivate();

        var dto = await SetupAsync(2);

        dto.Dealership.IsActive.Should().BeFalse();
        dto.ServiceTypes.Should().ContainSingle().Which.QualifiedTechnicianIds.Should().Equal(4);
        dto.Technicians.Should().ContainSingle().Which.Name.Should().Be("Hoa");
        dto.ServiceBays.Should().ContainSingle().Which.Name.Should().Be("Bay A");
    }

    [Fact]
    public async Task Unknown_dealership_is_not_found()
    {
        var result = await _app.DealershipSetupHandler().HandleAsync(new GetDealershipSetupQuery(99), CancellationToken.None);

        result.Error!.Code.Should().Be(ErrorCodes.NotFound);
        result.Error.Message.Should().Be("Dealership 99 was not found.");
    }
}
