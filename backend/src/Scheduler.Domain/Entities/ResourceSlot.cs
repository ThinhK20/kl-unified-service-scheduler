using Scheduler.Domain.Common;
using Scheduler.Domain.Enums;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Domain.Entities;

/// <summary>
/// One 15-minute slot of one resource (technician or bay) taken by an appointment.
/// The database has a UNIQUE index on (ResourceType, ResourceId, SlotStartUtc), which makes double booking impossible.
/// </summary>
public sealed class ResourceSlot
{
    private ResourceSlot()
    {
    }

    public long Id { get; private set; }

    public ResourceType ResourceType { get; private set; }

    /// <summary>Technician id or service bay id, depending on <see cref="ResourceType"/>.</summary>
    public int ResourceId { get; private set; }

    /// <summary>Slot start, UTC, on the 15-minute grid.</summary>
    public DateTime SlotStartUtc { get; private set; }

    public int AppointmentId { get; private set; }

    public Appointment Appointment { get; private set; } = null!;

    /// <summary>Creates a slot row for a resource, linked to the appointment that takes it.</summary>
    public static ResourceSlot For(ResourceType resourceType, int resourceId, DateTime slotStartUtc, Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        return new ResourceSlot
        {
            ResourceType = resourceType,
            ResourceId = Guard.Positive(resourceId, nameof(ResourceId)),
            SlotStartUtc = SlotGrid.EnsureOnGrid(slotStartUtc),
            Appointment = appointment,
            AppointmentId = appointment.Id,
        };
    }
}
