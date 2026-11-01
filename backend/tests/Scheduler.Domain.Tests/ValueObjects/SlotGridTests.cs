using Scheduler.Domain.Errors;
using Scheduler.Domain.Tests.TestSupport;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Domain.Tests.ValueObjects;

public class SlotGridTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(45)]
    public void Quarter_hours_are_on_grid(int minute)
    {
        SlotGrid.IsOnGrid(Utc.At(9, minute)).Should().BeTrue();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(1)]
    [InlineData(59)]
    public void Other_minutes_are_not_on_grid(int minute)
    {
        SlotGrid.IsOnGrid(Utc.At(9, minute)).Should().BeFalse();
    }

    [Fact]
    public void Non_zero_seconds_are_not_on_grid()
    {
        SlotGrid.IsOnGrid(Utc.At(9, 15, 1)).Should().BeFalse();
    }

    [Fact]
    public void Non_zero_milliseconds_are_not_on_grid()
    {
        SlotGrid.IsOnGrid(Utc.At(9, 15).AddMilliseconds(1)).Should().BeFalse();
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(14, 15)]
    [InlineData(15, 15)]
    [InlineData(16, 30)]
    [InlineData(50, 60)]
    [InlineData(60, 60)]
    [InlineData(61, 75)]
    [InlineData(90, 90)]
    public void RoundUpToGrid_rounds_up_to_next_15(int minutes, int expected)
    {
        SlotGrid.RoundUpToGrid(minutes).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    public void RoundUpToGrid_rejects_zero_or_negative(int minutes)
    {
        var act = () => SlotGrid.RoundUpToGrid(minutes);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Split_60_minutes_gives_4_slots()
    {
        var range = TimeRange.Create(Utc.At(3, 0), Utc.At(4, 0));

        SlotGrid.Split(range).Should().Equal(Utc.At(3, 0), Utc.At(3, 15), Utc.At(3, 30), Utc.At(3, 45));
    }

    [Fact]
    public void Split_90_minutes_gives_6_slots()
    {
        var range = TimeRange.Create(Utc.At(3, 30), Utc.At(5, 0));

        var slots = SlotGrid.Split(range);

        slots.Should().HaveCount(6);
        slots[0].Should().Be(Utc.At(3, 30));
        slots[^1].Should().Be(Utc.At(4, 45));
    }

    [Fact]
    public void Split_rejects_start_off_grid()
    {
        var range = TimeRange.Create(Utc.At(3, 10), Utc.At(4, 0));

        var act = () => SlotGrid.Split(range);

        act.Should().Throw<DomainException>().Which.Error.Should().Be(DomainErrors.Time.NotOnGrid);
    }

    [Fact]
    public void Split_rejects_end_off_grid()
    {
        var range = TimeRange.Create(Utc.At(3, 0), Utc.At(3, 50));

        var act = () => SlotGrid.Split(range);

        act.Should().Throw<DomainException>().Which.Error.Should().Be(DomainErrors.Time.NotOnGrid);
    }
}
