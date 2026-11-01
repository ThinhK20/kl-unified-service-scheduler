using FluentValidation;
using Scheduler.Application.Abstractions;
using Scheduler.Application.Common;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Application.Appointments.Book;

/// <summary>
/// Checks the request shape (SPEC section 5 step 1, the part that needs no database).
/// Field names match the JSON body; the header is reported as "Idempotency-Key".
/// </summary>
public sealed class BookAppointmentValidator : AbstractValidator<BookAppointmentCommand>
{
    public const int MaxIdempotencyKeyLength = 100;

    public BookAppointmentValidator(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        RuleFor(x => x.IdempotencyKey)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("The Idempotency-Key header is required.")
            .MaximumLength(MaxIdempotencyKeyLength)
            .WithMessage($"The Idempotency-Key header must be at most {MaxIdempotencyKeyLength} characters.")
            .OverridePropertyName("Idempotency-Key");

        RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("customerId is required.").OverridePropertyName("customerId");
        RuleFor(x => x.VehicleId).GreaterThan(0).WithMessage("vehicleId is required.").OverridePropertyName("vehicleId");
        RuleFor(x => x.DealershipId).GreaterThan(0).WithMessage("dealershipId is required.").OverridePropertyName("dealershipId");
        RuleFor(x => x.ServiceTypeId).GreaterThan(0).WithMessage("serviceTypeId is required.").OverridePropertyName("serviceTypeId");

        RuleFor(x => x.StartTime)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("startTime is required.")
            .Must(value => IsoDateTime.TryParseWithOffsetToUtc(value, out _))
            .WithMessage("startTime must be an ISO-8601 date and time with an offset, for example 2026-11-10T10:30:00+07:00.")
            .Must(value => SlotGrid.IsOnGrid(ToUtc(value)))
            .WithMessage("startTime must be on the 15-minute grid (:00, :15, :30 or :45, with zero seconds).")
            .Must(value => ToUtc(value) > clock.UtcNow)
            .WithMessage("startTime must be in the future.")
            .OverridePropertyName("startTime");
    }

    private static DateTime ToUtc(string? value) =>
        IsoDateTime.TryParseWithOffsetToUtc(value, out var utc) ? utc : default;
}
