using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Scheduler.Api.Observability;

/// <summary>
/// The custom trace source and metrics of the app (SPEC section 10). One instance per app (singleton).
/// The meter comes from <see cref="IMeterFactory"/>, so each app instance (and each test host) has its own meter.
/// </summary>
public sealed class SchedulerInstruments
{
    /// <summary>Name of the <see cref="ActivitySource"/> and of the <see cref="Meter"/>.</summary>
    public const string Name = "Scheduler";

    /// <summary>Spans made by our own code (the booking span).</summary>
    public static readonly ActivitySource ActivitySource = new(Name);

    public SchedulerInstruments(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        Meter = meterFactory.Create(Name);
        Bookings = Meter.CreateCounter<long>("scheduler.bookings", "{booking}", "Booking requests, by outcome.");
        Cancellations = Meter.CreateCounter<long>("scheduler.cancellations", "{cancellation}", "Cancel requests, by outcome.");
        DatabaseRetries = Meter.CreateCounter<long>("scheduler.db.retries", "{retry}", "Times a busy database made us try again.");
        BookingDuration = Meter.CreateHistogram<double>("scheduler.booking.duration", "ms", "How long a booking request took, by outcome.");
    }

    public Meter Meter { get; }

    /// <summary>Tag: outcome (see BookingOutcomes).</summary>
    public Counter<long> Bookings { get; }

    /// <summary>Tag: outcome (see CancellationOutcomes).</summary>
    public Counter<long> Cancellations { get; }

    /// <summary>Tag: operation (for example "book").</summary>
    public Counter<long> DatabaseRetries { get; }

    /// <summary>Milliseconds. Tag: outcome.</summary>
    public Histogram<double> BookingDuration { get; }
}
