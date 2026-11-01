using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Scheduler.Application.Appointments.Book;

/// <summary>
/// SHA-256 of a canonical form of the booking request (SPEC section 5 step 2). The five fields are written in a fixed
/// order with the start time in UTC, so "10:30+07:00" and "03:30Z" give the same hash.
/// </summary>
public static class BookingRequestHash
{
    /// <summary>The canonical text that is hashed, for example "v1|1|2|1|3|2026-11-10T03:30:00Z".</summary>
    public static string Canonical(int customerId, int vehicleId, int dealershipId, int serviceTypeId, DateTime startUtc) =>
        string.Join(
            '|',
            "v1",
            customerId.ToString(CultureInfo.InvariantCulture),
            vehicleId.ToString(CultureInfo.InvariantCulture),
            dealershipId.ToString(CultureInfo.InvariantCulture),
            serviceTypeId.ToString(CultureInfo.InvariantCulture),
            startUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    /// <summary>Lower-case hex SHA-256 (64 characters) of <see cref="Canonical"/>.</summary>
    public static string Compute(int customerId, int vehicleId, int dealershipId, int serviceTypeId, DateTime startUtc)
    {
        var bytes = Encoding.UTF8.GetBytes(Canonical(customerId, vehicleId, dealershipId, serviceTypeId, startUtc));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
