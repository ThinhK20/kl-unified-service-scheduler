namespace Scheduler.Domain.Enums;

/// <summary>
/// The kind of resource a slot row reserves. The vehicle is reserved too, so one car cannot be booked twice at the
/// same time (at any dealership).
/// </summary>
public enum ResourceType
{
    Technician,
    Bay,
    Vehicle,
}
