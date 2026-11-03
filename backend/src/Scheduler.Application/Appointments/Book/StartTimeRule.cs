using FluentValidation;
using Scheduler.Application.Common;
using Scheduler.Domain.ValueObjects;

namespace Scheduler.Application.Appointments.Book;

/// <summary>
/// The booking request's <c>startTime</c> rule (SPEC section 5, step 1a): required, ISO-8601 WITH an offset, inside
/// <see cref="SupportedDates"/>, on the 15-minute grid. Shared with the assignment preview (SPEC section 6) so both
/// give the same messages. "In the future" is not part of it.
/// </summary>
internal static class StartTimeRule
{
    public const string Field = "startTime";

    public static IRuleBuilderOptions<T, string?> BookingStartTime<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("startTime is required.")
            .Must(value => IsoDateTime.TryParseWithOffsetToUtc(value, out _))
            .WithMessage("startTime must be an ISO-8601 date and time with an offset, for example 2026-11-10T10:30:00+07:00.")
            .Must(value => SupportedDates.Contains(ToUtc(value)))
            .WithMessage(SupportedDates.Message(Field))
            .Must(value => SlotGrid.IsOnGrid(ToUtc(value)))
            .WithMessage("startTime must be on the 15-minute grid (:00, :15, :30 or :45, with zero seconds).")
            .OverridePropertyName(Field);

    private static DateTime ToUtc(string? value) =>
        IsoDateTime.TryParseWithOffsetToUtc(value, out var utc) ? utc : default;
}
