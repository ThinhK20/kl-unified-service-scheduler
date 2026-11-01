using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Abstractions;
using Scheduler.Application.Common;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Domain.ValueObjects;
using Scheduler.Infrastructure.Persistence;
using Scheduler.Infrastructure.Persistence.Entities;

namespace Scheduler.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IAppointmentRepository"/>.</summary>
internal sealed class AppointmentRepository(SchedulerDbContext db) : IAppointmentRepository
{
    public async Task<Result<Appointment>> TryAddWithSlotsAsync(
        Appointment appointment,
        IdempotencyEntry? idempotencyEntry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // 1. Appointment + all its slot rows. The unique slot index decides who wins.
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync(cancellationToken);

            // 2. The Idempotency-Key row; its response body needs the new appointment Id.
            if (idempotencyEntry is not null)
            {
                db.IdempotencyKeys.Add(new IdempotencyKey(
                    idempotencyEntry.Key,
                    idempotencyEntry.RequestHash,
                    appointment.Id,
                    idempotencyEntry.BuildResponseJson(appointment),
                    idempotencyEntry.CreatedUtc));
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return appointment;
        }
        catch (Exception ex)
        {
            await RollBackQuietlyAsync(transaction);

            // Start clean: the next pair is tried on the same context.
            db.ChangeTracker.Clear();

            switch (SqlErrorClassifier.Classify(ex))
            {
                case SqlErrorKind.SlotTaken:
                    return RepositoryErrors.SlotTaken;
                case SqlErrorKind.IdempotencyKeyTaken:
                    return RepositoryErrors.IdempotencyKeyTaken;
                case SqlErrorKind.Transient:
                    throw new TransientDatabaseException("The database was busy while saving the appointment.", ex);
                default:
                    throw;
            }
        }
    }

    public Task<Appointment?> GetByIdWithDetailsAsync(int appointmentId, CancellationToken cancellationToken) =>
        WithDetails(db.Appointments.AsNoTracking())
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

    public async Task<IReadOnlyList<Appointment>> GetByCustomerWithDetailsAsync(int customerId, CancellationToken cancellationToken) =>
        await WithDetails(db.Appointments.AsNoTracking())
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.StartUtc)
            .ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);

    public Task<Appointment?> GetForCancelAsync(int appointmentId, CancellationToken cancellationToken) =>
        WithDetails(db.Appointments)
            .Include(a => a.ResourceSlots)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

    public async Task<IReadOnlyList<TakenSlot>> GetTakenSlotsAsync(
        IReadOnlyCollection<int> technicianIds,
        IReadOnlyCollection<int> serviceBayIds,
        TimeRange windowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(technicianIds);
        ArgumentNullException.ThrowIfNull(serviceBayIds);
        ArgumentNullException.ThrowIfNull(windowUtc);
        if (technicianIds.Count == 0 && serviceBayIds.Count == 0)
        {
            return [];
        }

        var techIds = technicianIds.Distinct().ToArray();
        var bayIds = serviceBayIds.Distinct().ToArray();
        var start = windowUtc.StartUtc;
        var end = windowUtc.EndUtc;

        return await db.ResourceSlots
            .AsNoTracking()
            .Where(s => s.SlotStartUtc >= start && s.SlotStartUtc < end)
            .Where(s => (s.ResourceType == ResourceType.Technician && techIds.Contains(s.ResourceId))
                     || (s.ResourceType == ResourceType.Bay && bayIds.Contains(s.ResourceId)))
            .OrderBy(s => s.SlotStartUtc)
            .Select(s => new TakenSlot(s.ResourceType, s.ResourceId, s.SlotStartUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, int>> CountConfirmedByTechnicianAsync(
        IReadOnlyCollection<int> technicianIds,
        TimeRange windowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(technicianIds);
        ArgumentNullException.ThrowIfNull(windowUtc);
        if (technicianIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        var ids = technicianIds.Distinct().ToArray();
        var start = windowUtc.StartUtc;
        var end = windowUtc.EndUtc;

        return await db.Appointments
            .AsNoTracking()
            .Where(a => a.Status == AppointmentStatus.Confirmed
                     && a.StartUtc >= start && a.StartUtc < end
                     && ids.Contains(a.TechnicianId))
            .GroupBy(a => a.TechnicianId)
            .Select(g => new { TechnicianId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TechnicianId, x => x.Count, cancellationToken);
    }

    private static IQueryable<Appointment> WithDetails(IQueryable<Appointment> query) =>
        query
            .Include(a => a.Vehicle)
            .Include(a => a.Dealership)
            .Include(a => a.ServiceType)
            .Include(a => a.Technician)
            .Include(a => a.ServiceBay);

    private static async Task RollBackQuietlyAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            // Not cancellable: the rollback must run even when the request was cancelled.
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // SQL Server may already have rolled back (for example a deadlock victim); disposing cleans up.
        }
    }
}
