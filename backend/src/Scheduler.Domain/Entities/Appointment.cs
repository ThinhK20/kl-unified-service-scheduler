using Scheduler.Domain.Common;
using Scheduler.Domain.Enums;
using Scheduler.Domain.Errors;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Domain.Entities;

/// <summary>
/// A booked service: one customer vehicle, one technician and one bay for [StartUtc, EndUtc).
/// It owns the <see cref="ResourceSlot"/> rows that reserve the technician and the bay.
/// Snapshots keep the service rules as they were at booking time.
/// </summary>
public sealed class Appointment : Entity
{
    private readonly List<ResourceSlot> _resourceSlots = [];

    private Appointment()
    {
    }

    public int CustomerId { get; private set; }

    public int VehicleId { get; private set; }

    public int DealershipId { get; private set; }

    public int ServiceTypeId { get; private set; }

    public int TechnicianId { get; private set; }

    public int ServiceBayId { get; private set; }

    public DateTime StartUtc { get; private set; }

    public DateTime EndUtc { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    /// <summary>Service duration in minutes at booking time.</summary>
    public int DurationSnapshot { get; private set; }

    /// <summary>Required skill at booking time.</summary>
    public Skill RequiredSkillSnapshot { get; private set; }

    /// <summary>Required bay type at booking time.</summary>
    public BayType RequiredBayTypeSnapshot { get; private set; }

    /// <summary>Concurrency token (SQL Server rowversion), set by the database.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Slot rows held by this appointment. Empty after cancel.</summary>
    public IReadOnlyCollection<ResourceSlot> ResourceSlots => _resourceSlots.AsReadOnly();

    // Navigation properties for reads. Create() does not set them; they are only filled when loaded.
    public Vehicle? Vehicle { get; private set; }

    public Dealership? Dealership { get; private set; }

    public ServiceType? ServiceType { get; private set; }

    public Technician? Technician { get; private set; }

    public ServiceBay? ServiceBay { get; private set; }

    /// <summary>The booked time range.</summary>
    public TimeRange Range => TimeRange.Create(StartUtc, EndUtc);

    public bool CanBeCancelled => Status == AppointmentStatus.Confirmed;

    /// <summary>
    /// Creates a confirmed appointment and its slot rows (one per 15 minutes for the technician and for the bay).
    /// Throws a <see cref="DomainException"/> when any booking rule is broken: start not on the grid or not after
    /// <paramref name="nowUtc"/>, vehicle not owned by the customer, inactive dealership or service, service not
    /// offered at the dealership, outside opening hours, or a technician / bay that cannot do the job.
    /// It does NOT check that the technician and bay are free; the unique slot index does that.
    /// </summary>
    public static Appointment Create(
        int customerId,
        Vehicle vehicle,
        Dealership dealership,
        ServiceType serviceType,
        Technician technician,
        ServiceBay serviceBay,
        DateTime startUtc,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(dealership);
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(technician);
        ArgumentNullException.ThrowIfNull(serviceBay);
        Guard.Positive(customerId, nameof(CustomerId));

        var start = SlotGrid.EnsureOnGrid(startUtc);
        var now = Guard.Utc(nowUtc);
        if (start <= now)
        {
            throw new DomainException(DomainErrors.Time.StartNotInFuture);
        }

        Ensure(vehicle.IsOwnedBy(customerId), DomainErrors.Appointment.VehicleNotOwnedByCustomer);
        Ensure(dealership.IsActive, DomainErrors.Appointment.DealershipInactive);
        Ensure(serviceType.IsActive, DomainErrors.Appointment.ServiceTypeInactive);
        Ensure(serviceType.IsOfferedAt(dealership.Id), DomainErrors.Appointment.ServiceNotOfferedAtDealership);

        var range = TimeRange.FromStart(start, serviceType.Duration);
        Ensure(dealership.IsOpenFor(range), DomainErrors.Appointment.OutsideOpeningHours);
        Ensure(technician.CanDo(serviceType, dealership, range), DomainErrors.Appointment.TechnicianNotUsable);
        Ensure(serviceBay.CanHost(serviceType, dealership.Id), DomainErrors.Appointment.BayNotUsable);

        var appointment = new Appointment
        {
            CustomerId = customerId,
            VehicleId = vehicle.Id,
            DealershipId = dealership.Id,
            ServiceTypeId = serviceType.Id,
            TechnicianId = technician.Id,
            ServiceBayId = serviceBay.Id,
            StartUtc = range.StartUtc,
            EndUtc = range.EndUtc,
            Status = AppointmentStatus.Confirmed,
            CreatedUtc = now,
            DurationSnapshot = serviceType.DurationMinutes,
            RequiredSkillSnapshot = serviceType.RequiredSkill,
            RequiredBayTypeSnapshot = serviceType.RequiredBayType,
        };

        foreach (var slotStart in SlotGrid.Split(range))
        {
            appointment._resourceSlots.Add(ResourceSlot.For(ResourceType.Technician, technician.Id, slotStart, appointment));
            appointment._resourceSlots.Add(ResourceSlot.For(ResourceType.Bay, serviceBay.Id, slotStart, appointment));
        }

        return appointment;
    }

    /// <summary>
    /// Cancels the appointment and releases its slots (the slot rows are removed, the appointment row stays).
    /// Only a confirmed appointment can be cancelled. The slots must be loaded before calling this.
    /// </summary>
    public void Cancel()
    {
        if (!CanBeCancelled)
        {
            throw new DomainException(DomainErrors.Appointment.NotCancellable(Status));
        }

        Status = AppointmentStatus.Cancelled;
        _resourceSlots.Clear();
    }

    private static void Ensure(bool condition, DomainError error)
    {
        if (!condition)
        {
            throw new DomainException(error);
        }
    }
}
