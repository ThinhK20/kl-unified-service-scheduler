using FluentValidation;
using Scheduler.Application.Abstractions;
using Scheduler.Application.Allocation;
using Scheduler.Application.Appointments.Dtos;
using Scheduler.Application.Common;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Errors;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Application.Appointments.Book;

/// <summary>
/// Books an appointment (SPEC section 5, steps 1-10). The system picks the technician and the bay.
/// Double booking is stopped by the unique slot index: on <see cref="RepositoryErrors.SlotTaken"/> the next pair is tried.
/// </summary>
public sealed class BookAppointmentHandler
{
    /// <summary>How many nearby start times a NO_AVAILABILITY error suggests.</summary>
    public const int MaxSuggestions = 3;

    private const string StartNotInFutureMessage = "startTime must be in the future.";

    private readonly IValidator<BookAppointmentCommand> _validator;
    private readonly ICustomerRepository _customers;
    private readonly IVehicleRepository _vehicles;
    private readonly IDealershipRepository _dealerships;
    private readonly IServiceTypeRepository _serviceTypes;
    private readonly IAppointmentRepository _appointments;
    private readonly IIdempotencyStore _idempotency;
    private readonly AvailabilityService _availability;
    private readonly IClock _clock;
    private readonly IAppointmentTelemetry _telemetry;

    public BookAppointmentHandler(
        IValidator<BookAppointmentCommand> validator,
        ICustomerRepository customers,
        IVehicleRepository vehicles,
        IDealershipRepository dealerships,
        IServiceTypeRepository serviceTypes,
        IAppointmentRepository appointments,
        IIdempotencyStore idempotency,
        AvailabilityService availability,
        IClock clock,
        IAppointmentTelemetry telemetry)
    {
        _validator = validator;
        _customers = customers;
        _vehicles = vehicles;
        _dealerships = dealerships;
        _serviceTypes = serviceTypes;
        _appointments = appointments;
        _idempotency = idempotency;
        _availability = availability;
        _clock = clock;
        _telemetry = telemetry;
    }

    public async Task<Result<BookAppointmentResult>> HandleAsync(BookAppointmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // SPEC section 10: one span, one timer and one log line per booking request. If an exception escapes,
        // the attempt is disposed without Finish and is recorded as an unexpected error.
        using var attempt = _telemetry.StartBooking(command.DealershipId, command.ServiceTypeId);
        var result = await BookAsync(command, cancellationToken);
        attempt.Finish(result);
        return result;
    }

    private async Task<Result<BookAppointmentResult>> BookAsync(BookAppointmentCommand command, CancellationToken cancellationToken)
    {
        // Step 1a: request shape (no database).
        var validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationErrors.ToError(validation);
        }

        IsoDateTime.TryParseWithOffsetToUtc(command.StartTime, out var startUtc);
        var request = new BookingRequest(
            command.IdempotencyKey!,
            command.CustomerId,
            command.VehicleId,
            command.DealershipId,
            command.ServiceTypeId,
            startUtc,
            BookingRequestHash.Compute(command.CustomerId, command.VehicleId, command.DealershipId, command.ServiceTypeId, startUtc));

        // Step 10: a transient database error retries the whole attempt once.
        try
        {
            return await AttemptAsync(request, cancellationToken);
        }
        catch (TransientDatabaseException ex)
        {
            _telemetry.BookingRetried(ex);
            try
            {
                return await AttemptAsync(request, cancellationToken);
            }
            catch (TransientDatabaseException)
            {
                return Error.DatabaseBusy();
            }
        }
    }

    private async Task<Result<BookAppointmentResult>> AttemptAsync(BookingRequest request, CancellationToken cancellationToken)
    {
        // Step 1b: rules that need the database.
        var loaded = await LoadAsync(request, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var context = loaded.Value;

        // Step 2: idempotency. A replay wins over the time rules below.
        var replay = await CheckIdempotencyAsync(request, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        // Step 1c: the start must be in the future (checked after step 2, SPEC section 0b).
        if (request.StartUtc <= _clock.UtcNow)
        {
            return Error.Validation(
                StartNotInFutureMessage,
                new Dictionary<string, string[]> { ["startTime"] = [StartNotInFutureMessage] });
        }

        // Step 3: the whole half-open range must be inside opening hours.
        var range = TimeRange.FromStart(request.StartUtc, context.ServiceType.Duration);
        if (!context.Dealership.IsOpenFor(range))
        {
            return Error.FromDomain(DomainErrors.Appointment.OutsideOpeningHours);
        }

        // The vehicle is a reserved resource too (SPEC section 0c): one car, one booking at a time.
        if (await _appointments.IsVehicleBookedAsync(request.VehicleId, range, cancellationToken))
        {
            return await VehicleAlreadyBookedAsync(request, cancellationToken);
        }

        // Steps 4-5: candidates (may be stale) in strategy order.
        var localDate = DateOnly.FromDateTime(context.Dealership.ToLocal(request.StartUtc));
        var day = await _availability.LoadDayAsync(context.Dealership, context.ServiceType, localDate, cancellationToken);
        var nowUtc = _clock.UtcNow;

        // Steps 6-7: try each pair; the database decides.
        foreach (var pair in day.FindPairs(range))
        {
            Appointment appointment;
            try
            {
                appointment = Appointment.Create(
                    request.CustomerId,
                    context.Vehicle,
                    context.Dealership,
                    context.ServiceType,
                    pair.Technician,
                    pair.ServiceBay,
                    request.StartUtc,
                    nowUtc);
            }
            catch (DomainException ex)
            {
                return Error.FromDomain(ex.Error);
            }

            var entry = new IdempotencyEntry(
                request.IdempotencyKey,
                request.RequestHash,
                nowUtc,
                saved => StoredResponse.Serialize(ToDto(saved, context, pair)));

            var added = await _appointments.TryAddWithSlotsAsync(appointment, entry, cancellationToken);
            if (added.IsSuccess)
            {
                // Step 8.
                return new BookAppointmentResult(ToDto(added.Value, context, pair), IsReplay: false);
            }

            switch (added.Error.Code)
            {
                case RepositoryErrors.SlotTakenCode:
                    // The taken slot may be the VEHICLE's (a twin booking of the same car won): then no pair can work.
                    if (await _appointments.IsVehicleBookedAsync(request.VehicleId, range, cancellationToken))
                    {
                        return await VehicleAlreadyBookedAsync(request, cancellationToken);
                    }

                    continue;
                case RepositoryErrors.IdempotencyKeyTakenCode:
                    // A concurrent request with the same key won: apply step 2 again.
                    return await CheckIdempotencyAsync(request, cancellationToken)
                        ?? throw new TransientDatabaseException("Idempotency-Key was taken but cannot be read yet.");
                default:
                    return added.Error;
            }
        }

        // A concurrent request with the same key may have taken our only slot: its response wins.
        var concurrent = await CheckIdempotencyAsync(request, cancellationToken);
        if (concurrent is not null)
        {
            return concurrent;
        }

        // The candidates may have looked busy because a twin booking of the same car just won.
        if (await _appointments.IsVehicleBookedAsync(request.VehicleId, range, cancellationToken))
        {
            return await VehicleAlreadyBookedAsync(request, cancellationToken);
        }

        // Step 9: no pair worked. Suggest nearby free times from a fresh read of the day.
        // Never suggest a time when the customer's car is busy (it would fail with VEHICLE_ALREADY_BOOKED).
        var fresh = await _availability.LoadDayAsync(context.Dealership, context.ServiceType, localDate, cancellationToken);
        var vehicleBusy = await _appointments.GetVehicleSlotStartsAsync(request.VehicleId, fresh.OpeningHoursUtc, cancellationToken);
        return Error.NoAvailability(fresh.SuggestStartTimes(request.StartUtc, _clock.UtcNow, MaxSuggestions, vehicleBusy));
    }

    /// <summary>
    /// The car is busy. If the booking that holds it is a concurrent retry with the SAME key (it committed after our
    /// step 2), answer with its saved response; otherwise VEHICLE_ALREADY_BOOKED.
    /// </summary>
    private async Task<Result<BookAppointmentResult>> VehicleAlreadyBookedAsync(BookingRequest request, CancellationToken cancellationToken) =>
        await CheckIdempotencyAsync(request, cancellationToken) ?? Error.VehicleAlreadyBooked();

    private async Task<Result<BookingContext>> LoadAsync(BookingRequest request, CancellationToken cancellationToken)
    {
        if (!await _customers.ExistsAsync(request.CustomerId, cancellationToken))
        {
            return Error.NotFound("Customer", request.CustomerId);
        }

        var vehicle = await _vehicles.GetByIdAsync(request.VehicleId, cancellationToken);
        if (vehicle is null)
        {
            return Error.NotFound("Vehicle", request.VehicleId);
        }

        var dealership = await _dealerships.GetByIdAsync(request.DealershipId, cancellationToken);
        if (dealership is null)
        {
            return Error.NotFound("Dealership", request.DealershipId);
        }

        var serviceType = await _serviceTypes.GetByIdAsync(request.ServiceTypeId, cancellationToken);
        if (serviceType is null)
        {
            return Error.NotFound("Service type", request.ServiceTypeId);
        }

        if (!vehicle.IsOwnedBy(request.CustomerId))
        {
            return Error.FromDomain(DomainErrors.Appointment.VehicleNotOwnedByCustomer);
        }

        if (!dealership.IsActive)
        {
            return Error.FromDomain(DomainErrors.Appointment.DealershipInactive);
        }

        if (!serviceType.IsActive)
        {
            return Error.FromDomain(DomainErrors.Appointment.ServiceTypeInactive);
        }

        if (!serviceType.IsOfferedAt(dealership.Id))
        {
            return Error.FromDomain(DomainErrors.Appointment.ServiceNotOfferedAtDealership);
        }

        return new BookingContext(vehicle, dealership, serviceType);
    }

    /// <summary>
    /// Null when the key is new. Same key + same hash: the saved response. Same key + other hash: IDEMPOTENCY_CONFLICT.
    /// </summary>
    private async Task<Result<BookAppointmentResult>?> CheckIdempotencyAsync(BookingRequest request, CancellationToken cancellationToken)
    {
        var record = await _idempotency.FindAsync(request.IdempotencyKey, cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (!string.Equals(record.RequestHash, request.RequestHash, StringComparison.OrdinalIgnoreCase))
        {
            return Error.IdempotencyConflict();
        }

        var saved = StoredResponse.TryDeserialize(record.ResponseJson);
        if (saved is null)
        {
            // The saved body cannot be read; rebuild it from the appointment.
            var appointment = await _appointments.GetByIdWithDetailsAsync(record.AppointmentId, cancellationToken)
                ?? throw new InvalidOperationException($"Idempotency-Key '{record.Key}' points to missing appointment {record.AppointmentId}.");
            saved = AppointmentMapper.ToDto(appointment);
        }

        return new BookAppointmentResult(saved, IsReplay: true);
    }

    private static AppointmentDto ToDto(Appointment appointment, BookingContext context, ResourcePair pair) =>
        AppointmentMapper.ToDto(appointment, context.Vehicle, context.Dealership, context.ServiceType, pair.Technician, pair.ServiceBay);

    private sealed record BookingRequest(
        string IdempotencyKey,
        int CustomerId,
        int VehicleId,
        int DealershipId,
        int ServiceTypeId,
        DateTime StartUtc,
        string RequestHash);

    private sealed record BookingContext(Vehicle Vehicle, Dealership Dealership, ServiceType ServiceType);
}
