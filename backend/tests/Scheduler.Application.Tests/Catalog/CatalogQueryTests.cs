using Scheduler.Application.Catalog.Queries;
using Scheduler.Application.Common;
using Scheduler.Application.Tests.TestSupport;

namespace Scheduler.Application.Tests.Catalog;

public class CatalogQueryTests
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task Dealerships_have_local_open_and_close_times_as_HH_mm()
    {
        var result = await _app.DealershipsHandler().HandleAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result[1].Should().Be(new Scheduler.Application.Catalog.Dtos.DealershipDto(2, "Thu Duc Service Centre", "Asia/Ho_Chi_Minh", "07:30", "18:00"));
    }

    [Fact]
    public async Task Service_types_are_active_only_and_can_be_filtered_by_dealership()
    {
        _app.Data.GetServiceType(3).Deactivate();

        var all = await _app.ServiceTypesHandler().HandleAsync(new GetServiceTypesQuery(null), CancellationToken.None);
        var district1 = await _app.ServiceTypesHandler().HandleAsync(new GetServiceTypesQuery(1), CancellationToken.None);

        all.Select(s => s.Id).Should().Equal(1, 2, 4, 5);
        district1.Select(s => s.Id).Should().Equal(1, 2, 4);
        district1[1].Should().Be(new Scheduler.Application.Catalog.Dtos.ServiceTypeDto(2, 1, "Brake inspection", 90, "Brakes", "Qualified", "Lift"));
    }

    [Fact]
    public async Task Customers_are_the_three_demo_customers_ordered_by_id()
    {
        var result = await _app.CustomersHandler().HandleAsync(CancellationToken.None);

        result.Should().Equal(
            new Scheduler.Application.Catalog.Dtos.CustomerDto(1, "An Nguyen", "an.nguyen@example.com", "+84 900 000 001"),
            new Scheduler.Application.Catalog.Dtos.CustomerDto(2, "Binh Tran", "binh.tran@example.com", "+84 900 000 002"),
            new Scheduler.Application.Catalog.Dtos.CustomerDto(3, "Chi Le", "chi.le@example.com", "+84 900 000 003"));
    }

    [Fact]
    public async Task Vehicles_of_the_third_customer()
    {
        var result = await _app.VehiclesHandler().HandleAsync(new GetCustomerVehiclesQuery(3), CancellationToken.None);

        result.Value.Select(v => (v.Id, v.Make, v.Model)).Should().Equal((4, "Mazda", "CX-5"), (5, "Kia", "Morning"));
    }

    [Fact]
    public async Task Vehicles_of_a_customer()
    {
        var result = await _app.VehiclesHandler().HandleAsync(new GetCustomerVehiclesQuery(1), CancellationToken.None);

        result.Value.Select(v => v.Model).Should().Equal("Vios", "CR-V");
    }

    [Fact]
    public async Task Vehicles_of_an_unknown_customer_is_not_found()
    {
        var result = await _app.VehiclesHandler().HandleAsync(new GetCustomerVehiclesQuery(77), CancellationToken.None);

        result.Error!.Code.Should().Be(ErrorCodes.NotFound);
    }
}
