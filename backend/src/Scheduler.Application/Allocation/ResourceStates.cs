using Scheduler.Domain.Entities;

namespace Scheduler.Application.Allocation;

/// <summary>
/// A technician's state for one time range (SPEC section 6, assignment preview). The enum order is the display
/// order: the expected pick first, then Free, Busy, OffShift, NotQualified, Inactive.
/// </summary>
public enum TechnicianState
{
    /// <summary>The first pair booking would try right now uses this technician.</summary>
    WillServe,

    /// <summary>Active, qualified, the shift covers the whole range and no slot in it is taken.</summary>
    Free,

    /// <summary>Active, qualified and on shift, but at least one slot in the range is taken.</summary>
    Busy,

    /// <summary>Active and qualified, but the shift does not cover the whole range.</summary>
    OffShift,

    /// <summary>Active, but lacks the required skill or minimum tier.</summary>
    NotQualified,

    /// <summary>Not active.</summary>
    Inactive,
}

/// <summary>A service bay's state for one time range (SPEC section 6, assignment preview). The enum order is the display order.</summary>
public enum ServiceBayState
{
    /// <summary>The first pair booking would try right now uses this bay.</summary>
    WillServe,

    /// <summary>Active, the right type, and no slot in the range is taken.</summary>
    Free,

    /// <summary>Active and the right type, but at least one slot in the range is taken.</summary>
    Busy,

    /// <summary>Active, but its bay type is not the one the service needs.</summary>
    WrongType,

    /// <summary>Not active.</summary>
    Inactive,
}

/// <summary>One technician of the dealership and its state.</summary>
public sealed record TechnicianWithState(Technician Technician, TechnicianState State);

/// <summary>One service bay of the dealership and its state.</summary>
public sealed record ServiceBayWithState(ServiceBay ServiceBay, ServiceBayState State);
