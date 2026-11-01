using Scheduler.Application.Appointments.Book;

namespace Scheduler.Application.Tests.Appointments;

public class BookingRequestHashTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 3, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Canonical_form_is_fixed_order_with_utc_start()
    {
        BookingRequestHash.Canonical(1, 2, 3, 4, Start).Should().Be("v1|1|2|3|4|2026-11-10T03:30:00Z");
    }

    [Fact]
    public void Hash_is_stable_lower_case_hex()
    {
        var hash = BookingRequestHash.Compute(1, 2, 3, 4, Start);

        hash.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
        BookingRequestHash.Compute(1, 2, 3, 4, Start).Should().Be(hash);
    }

    [Fact]
    public void Any_field_change_changes_the_hash()
    {
        var hash = BookingRequestHash.Compute(1, 2, 3, 4, Start);

        BookingRequestHash.Compute(2, 2, 3, 4, Start).Should().NotBe(hash);
        BookingRequestHash.Compute(1, 3, 3, 4, Start).Should().NotBe(hash);
        BookingRequestHash.Compute(1, 2, 4, 4, Start).Should().NotBe(hash);
        BookingRequestHash.Compute(1, 2, 3, 5, Start).Should().NotBe(hash);
        BookingRequestHash.Compute(1, 2, 3, 4, Start.AddMinutes(15)).Should().NotBe(hash);
    }
}
