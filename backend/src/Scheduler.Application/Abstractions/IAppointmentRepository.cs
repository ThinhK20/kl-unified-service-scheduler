using Scheduler.Application.Common;
using Scheduler.Domain.Entities;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Application.Abstractions;

/// <summary>Stores and reads appointments and their slot rows.</summary>
public interface IAppointmentRepository
{
    /// <summary>
    /// Saves the appointment, all its <see cref="Appointment.ResourceSlots"/> and (when given) the idempotency entry
    /// in ONE transaction. On success the returned appointment has its database Id.
    /// <para>Failures (the transaction is rolled back and the change tracker cleared, so the next call starts clean):</para>
    /// <list type="bullet">
    /// <item><see cref="RepositoryErrors.SlotTaken"/>: SQL error 2601/2627 on the ResourceSlots unique index.</item>
    /// <item><see cref="RepositoryErrors.IdempotencyKeyTaken"/>: duplicate key on the IdempotencyKeys primary key.</item>
    /// </list>
    /// Throws <see cref="TransientDatabaseException"/> for deadlocks (1205) and timeouts.
    /// </summary>
    Task<Result<Appointment>> TryAddWithSlotsAsync(
        Appointment appointment,
        IdempotencyEntry? idempotencyEntry,
        CancellationToken cancellationToken);

    /// <summary>
    /// One appointment with Vehicle, Dealership, ServiceType, Technician and ServiceBay loaded (read only), or null.
    /// </summary>
    Task<Appointment?> GetByIdWithDetailsAsync(int appointmentId, CancellationToken cancellationToken);

    /// <summary>All appointments of a customer (any status) with the same details loaded as <see cref="GetByIdWithDetailsAsync"/>.</summary>
    Task<IReadOnlyList<Appointment>> GetByCustomerWithDetailsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>
    /// One appointment, TRACKED, with its ResourceSlots and the same details loaded, ready for
    /// <see cref="Appointment.Cancel"/> followed by <see cref="IUnitOfWork.SaveChangesAsync"/>. Null if not found.
    /// </summary>
    Task<Appointment?> GetForCancelAsync(int appointmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Slot rows that are taken in <paramref name="windowUtc"/> (SlotStartUtc &gt;= start and &lt; end) for the given
    /// technicians (ResourceType.Technician) and service bays (ResourceType.Bay).
    /// </summary>
    Task<IReadOnlyList<TakenSlot>> GetTakenSlotsAsync(
        IReadOnlyCollection<int> technicianIds,
        IReadOnlyCollection<int> serviceBayIds,
        TimeRange windowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// For each technician, how many CONFIRMED appointments start in <paramref name="windowUtc"/>.
    /// Technicians with none may be left out of the dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<int, int>> CountConfirmedByTechnicianAsync(
        IReadOnlyCollection<int> technicianIds,
        TimeRange windowUtc,
        CancellationToken cancellationToken);
}
