using Scheduler.Application.Appointments.Dtos;

namespace Scheduler.Application.Appointments.Book;

/// <summary>
/// The booked appointment. <see cref="IsReplay"/> is true when the same Idempotency-Key and body were seen before
/// and the saved response is returned (the API still answers 201 with the same body).
/// </summary>
public sealed record BookAppointmentResult(AppointmentDto Appointment, bool IsReplay);
