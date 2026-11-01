using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scheduler.Application.Appointments.Dtos;

namespace Scheduler.IntegrationTests.Api;

/// <summary>Small typed helpers over HttpClient for the API tests.</summary>
public static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>"2030-06-03T13:00:00+07:00" for a local time in Asia/Ho_Chi_Minh.</summary>
    public static string LocalIso(DateOnly date, int hour, int minute = 0) =>
        string.Create(CultureInfo.InvariantCulture, $"{date:yyyy-MM-dd}T{hour:00}:{minute:00}:00+07:00");

    /// <summary>The UTC instant of a Ho Chi Minh local time.</summary>
    public static DateTime LocalToUtc(DateOnly date, int hour, int minute = 0) =>
        DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(hour, minute)).AddHours(-7), DateTimeKind.Utc);

    /// <summary>The same instant formatted as the API formats UTC times.</summary>
    public static string UtcIso(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static Task<HttpResponseMessage> BookAsync(
        this HttpClient client,
        int serviceTypeId,
        string startTime,
        string? idempotencyKey = null,
        int customerId = 1,
        int vehicleId = 1,
        int dealershipId = 1)
    {
        var body = new { customerId, vehicleId, dealershipId, serviceTypeId, startTime };
        return client.PostWithKeyAsync("/api/appointments", body, idempotencyKey ?? Guid.NewGuid().ToString());
    }

    public static Task<HttpResponseMessage> PostWithKeyAsync(this HttpClient client, string url, object body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request);
    }

    public static async Task<AppointmentDto> BookOkAsync(this HttpClient client, int serviceTypeId, string startTime, string? key = null)
    {
        using var response = await client.BookAsync(serviceTypeId, startTime, key);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created, text);
        return JsonSerializer.Deserialize<AppointmentDto>(text, Json)!;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    /// <summary>Reads a ProblemDetails body and checks the content type.</summary>
    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().NotBeNullOrEmpty();
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        problem.GetProperty("status").GetInt32().Should().Be((int)response.StatusCode);
        return problem;
    }

    public static string Code(this JsonElement problem) => problem.GetProperty("code").GetString()!;
}
