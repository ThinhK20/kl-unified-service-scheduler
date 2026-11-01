using Scheduler.Application.Appointments.Book;

namespace Scheduler.Application.Tests.Appointments;

/// <summary>
/// The Idempotency-Key header is read by the API and passed through in the command (null when missing);
/// this validator is the one place that rejects a missing or too long key.
/// </summary>
public class BookAppointmentValidatorTests
{
    private static BookAppointmentValidator Validator => new();

    private static BookAppointmentCommand Valid(string? startTime = "2026-11-10T10:30:00+07:00", string? key = "k") =>
        new(key, 1, 1, 1, 1, startTime);

    private string[] ErrorsFor(BookAppointmentCommand command, string field)
    {
        var result = Validator.Validate(command);
        return result.Errors.Where(e => e.PropertyName == field).Select(e => e.ErrorMessage).ToArray();
    }

    [Theory]
    [InlineData("2026-11-10T10:30:00+07:00")]
    [InlineData("2026-11-10T03:30:00Z")]
    [InlineData("2026-11-10T03:30Z")]
    [InlineData("2026-11-10T03:30:00.0000000Z")]
    [InlineData("2026-11-10T05:15:00+05:45")]
    public void Valid_commands_pass(string startTime)
    {
        Validator.Validate(Valid(startTime)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("2026-11-10T10:30:00")]
    [InlineData("2026-11-10 10:30:00+07:00")]
    [InlineData("10/11/2026 10:30")]
    [InlineData("not a date")]
    [InlineData("2026-13-10T10:30:00+07:00")]
    public void Start_time_without_offset_or_in_a_bad_format_is_rejected(string startTime)
    {
        ErrorsFor(Valid(startTime), "startTime").Should().ContainSingle().Which.Should().Contain("offset");
    }

    [Theory]
    [InlineData("9999-12-31T23:45:00Z")]
    [InlineData("9999-12-31T23:45:00+07:00")]
    [InlineData("9999-01-01T00:00:00Z")]
    [InlineData("1999-12-31T23:45:00Z")]
    [InlineData("0001-01-01T08:00:00Z")]
    public void Start_time_outside_the_technical_year_bound_is_rejected(string startTime)
    {
        ErrorsFor(Valid(startTime), "startTime").Should().Equal("startTime must be between the years 2000 and 9998.");
    }

    [Theory]
    [InlineData("2000-01-01T00:00:00Z")]
    [InlineData("9998-12-31T23:45:00Z")]
    public void Start_time_at_the_edges_of_the_year_bound_passes_the_validator(string startTime)
    {
        ErrorsFor(Valid(startTime), "startTime").Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_start_time_is_rejected(string? startTime)
    {
        ErrorsFor(Valid(startTime), "startTime").Should().Equal("startTime is required.");
    }

    [Theory]
    [InlineData("2026-11-10T10:10:00+07:00")]
    [InlineData("2026-11-10T10:30:05+07:00")]
    [InlineData("2026-11-10T10:30:00.5+07:00")]
    public void Start_time_off_the_15_minute_grid_is_rejected(string startTime)
    {
        ErrorsFor(Valid(startTime), "startTime").Should().ContainSingle().Which.Should().Contain("15-minute grid");
    }

    [Fact]
    public void Start_time_in_the_past_is_not_checked_by_the_validator()
    {
        // The handler checks it after the idempotency lookup (SPEC section 0b).
        ErrorsFor(Valid("2000-01-01T00:00:00Z"), "startTime").Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_idempotency_key_is_rejected(string? key)
    {
        ErrorsFor(Valid(key: key), "Idempotency-Key").Should().Equal("The Idempotency-Key header is required.");
    }

    [Fact]
    public void Idempotency_key_of_100_characters_is_allowed_but_101_is_not()
    {
        ErrorsFor(Valid(key: new string('a', 100)), "Idempotency-Key").Should().BeEmpty();
        ErrorsFor(Valid(key: new string('a', 101)), "Idempotency-Key").Should().ContainSingle();
    }

    [Fact]
    public void Ids_must_be_positive()
    {
        var result = Validator.Validate(new BookAppointmentCommand("k", 0, -1, 0, 0, "2026-11-10T10:30:00+07:00"));

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo("customerId", "vehicleId", "dealershipId", "serviceTypeId");
    }
}
