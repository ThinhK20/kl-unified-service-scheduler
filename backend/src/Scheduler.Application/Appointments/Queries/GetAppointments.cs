using Scheduler.Application.Abstractions;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;

namespace Scheduler.Application.Appointments.Queries;

/// <summary>All appointments of a customer.</summary>
public sealed record GetAppointmentsQuery(int CustomerId);

/// <summary>Returns a customer's appointments, newest first (StartUtc descending, then Id descending).</summary>
public sealed class GetAppointmentsHandler
{
    private readonly IAppointmentRepository _appointments;

    public GetAppointmentsHandler(IAppointmentRepository appointments)
    {
        _appointments = appointments;
    }

    public async Task<Result<IReadOnlyList<AppointmentDto>>> HandleAsync(GetAppointmentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.CustomerId <= 0)
        {
            return Error.Validation(
                "customerId is required.",
                new Dictionary<string, string[]> { ["customerId"] = ["customerId is required."] });
        }

        var appointments = await _appointments.GetByCustomerWithDetailsAsync(query.CustomerId, cancellationToken);
        IReadOnlyList<AppointmentDto> dtos = appointments
            .OrderByDescending(a => a.StartUtc)
            .ThenByDescending(a => a.Id)
            .Select(AppointmentMapper.ToDto)
            .ToList();
        return Result.Success(dtos);
    }
}
