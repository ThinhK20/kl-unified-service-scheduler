using Scheduler.Domain.Common;

namespace Scheduler.Domain.Entities;

/// <summary>A vehicle. It belongs to exactly one customer.</summary>
public sealed class Vehicle : Entity
{
    private Vehicle()
    {
    }

    public int CustomerId { get; private set; }

    public string Vin { get; private set; } = string.Empty;

    public string Make { get; private set; } = string.Empty;

    public string Model { get; private set; } = string.Empty;

    public int Year { get; private set; }

    public static Vehicle Create(int customerId, string vin, string make, string model, int year) => new()
    {
        CustomerId = Guard.Positive(customerId, nameof(CustomerId)),
        Vin = Guard.NotBlank(vin, nameof(Vin)).ToUpperInvariant(),
        Make = Guard.NotBlank(make, nameof(Make)),
        Model = Guard.NotBlank(model, nameof(Model)),
        Year = Guard.Positive(year, nameof(Year)),
    };

    /// <summary>True when this vehicle belongs to the customer.</summary>
    public bool IsOwnedBy(int customerId) => CustomerId == customerId;
}
