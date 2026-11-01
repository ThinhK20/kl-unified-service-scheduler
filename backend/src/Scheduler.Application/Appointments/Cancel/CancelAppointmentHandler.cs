using Scheduler.Application.Abstractions;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;
using Scheduler.Domain.Errors;

namespace Scheduler.Application.Appointments.Cancel;

/// <summary>
/// Cancels a confirmed appointment that has not started yet: Status becomes Cancelled and its slot rows are deleted,
/// so the time can be booked again. The appointment row stays.
/// </summary>
public sealed class CancelAppointmentHandler
{
    private readonly IAppointmentRepository _appointments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CancelAppointmentHandler(IAppointmentRepository appointments, IUnitOfWork unitOfWork, IClock clock)
    {
        _appointments = appointments;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<AppointmentDto>> HandleAsync(CancelAppointmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var appointment = await _appointments.GetForCancelAsync(command.AppointmentId, cancellationToken);
        if (appointment is null)
        {
            return Error.NotFound("Appointment", command.AppointmentId);
        }

        if (!appointment.CanBeCancelled)
        {
            return Error.FromDomain(DomainErrors.Appointment.NotCancellable(appointment.Status));
        }

        if (appointment.StartUtc <= _clock.UtcNow)
        {
            return Error.FromDomain(DomainErrors.Appointment.AlreadyStarted);
        }

        appointment.Cancel(_clock.UtcNow);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Someone else changed it first (for example a parallel cancel).
            return new Error(
                ErrorCodes.AppointmentNotCancellable,
                "The appointment was changed by another request. Reload it and try again.");
        }
        catch (TransientDatabaseException)
        {
            return Error.DatabaseBusy();
        }

        return AppointmentMapper.ToDto(appointment);
    }
}
