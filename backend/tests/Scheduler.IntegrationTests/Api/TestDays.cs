namespace Scheduler.IntegrationTests.Api;

/// <summary>
/// One date per API test, so tests sharing the database never see each other's bookings.
/// Persistence tests use March 2030; API tests use June to August 2030 (all after FixedClock.DefaultNow).
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
}
