using System.Text.Json;
using Scheduler.Application.Appointments.Dtos;

namespace Scheduler.Application.Appointments.Book;

/// <summary>Writes and reads the response body saved with an Idempotency-Key.</summary>
public static class StoredResponse
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(AppointmentDto appointment) => JsonSerializer.Serialize(appointment, Options);

    /// <summary>The saved DTO, or null when the JSON is empty or cannot be read.</summary>
    public static AppointmentDto? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AppointmentDto>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
