using Scheduler.Application.Allocation;
using Scheduler.Application.Tests.Fakes;
using Scheduler.Application.Tests.TestSupport;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Tests.Allocation;

public class ScarcityAwareStrategyTests
{
    private readonly FakeData _data = new();
    private readonly ScarcityAwareStrategy _strategy = new();

    // ---------- Seed data (District 1) ----------

    [Fact]
    public void Oil_change_puts_Lan_last_because_she_is_the_only_EV_technician()
    {
        // Rule 2 ties (Nam: Brake + Tyre, Lan: Tyre + EV) and rule 3 would pick Lan (Qualified < Master),
        // but rule 1 protects Lan: she alone can do the EV battery check.
        var order = _strategy.Order(SeedRequest(serviceTypeId: 1, _data.GetTechnician(2), _data.GetTechnician(1)));

        order.Select(p => p.Technician.Name).Should().Equal("Nam", "Lan");
    }

    [Fact]
    public void Tyre_rotation_also_protects_Lan()
    {
        var order = _strategy.Order(SeedRequest(serviceTypeId: 3, _data.GetTechnician(2), _data.GetTechnician(1)));

        order.Select(p => p.Technician.Name).Should().Equal("Nam", "Lan");
    }

    [Fact]
    public void Brake_inspection_still_picks_Minh_first()
    {
        var order = _strategy.Order(SeedRequest(serviceTypeId: 2, _data.GetTechnician(1), _data.GetTechnician(3)));

        order.Select(p => p.Technician.Name).Should().Equal("Minh", "Nam");
    }

    [Fact]
    public void Sole_cover_is_counted_over_all_active_dealership_technicians_not_only_the_candidates()
    {
        // A second EV Senior exists but is not a candidate (busy or off shift): Lan is no longer the only one,
        // so rules 1 and 2 tie and rule 3 (lowest General tier) picks Lan.
        var otherEv = Tech(9, "Viet", (Skill.EV, SkillTier.Senior));
        var request = SeedRequest(serviceTypeId: 1, _data.GetTechnician(1), _data.GetTechnician(2)) with
        {
            DealershipTechnicians = [.. ActiveDistrict1Technicians(), otherEv],
        };

        _strategy.Order(request).Select(p => p.Technician.Name).Should().Equal("Lan", "Nam");
    }

    [Fact]
    public void Inactive_technicians_do_not_count_as_cover()
    {
        // Tuan (EV Master) is inactive, so Lan is still the only EV technician.
        var request = SeedRequest(serviceTypeId: 1, _data.GetTechnician(2), _data.GetTechnician(1)) with
        {
            DealershipTechnicians = _data.Technicians.Where(t => t.DealershipId == 1).ToList(),
        };

        _strategy.Order(request).Select(p => p.Technician.Name).Should().Equal("Nam", "Lan");
    }

    // ---------- Each rule in isolation (custom data) ----------

    [Fact]
    public void Rule_1_sole_cover_beats_every_later_rule()
    {
        // A: sole cover for Brakes, Trainee, 0 appointments. B: no sole cover, Master, 5 appointments, higher Id.
        var a = Tech(1, "A", (Skill.General, SkillTier.Trainee), (Skill.Brakes, SkillTier.Trainee));
        var b = Tech(2, "B", (Skill.General, SkillTier.Master), (Skill.Tyres, SkillTier.Trainee));
        var tyresBackup = Tech(3, "C", (Skill.Tyres, SkillTier.Trainee));

        var order = Order(
            candidates: [a, b],
            dealership: [a, b, tyresBackup],
            services: [General, Brakes, Tyres],
            counts: new Dictionary<int, int> { [2] = 5 });

        order.Should().Equal("B", "A");
    }

    [Fact]
    public void Rule_2_fewer_other_services_beats_tier_count_and_id()
    {
        // Nobody is the only cover. A can do 2 other services, B only 1 (and is Master, busier, higher Id).
        var a = Tech(1, "A", (Skill.General, SkillTier.Trainee), (Skill.Brakes, SkillTier.Trainee), (Skill.Tyres, SkillTier.Trainee));
        var b = Tech(2, "B", (Skill.General, SkillTier.Master), (Skill.Brakes, SkillTier.Trainee));
        var backup = Tech(3, "C", (Skill.Brakes, SkillTier.Trainee), (Skill.Tyres, SkillTier.Trainee));

        var order = Order(
            candidates: [a, b],
            dealership: [a, b, backup],
            services: [General, Brakes, Tyres],
            counts: new Dictionary<int, int> { [2] = 5 });

        order.Should().Equal("B", "A");
    }

    [Fact]
    public void Rule_3_lower_tier_beats_count_and_id_when_rules_1_and_2_tie()
    {
        var senior = Tech(1, "Senior", (Skill.General, SkillTier.Senior));
        var trainee = Tech(2, "Trainee", (Skill.General, SkillTier.Trainee));

        var order = Order([senior, trainee], [senior, trainee], [General], new Dictionary<int, int> { [2] = 3 });

        order.Should().Equal("Trainee", "Senior");
    }

    [Fact]
    public void Rule_4_fewer_confirmed_appointments_beats_id_when_rules_1_to_3_tie()
    {
        var first = Tech(1, "First", (Skill.General, SkillTier.Qualified));
        var second = Tech(2, "Second", (Skill.General, SkillTier.Qualified));

        var order = Order([first, second], [first, second], [General], new Dictionary<int, int> { [1] = 2, [2] = 1 });

        order.Should().Equal("Second", "First");
    }

    [Fact]
    public void Rule_5_id_when_everything_else_ties()
    {
        var first = Tech(1, "First", (Skill.General, SkillTier.Qualified));
        var second = Tech(2, "Second", (Skill.General, SkillTier.Qualified));

        var order = Order([second, first], [first, second], [General], new Dictionary<int, int>());

        order.Should().Equal("First", "Second");
    }

    [Fact]
    public void Inactive_service_types_are_not_in_the_request_so_they_protect_nobody()
    {
        // Without the EV service in the list, Lan has 1 other service (Tyre) and Nam 2 (Brake, Tyre): Lan first.
        var request = SeedRequest(serviceTypeId: 1, _data.GetTechnician(1), _data.GetTechnician(2)) with
        {
            ActiveServiceTypes = _data.ServiceTypes.Where(s => s.DealershipId == 1 && s.Id != 4).ToList(),
        };

        _strategy.Order(request).Select(p => p.Technician.Name).Should().Equal("Lan", "Nam");
    }

    // ---------- Contract (Liskov) ----------

    [Fact]
    public void Returns_every_pair_exactly_once_technician_major_with_bays_by_id()
    {
        var bay2 = _data.ServiceBays.Single(b => b.Id == 2);
        var bay5 = _data.ServiceBays.Single(b => b.Id == 5);
        var lan = _data.GetTechnician(2); // no Brakes skill: unqualified, kept but last
        var request = SeedRequest(serviceTypeId: 2, lan, _data.GetTechnician(1), _data.GetTechnician(3)) with
        {
            ServiceBays = [bay5, bay2],
        };

        var pairs = _strategy.Order(request);

        pairs.Should().HaveCount(3 * 2);
        pairs.Select(p => (p.Technician.Id, p.ServiceBay.Id)).Should().OnlyHaveUniqueItems();
        pairs.Select(p => (p.Technician.Id, p.ServiceBay.Id)).Should().Equal((3, 2), (3, 5), (1, 2), (1, 5), (2, 2), (2, 5));
    }

    [Fact]
    public void Unqualified_technician_goes_last_even_with_no_other_services()
    {
        var qualified = Tech(5, "Qualified", (Skill.General, SkillTier.Master), (Skill.Brakes, SkillTier.Master));
        var unqualified = Tech(1, "Unqualified", (Skill.Tyres, SkillTier.Trainee));

        var order = Order([unqualified, qualified], [qualified, unqualified], [General, Brakes], new Dictionary<int, int>());

        order.Should().Equal("Qualified", "Unqualified");
    }

    [Fact]
    public void Empty_input_gives_no_pairs()
    {
        var request = SeedRequest(serviceTypeId: 1) with { ServiceBays = [_data.ServiceBays[0]] };

        _strategy.Order(request).Should().BeEmpty();
    }

    // ---------- Helpers ----------

    private static readonly ServiceType General = Service(101, Skill.General);
    private static readonly ServiceType Brakes = Service(102, Skill.Brakes);
    private static readonly ServiceType Tyres = Service(103, Skill.Tyres);

    private List<Technician> ActiveDistrict1Technicians() =>
        _data.Technicians.Where(t => t.DealershipId == 1 && t.IsActive).ToList();

    /// <summary>A request on seed data: District 1 active technicians and service types, bay 1, no appointments.</summary>
    private AllocationRequest SeedRequest(int serviceTypeId, params Technician[] candidates) => new(
        _data.GetServiceType(serviceTypeId),
        candidates,
        [_data.ServiceBays[0]],
        new Dictionary<int, int>(),
        ActiveDistrict1Technicians(),
        _data.ServiceTypes.Where(s => s.DealershipId == 1 && s.IsActive).ToList());

    /// <summary>Orders custom data for the <see cref="General"/> service and returns technician names.</summary>
    private IReadOnlyList<string> Order(
        IReadOnlyList<Technician> candidates,
        IReadOnlyList<Technician> dealership,
        IReadOnlyList<ServiceType> services,
        IReadOnlyDictionary<int, int> counts) =>
        _strategy.Order(new AllocationRequest(General, candidates, [_data.ServiceBays[0]], counts, dealership, services))
            .Select(p => p.Technician.Name)
            .ToList();

    private static ServiceType Service(int id, Skill skill) =>
        ServiceType.Create(1, $"{skill} job", 60, skill, SkillTier.Trainee, BayType.Standard).WithId(id);

    private static Technician Tech(int id, string name, params (Skill Skill, SkillTier Tier)[] skills)
    {
        var technician = Technician.Create(1, name, new TimeOnly(8, 0), new TimeOnly(17, 0));
        foreach (var (skill, tier) in skills)
        {
            technician.WithSkill(skill, tier);
        }

        return technician.WithId(id);
    }
}
