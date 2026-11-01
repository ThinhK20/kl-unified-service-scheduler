using System.Net;
using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.IntegrationTests.Infrastructure;
using static Scheduler.IntegrationTests.Api.ApiClient;

namespace Scheduler.IntegrationTests.Api;

[Collection(SqlServerCollection.Name)]
public sealed class CancelApiTests(SqlServerFixture fixture)
{
    private const int BrakeInspection = 2;
    private readonly HttpClient _client = SchedulerApiFactory.For(fixture).CreateClient();

    [Fact]
    public async Task Cancel_frees_the_slots_and_the_same_slot_can_be_booked_again()
    {
        var day = TestDays.CancelAndRebook;
        var booked = await _client.BookOkAsync(BrakeInspection, LocalIso(day, 13));
        (await CountSlotsAsync(booked.Id)).Should().Be(18); // 6 slots x (technician + bay + vehicle)

        using var cancel = await _client.PostAsync($"/api/appointments/{booked.Id}/cancel", content: null);

        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await cancel.ReadAsync<AppointmentDto>();
        cancelled.Status.Should().Be("Cancelled");
        cancelled.Id.Should().Be(booked.Id);
        (await CountSlotsAsync(booked.Id)).Should().Be(0);

        var again = await _client.BookOkAsync(BrakeInspection, LocalIso(day, 13));
        again.Id.Should().NotBe(booked.Id);
        again.Technician.Name.Should().Be(booked.Technician.Name);
        again.ServiceBay.Name.Should().Be(booked.ServiceBay.Name);

        var read = await (await _client.GetAsync($"/api/appointments/{booked.Id}")).ReadAsync<AppointmentDto>();
        read.Status.Should().Be("Cancelled", "the cancelled row stays");
    }

    [Fact]
    public async Task Cancel_twice_is_409_not_cancellable()
    {
        var booked = await _client.BookOkAsync(BrakeInspection, LocalIso(TestDays.CancelTwice, 9));
        (await _client.PostAsync($"/api/appointments/{booked.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = await _client.PostAsync($"/api/appointments/{booked.Id}/cancel", null);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.ReadProblemAsync()).Code().Should().Be("APPOINTMENT_NOT_CANCELLABLE");
    }

    [Fact]
    public async Task Cancel_after_the_start_is_409_not_cancellable()
    {
        var day = TestDays.CancelAfterStart;
        var factory = SchedulerApiFactory.For(fixture);
        var booked = await _client.BookOkAsync(BrakeInspection, LocalIso(day, 9));
        factory.Clock.UtcNow = LocalToUtc(day, 9, 30);
        try
        {
            using var response = await _client.PostAsync($"/api/appointments/{booked.Id}/cancel", null);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var problem = await response.ReadProblemAsync();
            problem.Code().Should().Be("APPOINTMENT_NOT_CANCELLABLE");
            problem.GetProperty("detail").GetString().Should().Be("This appointment has already started or passed.");
            (await CountSlotsAsync(booked.Id)).Should().Be(18);
        }
        finally
        {
            factory.Clock.UtcNow = FixedClock.DefaultNow;
        }
    }

    [Fact]
    public async Task Cancel_unknown_is_404()
    {
        using var response = await _client.PostAsync("/api/appointments/987654321/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).Code().Should().Be("NOT_FOUND");
    }

    private async Task<int> CountSlotsAsync(int appointmentId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.ResourceSlots.CountAsync(s => s.AppointmentId == appointmentId);
    }
}
