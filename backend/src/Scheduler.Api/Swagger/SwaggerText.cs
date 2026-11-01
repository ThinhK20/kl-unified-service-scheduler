namespace Scheduler.Api.Swagger;

/// <summary>Text shown at the top of the Swagger page.</summary>
internal static class SwaggerText
{
    public const string Description = """
        Book a car service. The system picks a qualified technician and a free bay for the whole job.

        * All times in responses are UTC (ISO-8601 with `Z`). `startTime` in a booking needs an offset, for example `2026-11-10T10:30:00+07:00`.
        * Start times are on a 15-minute grid (:00, :15, :30, :45).
        * `POST /api/appointments` needs an `Idempotency-Key` header (max 100 characters). Same key + same body = same response, no second booking.
        * Health: `GET /health/live` (process up) and `GET /health/ready` (database reachable, else 503).

        Every error is `application/problem+json` (RFC 7807) with a stable `code`:

        | code | HTTP | Retry? | Meaning |
        |---|---|---|---|
        | INVALID_REQUEST | 400 (also 405, 415) | No | Missing field or header, bad format, not on the grid, in the past, wrong method or content type |
        | NOT_FOUND | 404 | No | Unknown customer, vehicle, dealership, service type, appointment or route |
        | BOOKING_RULE_VIOLATION | 422 | No | Vehicle not owned by customer, inactive dealership/service, service not offered there, outside opening hours |
        | NO_AVAILABILITY | 409 | No | Valid request but no technician + bay pair is free; see `suggestedStartTimes` |
        | IDEMPOTENCY_CONFLICT | 409 | No | Same Idempotency-Key with a different body |
        | VEHICLE_ALREADY_BOOKED | 409 | No | The vehicle already has a booking that overlaps this time (at any dealership) |
        | APPOINTMENT_NOT_CANCELLABLE | 409 | No | Only Confirmed appointments that have not started yet can be cancelled |
        | DATABASE_BUSY | 503 | Yes | Database busy after one retry (`Retry-After: 1`); retry with the same Idempotency-Key |
        | INTERNAL_ERROR | 500 | No | Unexpected bug |

        Field errors are in `errors` (field → messages).
        """;
}
