namespace Scheduler.Application.Appointments.Cancel;

/// <summary>Cancel an appointment. Only confirmed appointments can be cancelled.</summary>
public sealed record CancelAppointmentCommand(int AppointmentId);
