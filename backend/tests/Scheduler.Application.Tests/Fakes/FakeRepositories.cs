using Scheduler.Application.Abstractions;
using Scheduler.Application.Common;
using Scheduler.Application.Tests.TestSupport;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Application.Tests.Fakes;

/// <summary>
/// In-memory appointment store that behaves like the database: the unique slot index (SlotTaken),
/// the IdempotencyKeys primary key (IdempotencyKeyTaken) and one transaction per add.
/// Tests can script failures with <see cref="NextAddResults"/> and <see cref="TransientFailuresLeft"/>.
/// </summary>
public sealed class FakeAppointmentRepository : IAppointmentRepository
{
    private readonly FakeData _data;
    private int _nextId = 100;

    public FakeAppointmentRepository(FakeData data)
    {
        _data = data;
    }

    /// <summary>Forced results for the next TryAdd calls (null = behave normally).</summary>
    public Queue<Error?> NextAddResults { get; } = new();

    /// <summary>How many more calls to any method throw <see cref="TransientDatabaseException"/>.</summary>
    public int TransientFailuresLeft { get; set; }

    public List<Appointment> AddAttempts { get; } = [];

    /// <summary>Runs once at the start of the next TryAdd (to simulate a concurrent commit).</summary>
    public Action? BeforeNextAdd { get; set; }

    public int AddCalls => AddAttempts.Count;

    public Task<Result<Appointment>> TryAddWithSlotsAsync(Appointment appointment, IdempotencyEntry? idempotencyEntry, CancellationToken cancellationToken)
    {
        ThrowIfTransient();
        AddAttempts.Add(appointment);
        var before = BeforeNextAdd;
        BeforeNextAdd = null;
        before?.Invoke();

        if (NextAddResults.TryDequeue(out var forced) && forced is not null)
        {
            return Task.FromResult(Result.Failure<Appointment>(forced));
        }

        var taken = _data.Appointments
            .SelectMany(a => a.ResourceSlots)
            .Select(s => (s.ResourceType, s.ResourceId, s.SlotStartUtc))
            .ToHashSet();
        if (appointment.ResourceSlots.Any(s => taken.Contains((s.ResourceType, s.ResourceId, s.SlotStartUtc))))
        {
            return Task.FromResult(Result.Failure<Appointment>(RepositoryErrors.SlotTaken));
        }

        if (idempotencyEntry is not null && _data.IdempotencyKeys.ContainsKey(idempotencyEntry.Key))
        {
            return Task.FromResult(Result.Failure<Appointment>(RepositoryErrors.IdempotencyKeyTaken));
        }

        appointment.WithId(_nextId++);
        _data.Appointments.Add(appointment);
        if (idempotencyEntry is not null)
        {
            _data.IdempotencyKeys[idempotencyEntry.Key] = new IdempotencyRecord(
                idempotencyEntry.Key,
                idempotencyEntry.RequestHash,
                appointment.Id,
                idempotencyEntry.BuildResponseJson(appointment),
                idempotencyEntry.CreatedUtc);
        }

        return Task.FromResult(Result.Success(appointment));
    }

    public Task<Appointment?> GetByIdWithDetailsAsync(int appointmentId, CancellationToken cancellationToken)
    {
        ThrowIfTransient();
        var appointment = _data.Appointments.SingleOrDefault(a => a.Id == appointmentId);
        return Task.FromResult(appointment is null ? null : _data.WithDetails(appointment));
    }

    public Task<IReadOnlyList<Appointment>> GetByCustomerWithDetailsAsync(int customerId, CancellationToken cancellationToken)
    {
        ThrowIfTransient();
        IReadOnlyList<Appointment> list = _data.Appointments.Where(a => a.CustomerId == customerId).Select(_data.WithDetails).ToList();
        return Task.FromResult(list);
    }

    public Task<Appointment?> GetForCancelAsync(int appointmentId, CancellationToken cancellationToken) =>
        GetByIdWithDetailsAsync(appointmentId, cancellationToken);

    public Task<IReadOnlyList<TakenSlot>> GetTakenSlotsAsync(
        IReadOnlyCollection<int> technicianIds,
        IReadOnlyCollection<int> serviceBayIds,
        TimeRange windowUtc,
        CancellationToken cancellationToken)
    {
        ThrowIfTransient();
        IReadOnlyList<TakenSlot> slots = _data.Appointments
            .SelectMany(a => a.ResourceSlots)
            .Where(s => (s.ResourceType == ResourceType.Technician && technicianIds.Contains(s.ResourceId))
                || (s.ResourceType == ResourceType.Bay && serviceBayIds.Contains(s.ResourceId)))
            .Where(s => windowUtc.Contains(s.SlotStartUtc))
            .Select(s => new TakenSlot(s.ResourceType, s.ResourceId, s.SlotStartUtc))
            .ToList();
        return Task.FromResult(slots);
    }

    public Task<bool> IsVehicleBookedAsync(int vehicleId, TimeRange rangeUtc, CancellationToken cancellationToken)
    {
        ThrowIfTransient();
        return Task.FromResult(_data.Appointments
            .SelectMany(a => a.ResourceSlots)
            .Any(s => s.ResourceType == ResourceType.Vehicle && s.ResourceId == vehicleId && rangeUtc.Contains(s.SlotStartUtc)));
    }

    public Task<IReadOnlyList<DateTime>> GetVehicleSlotStartsAsync(int vehicleId, TimeRange windowUtc, CancellationToken cancellationToken)
    {
        ThrowIfTransient();
        IReadOnlyList<DateTime> starts = _data.Appointments
            .SelectMany(a => a.ResourceSlots)
            .Where(s => s.ResourceType == ResourceType.Vehicle && s.ResourceId == vehicleId && windowUtc.Contains(s.SlotStartUtc))
            .Select(s => s.SlotStartUtc)
            .Order()
            .ToList();
        return Task.FromResult(starts);
    }

    public Task<IReadOnlyDictionary<int, int>> CountConfirmedByTechnicianAsync(
        IReadOnlyCollection<int> technicianIds,
        TimeRange windowUtc,
        CancellationToken cancellationToken)
    {
        ThrowIfTransient();
        IReadOnlyDictionary<int, int> counts = _data.Appointments
            .Where(a => a.Status == AppointmentStatus.Confirmed
                && technicianIds.Contains(a.TechnicianId)
                && windowUtc.Contains(a.StartUtc))
            .GroupBy(a => a.TechnicianId)
            .ToDictionary(g => g.Key, g => g.Count());
        return Task.FromResult(counts);
    }

    private void ThrowIfTransient()
    {
        if (TransientFailuresLeft > 0)
        {
            TransientFailuresLeft--;
            throw new TransientDatabaseException("Simulated deadlock.");
        }
    }
}

public sealed class FakeIdempotencyStore : IIdempotencyStore
{
    private readonly FakeData _data;

    public FakeIdempotencyStore(FakeData data)
    {
        _data = data;
    }

    public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_data.IdempotencyKeys.GetValueOrDefault(key));
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCalls { get; private set; }

    /// <summary>Thrown by the next save, then cleared.</summary>
    public Exception? NextSaveException { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCalls++;
        if (NextSaveException is { } exception)
        {
            NextSaveException = null;
            throw exception;
        }

        return Task.CompletedTask;
    }
}

public sealed class FakeTechnicianRepository : ITechnicianRepository
{
    private readonly FakeData _data;

    public FakeTechnicianRepository(FakeData data)
    {
        _data = data;
    }

    public Task<IReadOnlyList<Technician>> GetByDealershipWithSkillsAsync(int dealershipId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Technician>>(_data.Technicians.Where(t => t.DealershipId == dealershipId).ToList());
}

public sealed class FakeServiceBayRepository : IServiceBayRepository
{
    private readonly FakeData _data;

    public FakeServiceBayRepository(FakeData data)
    {
        _data = data;
    }

    public Task<IReadOnlyList<ServiceBay>> GetByDealershipAsync(int dealershipId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ServiceBay>>(_data.ServiceBays.Where(b => b.DealershipId == dealershipId).ToList());
}

public sealed class FakeCatalog : IDealershipRepository, IServiceTypeRepository, IVehicleRepository, ICustomerRepository
{
    private readonly FakeData _data;

    public FakeCatalog(FakeData data)
    {
        _data = data;
    }

    Task<Dealership?> IDealershipRepository.GetByIdAsync(int dealershipId, CancellationToken cancellationToken) =>
        Task.FromResult(_data.Dealerships.SingleOrDefault(d => d.Id == dealershipId));

    public Task<IReadOnlyList<Dealership>> GetActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Dealership>>(_data.Dealerships.Where(d => d.IsActive).ToList());

    Task<ServiceType?> IServiceTypeRepository.GetByIdAsync(int serviceTypeId, CancellationToken cancellationToken) =>
        Task.FromResult(_data.ServiceTypes.SingleOrDefault(s => s.Id == serviceTypeId));

    public Task<IReadOnlyList<ServiceType>> GetActiveAsync(int? dealershipId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ServiceType>>(_data.ServiceTypes
            .Where(s => s.IsActive && (dealershipId is null || s.DealershipId == dealershipId))
            .ToList());

    Task<Vehicle?> IVehicleRepository.GetByIdAsync(int vehicleId, CancellationToken cancellationToken) =>
        Task.FromResult(_data.Vehicles.SingleOrDefault(v => v.Id == vehicleId));

    public Task<IReadOnlyList<Vehicle>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Vehicle>>(_data.Vehicles.Where(v => v.CustomerId == customerId).ToList());

    public Task<bool> ExistsAsync(int customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_data.Customers.Any(c => c.Id == customerId));

    public Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Customer>>(_data.Customers.OrderBy(c => c.Id).ToList());
}
