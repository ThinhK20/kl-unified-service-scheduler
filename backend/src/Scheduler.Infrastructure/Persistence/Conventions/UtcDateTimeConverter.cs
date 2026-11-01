using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Scheduler.Infrastructure.Persistence.Conventions;

/// <summary>
/// SQL Server <c>datetime2</c> has no time zone, so values come back as <see cref="DateTimeKind.Unspecified"/>.
/// All times in this database are UTC, so this converter marks every value read back as <see cref="DateTimeKind.Utc"/>.
/// Values are written unchanged (the domain only accepts UTC or unspecified values).
/// </summary>
internal sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }
}
