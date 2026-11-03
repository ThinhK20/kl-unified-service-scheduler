# Unified Service Scheduler — Design and Specification

This document is the technical design behind the app: the decisions, the data model, the booking rules and the API
contract. The README explains how to run it; this file explains *why it works the way it does*. Section numbers are
referenced from code comments (for example "SPEC section 5").

## 0. Key decisions

### 0a. Product decisions
- No login. Three demo customers; a "Booking as" switcher in the header picks who is booking (kept per browser).
- The system always assigns the technician and the bay. The customer never picks them, but once a time is chosen the
  form shows who is expected to serve them (assignment preview, section 6).
- The customer's flow follows the build plan: vehicle dropdown, dealership, service type, date picker, free start
  times, then a confirmation card on the same page. The UI design (`docs/design/`) sets the look: colours, fonts,
  header, cards and spacing.

### 0b. Rules that follow from the plan
- Validators live in the Application layer.
- Inactive dealership → 422 `BOOKING_RULE_VIOLATION` (same as an inactive service); unknown id → 404.
- A job may not cross local midnight; a closing time of 00:00 is not supported.
- `SlotGrid` is a static helper for the 15-minute grid; `TimeRange` is the value object.
- The Domain throws `DomainException`; the Application checks rules first and returns `Result<T>`.
- No "Complete" flow in this version: nothing in the API sets `Completed`.
- Idempotency records are an Application/Infrastructure concern, not a Domain entity.
- Every non-2xx API response is ProblemDetails with a `code`, including model-binding 400s, a missing
  `Idempotency-Key` (400), unknown routes (404) and unexpected errors (500).
- Availability `slots` and `suggestedStartTimes` are sorted ascending. "Today" means today in the dealership's zone.
- Availability for an inactive dealership/service, or a service not offered there → 422.
- `GET /api/dealerships` returns active dealerships only. `GET /api/appointments` for an unknown customer → 200 `[]`.
- Idempotency replay wins over the time rules: format, grid and offset are checked first, but "start is in the
  future" is checked after the idempotency lookup, so a retry of a saved booking always gets the saved 201.
- No `EnableRetryOnFailure`: bookings use explicit transactions and the handler's own single retry.
- The idempotency record is saved in the same transaction as the appointment, in two steps: appointment + slots
  (which creates the Id), then the key row with the saved response.

### 0c. Changes after an independent review
An independent review of the first complete version led to these changes:
- **The vehicle is a reserved resource.** Each appointment also writes vehicle slot rows (one per 15 minutes) under
  the same unique index. A car with an overlapping Confirmed appointment at any dealership → 409
  `VEHICLE_ALREADY_BOOKED`. `NO_AVAILABILITY` suggestions skip times when the car is busy. Cancel frees the car.
- **Scarcity-aware allocation** (`ScarcityAwareStrategy`, the default). Technicians are ordered by:
  1. fewest other active services at the dealership for which they are the *only* active qualified technician
     (counted over all active technicians, not only those free right now), so sole specialists stay free;
  2. fewest other active services they are qualified for;
  3. lowest tier in the required skill;
  4. fewest Confirmed appointments that local day;
  5. lowest Id.

  It only reorders candidates: if the specialist is the only one free, they still get the job.
  `LeastQualifiedFirstStrategy` (the first version) stays as an alternative.
- **Cancel only before the start.** `StartUtc <= now` → 409 `APPOINTMENT_NOT_CANCELLABLE`. The UI hides Cancel for
  past appointments and asks for confirmation first.
- **Robustness.** Client dates outside the years 2000–9998 → 400 (a technical bound, not a booking horizon). An
  unknown time zone in stored data → 500 (broken data, not the client's fault). Idempotency keys are case-sensitive
  (BIN2 collation). Expected slot clashes are no longer logged as errors.

## 1. Goal
A customer picks a vehicle, a service type, a dealership and a start time. The system checks that one qualified
technician and one free service bay are available for the **whole** duration, and saves an Appointment linking
customer, vehicle, technician and bay. Double booking must be impossible even under concurrent requests: the
database blocks it with unique slot rows on a 15-minute grid.

Out of scope: login, notifications, rescheduling, jobs that need two technicians, payment.

## 2. Tech stack
| Part | Choice |
|---|---|
| API | ASP.NET Core 8 Web API with controllers |
| Data access | EF Core 8 (SQL Server provider), migrations |
| Database | SQL Server 2022 in Docker |
| Validation | FluentValidation |
| Backend tests | xUnit, FluentAssertions, Testcontainers (SQL Server), WebApplicationFactory, NetArchTest |
| Frontend | React 18 + TypeScript + Vite |
| Server state / forms | TanStack Query / React Hook Form + Zod |
| Frontend tests | Vitest + React Testing Library |
| Run everything | Docker Compose (db + api + web) |
| API docs | Swagger (Swashbuckle) |

Not used on purpose: MediatR, a generic `Repository<T>`, AutoMapper.

## 3. Repository layout and layers
```
backend/
  src/Scheduler.Domain/          Entities, ValueObjects (TimeRange, SlotGrid), Enums, Errors
  src/Scheduler.Application/     Abstractions (repository interfaces, IClock), Appointments (Book, Cancel, Queries),
                                 Catalog (Queries), Allocation (strategies, AvailabilityService), Common (Result, Error)
  src/Scheduler.Infrastructure/  Persistence (DbContext, configurations, migrations), Repositories, Seeding
  src/Scheduler.Api/             Controllers, Middleware, Http (ProblemDetails mapping), Swagger, Program.cs
  tests/                         Domain, Application, Architecture, Integration (real SQL Server)
frontend/src/                    api/, features/booking, features/appointments, components/, lib/
```
Layer rules (enforced by architecture tests): Domain → nothing. Application → Domain. Infrastructure → Application,
Domain. Api → Application, Infrastructure (DI wiring only). Handlers never see `DbContext`.

## 4. Data model
All times are stored in **UTC** (`datetime2`). The dealership's IANA time zone (for example `Asia/Ho_Chi_Minh`) is
used only to show times and to check opening hours and shifts.

| Table | Columns | Notes |
|---|---|---|
| Customers | Id, Name, Email, Phone | |
| Vehicles | Id, CustomerId, Vin, Make, Model, Year | belongs to one customer; VIN unique |
| Dealerships | Id, Name, TimeZoneId, OpenTime, CloseTime, IsActive | local `time` |
| ServiceTypes | Id, DealershipId, Name, DurationMinutes, RequiredSkill, MinimumTier, RequiredBayType, IsActive | duration is a multiple of 15 |
| Technicians | Id, DealershipId, Name, ShiftStart, ShiftEnd, IsActive | shift is local `time`, same every day |
| TechnicianSkills | TechnicianId, Skill, Tier | PK (TechnicianId, Skill) |
| ServiceBays | Id, DealershipId, Name, BayType, IsActive | |
| Appointments | Id, CustomerId, VehicleId, DealershipId, ServiceTypeId, TechnicianId, ServiceBayId, StartUtc, EndUtc, Status, CreatedUtc, DurationSnapshot, RequiredSkillSnapshot, RequiredBayTypeSnapshot, RowVersion | snapshots keep the service rules as they were at booking time |
| ResourceSlots | Id, ResourceType, ResourceId, SlotStartUtc, AppointmentId | **UNIQUE (ResourceType, ResourceId, SlotStartUtc)** |
| IdempotencyKeys | Key (PK, case-sensitive), RequestHash, AppointmentId, ResponseJson, CreatedUtc | same transaction as the appointment |

The key constraint:
```sql
CREATE UNIQUE INDEX UX_ResourceSlots_Resource_Slot ON ResourceSlots (ResourceType, ResourceId, SlotStartUtc);
```

Enums are stored as strings: `Skill` (General, Brakes, Tyres, EV, Diagnostics), `SkillTier`
(Trainee < Qualified < Senior < Master), `BayType` (Standard, Lift, EV), `AppointmentStatus` (Confirmed, Cancelled,
Completed), `ResourceType` (Technician, Bay, Vehicle).

Rules:
- A start time must be on the 15-minute grid (:00, :15, :30, :45, zero seconds) and in the future.
- A 60-minute job writes 4 slot rows each for the technician, the bay and the vehicle, in the same transaction as the
  appointment.
- Cancel deletes the appointment's slot rows; the appointment stays with Status = Cancelled.
- A bay matches when its type equals the required bay type. A technician qualifies when they have the required
  skill at or above the minimum tier.
- The whole range must be inside the dealership's opening hours and the technician's shift (local time, same day).

### Seed data (fixed Ids)
Both dealerships use `Asia/Ho_Chi_Minh`.
- Dealership 1 "District 1 Service Centre", 08:00–17:00. Dealership 2 "Thu Duc Service Centre", 07:30–18:00.
- Customer 1 "An Nguyen" with vehicles 1 (Toyota Vios 2021) and 2 (Honda CR-V 2023); customer 2 "Binh Tran" with
  vehicle 3 (VinFast VF 8 2024); customer 3 "Chi Le" with vehicles 4 (Mazda CX-5 2022) and 5 (Kia Morning 2020).
  Customers 2–3 and vehicles 3–5 come from the `ThreeDemoCustomers` migration.

| Id | Dealer | Service | Min | Skill | Min tier | Bay |
|---|---|---|---|---|---|---|
| 1 | 1 | Oil change | 60 | General | Trainee | Standard |
| 2 | 1 | Brake inspection | 90 | Brakes | Qualified | Lift |
| 3 | 1 | Tyre rotation | 30 | Tyres | Trainee | Lift |
| 4 | 1 | EV battery check | 60 | EV | Senior | EV |
| 5 | 2 | Oil change | 60 | General | Trainee | Standard |

| Id | Dealer | Technician | Shift | Active | Skills |
|---|---|---|---|---|---|
| 1 | 1 | Nam | 08:00–17:00 | yes | General Master, Brakes Senior, Tyres Senior |
| 2 | 1 | Lan | 08:00–17:00 | yes | General Qualified, Tyres Qualified, EV Senior |
| 3 | 1 | Minh | 12:00–17:00 | yes | General Trainee, Brakes Qualified |
| 4 | 2 | Hoa | 07:30–18:00 | yes | General Senior |
| 5 | 1 | Tuan | 08:00–17:00 | no | General Master, Brakes Master, EV Master |

| Id | Dealer | Bay | Type | Active |
|---|---|---|---|---|
| 1 | 1 | Bay 1 | Standard | yes |
| 2 | 1 | Bay 2 | Lift | yes |
| 3 | 1 | Bay 3 | EV | yes |
| 4 | 2 | Bay A | Standard | yes |
| 5 | 1 | Bay 4 | Lift | no |

## 5. Booking flow (POST /api/appointments)
1. **Validate** the request shape: required fields, `startTime` with an offset and on the grid, `Idempotency-Key`
   present (max 100). Then check that the customer, vehicle, dealership and service type exist (404) and the booking
   rules hold (422: vehicle owned by the customer, dealership and service active, service offered there).
2. **Idempotency.** SHA-256 of a canonical form of the body. Same key + same hash → return the saved 201. Same key +
   different hash → 409 `IDEMPOTENCY_CONFLICT`. Then check the start is in the future (400).
3. **Time range** = [start, start + duration), half-open. It must be inside opening hours (422).
4. **Vehicle check.** If the car already has a slot in the range → 409 `VEHICLE_ALREADY_BOOKED`.
5. **Candidates** (a fast filter that may be stale): active, qualified technicians whose shift covers the range and
   who are free; active bays of the right type that are free. Ordered by the allocation strategy (section 0c).
6. **Try each pair:** insert the appointment, all its slot rows and the idempotency record in one transaction.
7. **On a duplicate-key error** (SQL 2601/2627) on the slot index, roll back and clear the change tracker. If the
   car is now booked → `VEHICLE_ALREADY_BOOKED`; otherwise try the next pair. A duplicate idempotency key means a
   same-key twin won: re-read the key and apply step 2.
8. **Success** → 201 Created with a `Location` header and the appointment.
9. **No pair works** → 409 `NO_AVAILABILITY` with up to 3 nearby free start times (same local day, nearest first,
   skipping times when the car is busy, returned in ascending order).
10. **Transient database errors** (deadlock, timeout) retry the whole attempt once; still failing → 503
    `DATABASE_BUSY`.

Step 5 is only a hint. Step 6 is the real check: the database rejects any overlap.

`GET availability` returns every grid start time on that local day where at least one valid technician + bay pair
is free for the whole duration and the start is in the future. It reserves nothing.

## 6. API contract
JSON is camelCase with enums as strings. Times in responses are UTC with `Z`. The booking request's `startTime`
needs an offset (for example `"2026-11-10T10:30:00+07:00"`).

| Method | Path | Responses |
|---|---|---|
| GET | /api/dealerships | 200 |
| GET | /api/service-types?dealershipId= | 200, 400 |
| GET | /api/customers | 200 |
| GET | /api/customers/{id}/vehicles | 200, 404 |
| GET | /api/dealerships/{id}/availability?serviceTypeId=&date=YYYY-MM-DD[&vehicleId=] | 200, 400, 404, 422 |
| GET | /api/dealerships/{id}/availability/days?serviceTypeId=&from=&to=[&vehicleId=] | 200, 400, 404, 422 |
| GET | /api/dealerships/{id}/assignment-preview?serviceTypeId=&startTime=[&vehicleId=] | 200, 400, 404, 422 |
| POST | /api/appointments (header `Idempotency-Key`) | 201, 400, 404, 409, 422, 503 |
| GET | /api/appointments?customerId= | 200, 400 |
| GET | /api/appointments/{id} | 200, 404 |
| POST | /api/appointments/{id}/cancel | 200, 404, 409, 503 |
| GET | /health/live, /health/ready | 200 / 200, 503 |

```ts
DealershipDto   { id; name; timeZoneId; openTime: "HH:mm"; closeTime: "HH:mm" }
ServiceTypeDto  { id; dealershipId; name; durationMinutes; requiredSkill; minimumTier; requiredBayType }
CustomerDto     { id; name; email; phone }
VehicleDto      { id; customerId; vin; make; model; year }
AvailabilityDto { dealershipId; serviceTypeId; date; timeZoneId; durationMinutes;
                  slots: { startUtc; endUtc }[];                                  // exactly the Available times
                  times: { startUtc; endUtc; status: "Past" | "VehicleBooked" | "Booked" | "Available" }[] }
AssignmentPreviewDto { dealershipId; serviceTypeId; timeZoneId; startUtc; endUtc;
                  status: "Past" | "VehicleBooked" | "NoAvailability" | "Assignable";
                  technician { id; name } | null; serviceBay { id; name; bayType } | null;
                  technicians: { id; name; state: "WillServe" | "Free" | "Busy" | "OffShift" | "NotQualified" | "Inactive" }[];
                  serviceBays: { id; name; bayType; state: "WillServe" | "Free" | "Busy" | "WrongType" | "Inactive" }[] }
AvailabilityDaysDto { dealershipId; serviceTypeId; from; to; timeZoneId; durationMinutes;
                  days: { date; availableCount; status: "Past" | "FullyBooked" | "Available" }[] }
BookAppointmentRequest { customerId; vehicleId; dealershipId; serviceTypeId; startTime }
AppointmentDto  { id; status; startUtc; endUtc; createdUtc; customerId;
                  vehicle { id; make; model; year; vin }; dealership { id; name; timeZoneId };
                  serviceType { id; name; durationMinutes }; technician { id; name };
                  serviceBay { id; name; bayType } }
```

**Availability (UX only, reserves nothing; booking stays the final authority).**
- `times` lists every 15-minute start from opening while the whole job fits before closing. Status, first match wins,
  in the same order booking checks: `Past` (start <= now) → `VehicleBooked` (only when `vehicleId` is given: the car
  has a slot in the range, any dealership) → `Booked` (no free technician + bay pair, `DaySchedule.FindPairs`) →
  `Available`. Both endpoints and the 409 suggestions use the same `DaySchedule.ClassifyStartTimes`, so the screen
  and booking agree on the same snapshot; every `Available` time books with 201 (integration-tested).
- `vehicleId` is optional; absent = ignore the car (old response). Present but not a positive integer (including
  empty) → 400; unknown → 404. No ownership check.
- Days: `from`/`to` are local dates, inclusive, at most 42 days (one month view). `Past` = before today in the
  dealership zone; `FullyBooked` = no `Available` time (also today when every remaining time has passed). The whole
  range needs one query for the technicians' and bays' taken slots and one for the car's slots (plus the catalog
  lookups); each day is then computed in memory. Days before today load nothing.
- The web app uses this for a calendar that disables past and fully booked days (still focusable and announced), and a
  time list that shows taken times disabled with the reason ("Booked", "Your car is booked"); past times are hidden.

**Assignment preview (read-only, reserves nothing).** "Who would serve this booking if it were made now."
- `startTime` uses the booking request's rules and messages (offset required, on the grid, supported years; URL-encode
  the `+`). 404s and 422s as availability; a job outside opening hours is 422 with booking's message. A past start is
  not an error: it returns status `Past`.
- Status, first match wins, in booking's order: `Past` → `VehicleBooked` → `NoAvailability` → `Assignable`. When
  Assignable, `technician` and `serviceBay` are exactly `FindPairs(range)[0]` from the same `LoadDayAsync` schedule
  booking uses (same counts, same strategy), so booking right away gets the same pair (integration-tested). Someone
  else can still book first; the confirmation shows the final pick.
- `technicians` / `serviceBays` list every resource of the dealership, inactive ones too, with a state from the same
  rules `FindPairs` uses (active, qualified, shift covers the range, free slots, bay type). Order: WillServe, Free,
  Busy, OffShift, NotQualified / WrongType, Inactive; then by name.
- In the web app the panel appears once an Available time is chosen. When the preview is not Assignable it shows a
  one-line reason instead of a pick; if the preview cannot be loaded it says so and booking still works. The
  "Booking as" switcher is disabled while a booking request is in flight; in the live preview "Reset demo data" also
  switches back to the first customer.

Errors are RFC 7807 ProblemDetails: `{ type, title, status, detail, code, traceId, errors?, suggestedStartTimes? }`.

| code | HTTP | Retry? | Meaning |
|---|---|---|---|
| INVALID_REQUEST | 400 | No | missing field, bad format, off the grid, in the past, missing key, date out of range |
| NOT_FOUND | 404 | No | unknown customer, vehicle, dealership, service type or appointment |
| BOOKING_RULE_VIOLATION | 422 | No | vehicle not owned, inactive dealership or service, not offered there, outside opening hours |
| NO_AVAILABILITY | 409 | No | no technician + bay pair free; includes `suggestedStartTimes` |
| VEHICLE_ALREADY_BOOKED | 409 | No | the car already has a booking at that time |
| IDEMPOTENCY_CONFLICT | 409 | No | same key, different body |
| APPOINTMENT_NOT_CANCELLABLE | 409 | No | not Confirmed, or already started |
| DATABASE_BUSY | 503 | Yes | database busy after one retry |
| INTERNAL_ERROR | 500 | — | unexpected error or broken data |

## 7. SOLID and patterns
- **S:** the handler only runs the use case; validation, slot maths, SQL error mapping and HTTP mapping each live in
  their own class.
- **O:** new allocation rules are new `IResourceAllocationStrategy` classes; the handler does not change.
- **L:** every strategy returns all valid pairs, only reordered; `FixedClock` replaces `SystemClock` in tests.
- **I:** small interfaces (`IAppointmentRepository`, `ITechnicianRepository`, `IServiceBayRepository`,
  `IIdempotencyStore`, `IUnitOfWork`, `IClock`, catalog repositories).
- **D:** the Application defines the interfaces, Infrastructure implements them, `Program.cs` wires them.

Patterns: Clean Architecture, Command/Handler, Repository, Unit of Work, Strategy, Factory Method, Value Object,
Result, Idempotent Receiver, Chain of Responsibility (middleware), Dependency Injection. Frontend: custom hooks
(container/presentational split) and an Adapter (`appointmentsApi.ts`).

## 8. Testing
| Level | What it proves |
|---|---|
| Domain | time ranges, the grid, appointment creation and cancel rules, opening hours and shifts |
| Application | allocation order, skipping unusable resources, next pair on a clash, suggestions, idempotency, vehicle rule |
| Architecture | the layer rules in section 3 |
| Integration | the full API on a real SQL Server: booking, cancel, errors, idempotency and concurrency |
| Concurrency | 20 parallel bookings of one slot → exactly 1 success; 10 parallel bookings of one car → exactly 1 |
| Frontend | form validation, dealership time display, error states, cancel confirmation |

## 9. Frontend
Two pages: **Book a service** and **My appointments**. All times are shown in the dealership's time zone. One
`Idempotency-Key` per form submit; "Try again" after a 503 or network error reuses it. A 409 `NO_AVAILABILITY` shows
the suggested times as buttons. Components only render; hooks load data; hooks call the typed API layer, never
`fetch` directly. The API base URL comes from `VITE_API_BASE_URL` (default `http://localhost:5080`).

## 10. Runtime
API on `http://localhost:5080` (container port 8080). Web on `http://localhost:5173` (dev) or
`http://localhost:3000` (Compose). SQL Server on 1433, with the SA password in `.env` (see `.env.example`).
Migrations and seed data run on API startup when `Database:MigrateOnStartup=true`.

**Observability.** JSON console logs outside Development (`Logging:Format` = `Json` | `Simple`). Every request gets a
correlation id: `X-Correlation-Id` is accepted when it is 1–100 characters of `[A-Za-z0-9._:-]`, otherwise a new
32-hex id is made. It is returned in the response header, added to a log scope with the trace id, tagged on the server
span, and added to every ProblemDetails as `correlationId`. OpenTelemetry (packages only in the Api project):
ASP.NET Core and SqlClient spans, a `Book appointment` span (ActivitySource `Scheduler`), built-in HTTP metrics, and
the `Scheduler` meter: `scheduler.bookings` and `scheduler.cancellations` (tag `outcome`), `scheduler.db.retries`,
`scheduler.booking.duration` (ms). Application reports through `IAppointmentTelemetry`, so it has no OpenTelemetry
dependency. Exporters: `Observability:Otlp:Endpoint` (empty = off) and `Observability:ConsoleExporter`. Log levels:
created, replayed, refusals and cancels at Information (with the code), a retry at Warning, `DATABASE_BUSY` at Error,
slot clashes at Debug. Never logged: email, phone, VIN, customer name.
