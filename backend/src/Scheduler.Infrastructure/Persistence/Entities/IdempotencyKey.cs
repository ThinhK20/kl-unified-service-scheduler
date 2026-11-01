using Scheduler.Domain.Entities;

namespace Scheduler.Infrastructure.Persistence.Entities;

/// <summary>
/// A saved booking request, keyed by the client's <c>Idempotency-Key</c> header.
/// It is saved in the same transaction as the appointment it created, so a retry of the same request
/// returns the saved response instead of booking again. This is a persistence record, not a domain entity.
/// </summary>
public sealed class IdempotencyKey
{
    public const int KeyMaxLength = 100;

    public const int RequestHashMaxLength = 64;

    private IdempotencyKey()
    {
    }

    /// <summary>Creates a record for an appointment that already has a database id.</summary>
    public IdempotencyKey(string key, string requestHash, int appointmentId, string responseJson, DateTime createdUtc)
    {
        Key = key;
        RequestHash = requestHash;
        AppointmentId = appointmentId;
        ResponseJson = responseJson;
        CreatedUtc = createdUtc;
    }

    /// <summary>
    /// Creates a record linked to an appointment object. Use this when the appointment is saved in the same
    /// <c>SaveChanges</c> call and has no id yet; EF fills <see cref="AppointmentId"/> on save.
    /// </summary>
    public IdempotencyKey(string key, string requestHash, Appointment appointment, string responseJson, DateTime createdUtc)
        : this(key, requestHash, appointment?.Id ?? throw new ArgumentNullException(nameof(appointment)), responseJson, createdUtc)
    {
        Appointment = appointment;
    }

    /// <summary>The client key (primary key, max 100 characters).</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>SHA-256 of the canonical request body (hex, 64 characters).</summary>
    public string RequestHash { get; private set; } = string.Empty;

    public int AppointmentId { get; private set; }

    /// <summary>The response body returned the first time (JSON).</summary>
    public string ResponseJson { get; private set; } = string.Empty;

    public DateTime CreatedUtc { get; private set; }

    public Appointment? Appointment { get; private set; }
}
