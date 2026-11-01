using Scheduler.Application.Abstractions;
using Scheduler.Application.Allocation;
using Scheduler.Application.Appointments.Book;
using Scheduler.Application.Appointments.Cancel;
using Scheduler.Application.Appointments.Queries;
using Scheduler.Application.Catalog.Queries;
using Scheduler.Application.Tests.Fakes;

namespace Scheduler.Application.Tests.TestSupport;

/// <summary>Wires real handlers to fakes. "Now" is Monday 9 Nov 2026 07:00 UTC unless a test changes it.</summary>
public sealed class TestApp
{
    public TestApp()
    {
        Data = new FakeData();
        Clock = new FakeClock(new DateTime(2026, 11, 9, 7, 0, 0, DateTimeKind.Utc));
        Appointments = new FakeAppointmentRepository(Data);
        Idempotency = new FakeIdempotencyStore(Data);
        UnitOfWork = new FakeUnitOfWork();
        Catalog = new FakeCatalog(Data);
        Availability = new AvailabilityService(
            new FakeTechnicianRepository(Data),
            new FakeServiceBayRepository(Data),
            Appointments,
            new LeastQualifiedFirstStrategy());
    }

    public FakeData Data { get; }

    public FakeClock Clock { get; }

    public FakeAppointmentRepository Appointments { get; }

    public FakeIdempotencyStore Idempotency { get; }

    public FakeUnitOfWork UnitOfWork { get; }

    public FakeCatalog Catalog { get; }

    public AvailabilityService Availability { get; }

    public BookAppointmentHandler BookHandler(IIdempotencyStore? idempotency = null) => new(
        new BookAppointmentValidator(Clock), Catalog, Catalog, Catalog, Catalog, Appointments, idempotency ?? Idempotency, Availability, Clock);

    public CancelAppointmentHandler CancelHandler() => new(Appointments, UnitOfWork);

    public GetAvailabilityHandler AvailabilityHandler() => new(new GetAvailabilityValidator(), Catalog, Catalog, Availability, Clock);

    public GetAppointmentsHandler AppointmentsHandler() => new(Appointments);

    public GetAppointmentByIdHandler AppointmentByIdHandler() => new(Appointments);

    public GetDealershipsHandler DealershipsHandler() => new(Catalog);

    public GetServiceTypesHandler ServiceTypesHandler() => new(Catalog);

    public GetCustomerVehiclesHandler VehiclesHandler() => new(Catalog, Catalog);

    /// <summary>A booking command for customer 1, vehicle 1, dealership 1.</summary>
    public static BookAppointmentCommand Command(int serviceTypeId, string startTime, string key = "key-1", int customerId = 1, int vehicleId = 1, int dealershipId = 1) =>
        new(key, customerId, vehicleId, dealershipId, serviceTypeId, startTime);

    /// <summary>Books through the real handler and returns the result (fails the test on error).</summary>
    public async Task<BookAppointmentResult> BookAsync(int serviceTypeId, string startTime, string key)
    {
        var result = await BookHandler().HandleAsync(Command(serviceTypeId, startTime, key), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }
}
