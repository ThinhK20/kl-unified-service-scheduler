using Scheduler.Application.Common;

namespace Scheduler.Application.Abstractions;

/// <summary>
/// Errors a repository returns to the booking handler. They are internal: the handler reacts to them
/// and they never reach the API.
/// </summary>
public static class RepositoryErrors
{
    public const string SlotTakenCode = "SLOT_TAKEN";
    public const string IdempotencyKeyTakenCode = "IDEMPOTENCY_KEY_TAKEN";

    /// <summary>Another booking took one of the slots first (unique index violation).</summary>
    public static readonly Error SlotTaken =
        new(SlotTakenCode, "One of the technician or bay slots was taken by another booking.");

    /// <summary>Another request saved the same Idempotency-Key first (primary key violation).</summary>
    public static readonly Error IdempotencyKeyTaken =
        new(IdempotencyKeyTakenCode, "Another request with the same Idempotency-Key was saved first.");
}
