namespace Scheduler.IntegrationTests.Api;

/// <summary>
/// One date per API test, so tests sharing the database never see each other's bookings.
/// Persistence tests use March 2030; API tests use June to September 2030 (plus January-February 2031 for the 42-day range) (all after FixedClock.DefaultNow).
/// </summary>
internal static class TestDays
{
    public static readonly DateOnly AvailabilitySorted = new(2030, 6, 3);
    public static readonly DateOnly AvailabilityAfterBooking = new(2030, 6, 4);
    public static readonly DateOnly BookAndRead = new(2030, 6, 5);
    public static readonly DateOnly ListNewestFirst = new(2030, 6, 6);
    public static readonly DateOnly LeastQualifiedAfternoon = new(2030, 6, 7);
    public static readonly DateOnly LeastQualifiedMorning = new(2030, 6, 10);
    public static readonly DateOnly CancelAndRebook = new(2030, 6, 11);
    public static readonly DateOnly CancelTwice = new(2030, 6, 12);
    public static readonly DateOnly OutsideHours = new(2030, 6, 13);
    public static readonly DateOnly BadRequests = new(2030, 6, 14);
    public static readonly DateOnly OtherCustomer = new(2030, 6, 17);
    public static readonly DateOnly IdempotentReplay = new(2030, 6, 18);
    public static readonly DateOnly IdempotencyConflict = new(2030, 6, 19);
    public static readonly DateOnly NoAvailability = new(2030, 6, 20);
    public static readonly DateOnly ConcurrentDifferentKeys = new(2030, 6, 21);
    public static readonly DateOnly ConcurrentSameKey = new(2030, 6, 24);
    public static readonly DateOnly ReplayAfterStart = new(2030, 6, 25);
    public static readonly DateOnly CancelledReplay = new(2030, 6, 26);
    public static readonly DateOnly VehicleTwoDealerships = new(2030, 7, 1);
    public static readonly DateOnly VehicleConcurrent = new(2030, 7, 2);
    public static readonly DateOnly CancelAfterStart = new(2030, 7, 3);
    public static readonly DateOnly CorruptTimeZone = new(2030, 7, 4);
    public static readonly DateOnly SuggestionsSkipBusyCar = new(2030, 7, 5);

    // Availability v2 (times with a status, days endpoint): September 2030.
    public static readonly DateOnly TimesCarBusy = new(2030, 9, 2);
    public static readonly DateOnly TimesFullyBooked = new(2030, 9, 3);
    public static readonly DateOnly TimesEveryAvailableBrakes = new(2030, 9, 4);
    public static readonly DateOnly TimesEveryAvailableOil = new(2030, 9, 5);
    public static readonly DateOnly DaysRangeFrom = new(2030, 9, 9);
    public static readonly DateOnly DaysRangeTo = new(2030, 9, 20);
    public static readonly DateOnly DaysMaxRangeFrom = new(2031, 1, 1);
    public static readonly DateOnly DaysMaxRangeTo = new(2031, 2, 11); // 42 days, counting both ends
}
