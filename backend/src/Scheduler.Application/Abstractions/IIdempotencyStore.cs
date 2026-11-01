namespace Scheduler.Application.Abstractions;

/// <summary>
/// Reads saved Idempotency-Key rows. Saving happens inside
/// <see cref="IAppointmentRepository.TryAddWithSlotsAsync"/>, in the same transaction as the appointment.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>The saved row for the key, or null. Must read committed data (no cached/tracked copy).</summary>
    Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken);
}

/// <summary>A saved Idempotency-Key row.</summary>
public sealed record IdempotencyRecord(
    string Key,
    string RequestHash,
    int AppointmentId,
    string ResponseJson,
    DateTime CreatedUtc);
