using Scheduler.Application.Allocation;
using Scheduler.Application.Tests.Fakes;

namespace Scheduler.Application.Tests.Allocation;

public class LeastQualifiedFirstStrategyTests
{
    private readonly FakeData _data = new();
    private readonly LeastQualifiedFirstStrategy _strategy = new();

    [Fact]
    public void Lowest_tier_comes_first()
    {
        // Brake inspection: Minh (Qualified), Nam (Senior), Tuan (Master).
        var request = new AllocationRequest(
            _data.GetServiceType(2),
            [_data.GetTechnician(5), _data.GetTechnician(1), _data.GetTechnician(3)],
            [_data.ServiceBays[1]],
            new Dictionary<int, int>());

        _strategy.Order(request).Select(p => p.Technician.Name).Should().Equal("Minh", "Nam", "Tuan");
    }

    [Fact]
    public void Same_tier_then_fewest_confirmed_appointments_then_id()
    {
        // Oil change (General): Nam Master, Tuan Master, Lan Qualified.
        var request = new AllocationRequest(
            _data.GetServiceType(1),
            [_data.GetTechnician(1), _data.GetTechnician(5), _data.GetTechnician(2)],
            [_data.ServiceBays[0]],
            new Dictionary<int, int> { [1] = 2, [5] = 1 });

        _strategy.Order(request).Select(p => p.Technician.Name).Should().Equal("Lan", "Tuan", "Nam");
    }

    [Fact]
    public void Equal_tier_and_count_falls_back_to_id()
    {
        var request = new AllocationRequest(
            _data.GetServiceType(1),
            [_data.GetTechnician(5), _data.GetTechnician(1)],
            [_data.ServiceBays[0]],
            new Dictionary<int, int>());

        _strategy.Order(request).Select(p => p.Technician.Id).Should().Equal(1, 5);
    }

    [Fact]
    public void Returns_every_pair_exactly_once_technician_major_with_bays_by_id()
    {
        var bay2 = _data.ServiceBays.Single(b => b.Id == 2);
        var bay5 = _data.ServiceBays.Single(b => b.Id == 5);
        var request = new AllocationRequest(
            _data.GetServiceType(2),
            [_data.GetTechnician(1), _data.GetTechnician(3)],
            [bay5, bay2],
            new Dictionary<int, int>());

        var pairs = _strategy.Order(request);

        pairs.Should().HaveCount(4);
        pairs.Select(p => (p.Technician.Id, p.ServiceBay.Id)).Should().Equal((3, 2), (3, 5), (1, 2), (1, 5));
    }

    [Fact]
    public void Empty_input_gives_no_pairs()
    {
        var request = new AllocationRequest(_data.GetServiceType(1), [], [_data.ServiceBays[0]], new Dictionary<int, int>());

        _strategy.Order(request).Should().BeEmpty();
    }
}
