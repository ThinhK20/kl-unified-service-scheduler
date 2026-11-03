using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scheduler.Api.Middleware;
using Scheduler.Api.Observability;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.IntegrationTests.Infrastructure;
using static Scheduler.IntegrationTests.Api.ApiClient;

namespace Scheduler.IntegrationTests.Api;

/// <summary>SPEC section 10: correlation id, booking metrics, the booking span and safe log lines, through the real API.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ObservabilityApiTests(SqlServerFixture fixture)
{
    private const int OilChange = 1;
    private const string Vin = "JTDBT923X71000001"; // vehicle 1
    private readonly SchedulerApiFactory _factory = SchedulerApiFactory.For(fixture);
    private readonly HttpClient _client = SchedulerApiFactory.For(fixture).CreateClient();

    private static HttpRequestMessage Get(string url, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, correlationId);
        }

        return request;
    }

    private static HttpRequestMessage Book(DateOnly day, int hour, string key, int vehicleId = 1, string? correlationId = null, string? traceParent = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/appointments")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { customerId = 1, vehicleId, dealershipId = 1, serviceTypeId = OilChange, startTime = LocalIso(day, hour) }),
        };
        request.Headers.Add("Idempotency-Key", key);
        if (correlationId is not null)
        {
            request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        }

        if (traceParent is not null)
        {
            request.Headers.Add("traceparent", traceParent);
        }

        return request;
    }

    private static string Header(HttpResponseMessage response) =>
        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Should().ContainSingle().Subject;

    // ---- Correlation id ----

    [Fact]
    public async Task A_missing_correlation_id_is_generated_and_returned()
    {
        using var response = await _client.SendAsync(Get("/api/dealerships"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Header(response).Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Theory]
    [InlineData("abc-123")]
    [InlineData("Order_42.retry:2")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789")] // 100 chars
    public async Task A_valid_correlation_id_is_echoed(string correlationId)
    {
        using var response = await _client.SendAsync(Get("/api/dealerships", correlationId));

        Header(response).Should().Be(correlationId);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("<script>")]
    [InlineData("a/b")]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890")] // 101 chars
    public async Task An_unsafe_correlation_id_is_replaced_by_a_new_one(string correlationId)
    {
        using var response = await _client.SendAsync(Get("/api/dealerships", correlationId));

        Header(response).Should().NotBe(correlationId).And.MatchRegex("^[0-9a-f]{32}$");
    }

    [Theory]
    [InlineData("/api/appointments/987654", HttpStatusCode.NotFound)]          // use case error
    [InlineData("/api/no-such-route", HttpStatusCode.NotFound)]                // empty 404 filled by StatusCodeProblems
    [InlineData("/api/appointments?customerId=abc", HttpStatusCode.BadRequest)] // model binding error
    public async Task Every_problem_details_has_the_correlation_id_of_the_response_header(string url, HttpStatusCode status)
    {
        using var response = await _client.SendAsync(Get(url, "problem-check-1"));

        response.StatusCode.Should().Be(status);
        Header(response).Should().Be("problem-check-1");
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("correlationId").GetString().Should().Be("problem-check-1");
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Cors_exposes_the_correlation_id_header()
    {
        using var request = Get("/api/dealerships");
        request.Headers.Add("Origin", "http://localhost:5173");

        using var response = await _client.SendAsync(request);

        string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers")).Should().Contain(CorrelationIdMiddleware.HeaderName);
    }

    // ---- Metrics ----

    [Fact]
    public async Task Booking_counters_count_created_replayed_and_no_availability_and_record_the_duration()
    {
        var day = TestDays.ObservedCreatedAndReplayed;
        var meter = _factory.Services.GetRequiredService<SchedulerInstruments>().Meter;
        var measurements = new ConcurrentQueue<(string Instrument, double Value, string? Outcome)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => measurements.Enqueue((instrument.Name, value, Outcome(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => measurements.Enqueue((instrument.Name, value, Outcome(tags))));
        listener.Start();

        var key = $"metrics-{Guid.NewGuid()}";
        using (var created = await _client.SendAsync(Book(day, 10, key)))
        {
            created.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using (var replayed = await _client.SendAsync(Book(day, 10, key)))
        {
            replayed.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using (var full = await _client.SendAsync(Book(TestDays.ObservedNoAvailability, 10, $"setup-{Guid.NewGuid()}")))
        {
            full.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using (var noAvailability = await _client.SendAsync(Book(TestDays.ObservedNoAvailability, 10, $"metrics-{Guid.NewGuid()}", vehicleId: 2)))
        {
            noAvailability.StatusCode.Should().Be(HttpStatusCode.Conflict); // the only standard bay is taken
        }

        listener.Dispose();
        var bookings = measurements.Where(m => m.Instrument == "scheduler.bookings").ToList();
        bookings.Select(m => (m.Value, m.Outcome)).Should().Equal((1, "created"), (1, "replayed"), (1, "created"), (1, "no_availability"));
        var durations = measurements.Where(m => m.Instrument == "scheduler.booking.duration").ToList();
        durations.Select(m => m.Outcome).Should().Equal("created", "replayed", "created", "no_availability");
        durations.Should().OnlyContain(m => m.Value > 0);
        measurements.Should().NotContain(m => m.Instrument == "scheduler.db.retries");
    }

    [Fact]
    public async Task Cancellation_counter_counts_cancelled_and_not_cancellable()
    {
        var meter = _factory.Services.GetRequiredService<SchedulerInstruments>().Meter;
        var outcomes = new ConcurrentQueue<string?>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (ReferenceEquals(instrument.Meter, meter) && instrument.Name == "scheduler.cancellations")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => outcomes.Enqueue(Outcome(tags)));
        listener.Start();
        var booked = await _client.BookOkAsync(OilChange, LocalIso(TestDays.ObservedCreatedAndReplayed, 15));

        (await _client.PostAsync($"/api/appointments/{booked.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostAsync($"/api/appointments/{booked.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        listener.Dispose();
        outcomes.Should().Equal("cancelled", "not_cancellable");
    }

    // ---- Traces ----

    [Fact]
    public async Task The_booking_span_has_ids_and_the_outcome_and_sql_spans_belong_to_the_same_trace()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                {
                    stopped.Enqueue(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var traceParent = $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01";
        var key = $"span-{Guid.NewGuid()}";

        using (var created = await _client.SendAsync(Book(TestDays.ObservedSpan, 9, key, traceParent: traceParent)))
        {
            created.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using (var replayed = await _client.SendAsync(Book(TestDays.ObservedSpan, 9, key, traceParent: traceParent)))
        {
            replayed.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var spans = stopped.Where(a => a.Source.Name == SchedulerInstruments.Name).ToList();
        spans.Should().HaveCount(2);
        spans.Should().OnlyContain(a => a.DisplayName == AppointmentTelemetry.BookingSpanName);
        spans.Select(a => a.GetTagItem("booking.outcome")).Should().Equal("created", "replayed");
        spans[0].GetTagItem("dealership.id").Should().Be(1);
        spans[0].GetTagItem("service_type.id").Should().Be(OilChange);
        spans[0].GetTagItem("appointment.id").Should().BeOfType<int>();
        stopped.Should().Contain(a => a.Kind == ActivityKind.Server && a.Source.Name == "Microsoft.AspNetCore");
        stopped.Should().Contain(a => a.Kind == ActivityKind.Client && a.Source.Name.Contains("SqlClient"), "SQL client spans are on");
        stopped.Where(a => a.Kind == ActivityKind.Server).Should().OnlyContain(a => a.GetTagItem("correlation.id") != null);
    }

    // ---- Logs ----

    [Fact]
    public async Task Booking_logs_carry_the_correlation_id_and_never_the_customer_email_phone_or_vin()
    {
        var correlationId = $"logs-{Guid.NewGuid():N}";
        var key = $"logs-{Guid.NewGuid()}";
        var day = TestDays.ObservedLogs;

        using (var created = await _client.SendAsync(Book(day, 9, key, correlationId: correlationId)))
        {
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            var appointment = JsonSerializer.Deserialize<AppointmentDto>(await created.Content.ReadAsStringAsync(), Json)!;
            appointment.Vehicle.Vin.Should().Be(Vin, "the response itself may show the VIN; the logs must not");
        }

        using (await _client.SendAsync(Book(day, 9, key, correlationId: correlationId)))
        {
        }

        using (await _client.SendAsync(Book(day, 9, $"logs-{Guid.NewGuid()}", correlationId: correlationId)))
        {
        }

        var logs = _factory.Logs.Entries.Where(e => Equals(e.Scope("CorrelationId"), correlationId)).ToList();
        logs.Should().Contain(e => e.Category == AppointmentTelemetry.LogCategory && e.Level == LogLevel.Information && e.Message.StartsWith("Booked appointment ", StringComparison.Ordinal));
        logs.Should().Contain(e => e.Category == AppointmentTelemetry.LogCategory && e.Level == LogLevel.Information && e.Message.StartsWith("Booking replayed", StringComparison.Ordinal));
        logs.Should().Contain(e => e.Category == AppointmentTelemetry.LogCategory && e.Level == LogLevel.Information && e.Message.Contains("VEHICLE_ALREADY_BOOKED", StringComparison.Ordinal));
        logs.Should().OnlyContain(e => e.Scope("TraceId") is string && ((string)e.Scope("TraceId")!).Length == 32);
        foreach (var secret in new[] { Vin, "an.nguyen@example.com", "+84 900 000 001", "An Nguyen" })
        {
            logs.Should().NotContain(e => e.AllText.Contains(secret, StringComparison.OrdinalIgnoreCase), $"logs must not contain {secret}");
        }
    }

    private static string? Outcome(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == "outcome")
            {
                return tag.Value as string;
            }
        }

        return null;
    }
}
