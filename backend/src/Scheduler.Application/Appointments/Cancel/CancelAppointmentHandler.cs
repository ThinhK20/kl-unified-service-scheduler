using Scheduler.Application.Abstractions;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;
using Scheduler.Domain.Errors;

namespace Scheduler.Application.Appointments.Cancel;

/// <summary>
/// Cancels a confirmed appointment: Status becomes Cancelled and its slot rows are deleted, so the time can be booked
/// again. The appointment row stays.
/// </summary>
public sealed class CancelAppointmentHandler
{
    private readonly IAppointmentRepository _appointments;
    private readonly IUnitOfWork _unitOfWork;

    public CancelAppointmentHandler(IAppointmentRepository appointments, IUnitOfWork unitOfWork)
    {
        _appointments = appointments;
        _unitOfWork = unitOfWork;
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

        appointment.Cancel();

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
