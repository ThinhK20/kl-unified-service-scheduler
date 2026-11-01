using Scheduler.Application.Abstractions;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;

namespace Scheduler.Application.Appointments.Queries;

/// <summary>Get one appointment.</summary>
public sealed record GetAppointmentByIdQuery(int AppointmentId);

/// <summary>Returns the appointment, or NOT_FOUND.</summary>
public sealed class GetAppointmentByIdHandler
{
    private readonly IAppointmentRepository _appointments;

    public GetAppointmentByIdHandler(IAppointmentRepository appointments)
    {
        _appointments = appointments;
    }

    public async Task<Result<AppointmentDto>> HandleAsync(GetAppointmentByIdQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var appointment = await _appointments.GetByIdWithDetailsAsync(query.AppointmentId, cancellationToken);
        return appointment is null
            ? Error.NotFound("Appointment", query.AppointmentId)
            : AppointmentMapper.ToDto(appointment);
    }
}
