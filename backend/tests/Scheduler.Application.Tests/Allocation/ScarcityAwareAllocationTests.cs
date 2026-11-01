using Scheduler.Application.Tests.TestSupport;

namespace Scheduler.Application.Tests.Allocation;

/// <summary>The default strategy through the real booking handler, on the seed data (District 1).</summary>
public class ScarcityAwareAllocationTests
{
    private const int OilChange = 1;
    private const int TyreRotation = 3;
    private const int EvBatteryCheck = 4;

    private readonly TestApp _app = new();

    [Fact]
    public async Task Oil_change_at_10_goes_to_Nam_so_Lan_stays_free_for_an_EV_check_at_10()
    {
        // Minh starts at 12:00. Lan is the only active EV Senior, so the oil change goes to Nam.
        var oil = await _app.BookHandler().HandleAsync(TestApp.Command(OilChange, Local.Iso(10), "oil"), CancellationToken.None);
        oil.IsSuccess.Should().BeTrue(oil.Error?.Message);
        oil.Value.Appointment.Technician.Name.Should().Be("Nam");

        // Another car, same time: Lan is still free.
        var ev = await _app.BookHandler().HandleAsync(
            TestApp.Command(EvBatteryCheck, Local.Iso(10), "ev", vehicleId: 2), CancellationToken.None);
        ev.IsSuccess.Should().BeTrue(ev.Error?.Message);
        ev.Value.Appointment.Technician.Name.Should().Be("Lan");
        ev.Value.Appointment.ServiceBay.Name.Should().Be("Bay 3");
    }

    [Fact]
    public async Task Tyre_rotation_at_10_goes_to_Nam_not_Lan()
    {
        var result = await _app.BookHandler().HandleAsync(TestApp.Command(TyreRotation, Local.Iso(10)), CancellationToken.None);

        result.Value.Appointment.Technician.Name.Should().Be("Nam");
    }

    [Fact]
    public async Task When_Nam_is_busy_the_oil_change_still_goes_to_Lan()
    {
        // Protecting a specialist never refuses a booking: Lan is tried after Nam.
        await _app.BookAsync(TyreRotation, Local.Iso(10), "tyre"); // Nam, 10:00-10:30

        var oil = await _app.BookHandler().HandleAsync(
            TestApp.Command(OilChange, Local.Iso(10), "oil", vehicleId: 2), CancellationToken.None);

        oil.IsSuccess.Should().BeTrue(oil.Error?.Message);
        oil.Value.Appointment.Technician.Name.Should().Be("Lan");
    }
}
