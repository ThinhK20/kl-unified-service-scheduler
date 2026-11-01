using Scheduler.Domain.Errors;

namespace Scheduler.Domain.Common;

/// <summary>Small checks used by entity factories. Each one throws a <see cref="DomainException"/>.</summary>
internal static class Guard
{
    public static string NotBlank(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(DomainErrors.Entity.Required(field));
        }

        return value.Trim();
    }

    public static int Positive(int value, string field)
    {
        if (value <= 0)
        {
            throw new DomainException(DomainErrors.Entity.MustBePositive(field));
        }

        return value;
    }

    /// <summary>
    /// Returns the value as a UTC <see cref="DateTime"/>. <see cref="DateTimeKind.Unspecified"/> is treated
    /// as UTC (SQL Server datetime2 comes back unspecified); <see cref="DateTimeKind.Local"/> is rejected.
    /// </summary>
    public static DateTime Utc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => throw new DomainException(DomainErrors.Time.NotUtc),
        };
    }
}
