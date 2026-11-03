using FluentValidation;

namespace Scheduler.Application.Appointments.Book;

/// <summary>
/// Checks the request shape (SPEC section 5 step 1, the part that needs no database).
/// Field names match the JSON body; the header is reported as "Idempotency-Key".
/// "Start is in the future" is NOT checked here: the handler checks it after the idempotency lookup, so a retry of a
/// saved booking always gets the saved response (SPEC section 0b).
/// </summary>
public sealed class BookAppointmentValidator : AbstractValidator<BookAppointmentCommand>
{
    public const int MaxIdempotencyKeyLength = 100;

    public BookAppointmentValidator()
    {
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

        RuleFor(x => x.StartTime).BookingStartTime();
    }
}
