using Scheduler.Domain.Entities;

namespace Scheduler.Application.Abstractions;

/// <summary>
/// An Idempotency-Key row to save in the same transaction as a new appointment.
/// The response body needs the appointment Id, which exists only after the insert, so the repository calls
/// <see cref="BuildResponseJson"/> after the appointment has its Id and before the commit.
/// </summary>
public sealed class IdempotencyEntry
{
    private readonly Func<Appointment, string> _responseJsonFactory;

    public IdempotencyEntry(string key, string requestHash, DateTime createdUtc, Func<Appointment, string> responseJsonFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentNullException.ThrowIfNull(responseJsonFactory);
        Key = key;
        RequestHash = requestHash;
        CreatedUtc = createdUtc;
        _responseJsonFactory = responseJsonFactory;
    }

    /// <summary>The Idempotency-Key header value (max 100 characters).</summary>
    public string Key { get; }

    /// <summary>SHA-256 of the canonical request, lower-case hex (64 characters).</summary>
    public string RequestHash { get; }

    public DateTime CreatedUtc { get; }

    /// <summary>The JSON body to store, built from the saved appointment (which has its Id).</summary>
    public string BuildResponseJson(Appointment savedAppointment) => _responseJsonFactory(savedAppointment);
}
