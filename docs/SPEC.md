# Unified Service Scheduler — Build Spec (source of truth for all agents)

This file is the contract every agent builds against. It restates the approved build plan
(`docs/build-plan-original.md`, flattened export of the plan doc) plus decisions the owner made.
**If something is not covered here, do not guess: stop and report the question back to the orchestrator.**

## 0. Owner decisions
- No login. One fixed demo customer (Id = 1).
- The system ALWAYS assigns the technician and bay. The customer never picks one.
- Full plan, all 7 phases.
- Where the plan and the UI design differ, **the plan wins**. The design (`docs/design/*.dc.html`) is used only for
  look and styling (colours, fonts, spacing, card style, header). Example: vehicle is a **dropdown** (plan), the
  booking success is a **confirmation card on the booking page** (plan), the date is a **date picker** (plan).
- Plain, simple English in docs, comments and UI copy.

## 1. Goal
A customer picks a vehicle, a service type, a dealership and a start time. The system checks that one qualified
technician and one free service bay are available for the WHOLE duration, and saves an Appointment linking customer,
vehicle, technician and bay. Double booking must be impossible even under concurrent requests: the database blocks it
with unique slot rows on a 15-minute grid.

Out of scope (README only): login, notifications, rescheduling, multi-technician jobs, payment.

## 2. Tech stack
| Part | Choice |
|---|---|
| API | ASP.NET Core 8 Web API with **controllers** |
| Data access | EF Core 8 (SqlServer provider), migrations |
| Database | SQL Server 2022 in Docker (`mcr.microsoft.com/mssql/server:2022-latest`) |
| Validation | FluentValidation |
| Backend tests | xUnit, FluentAssertions, Testcontainers (MsSql), WebApplicationFactory, NetArchTest.Rules |
| Frontend | React 18 + TypeScript + Vite |
| Server state | TanStack Query |
| Forms | React Hook Form + Zod |
| Frontend tests | Vitest + React Testing Library |
| Run everything | Docker Compose (db + api + web) |
| Docs | Swagger (Swashbuckle) |

No MediatR. No generic `Repository<T>`. No AutoMapper (map by hand).

## 3. Repository layout
```
/                       README.md, docker-compose.yml, .gitignore, .editorconfig
backend/
  Scheduler.sln
  src/
    Scheduler.Domain/          Entities/, ValueObjects/ (TimeRange, SlotGrid), Enums/, Errors/
    Scheduler.Application/     Abstractions/ (IAppointmentRepository, ITechnicianRepository, IServiceBayRepository,
                               IIdempotencyStore, IUnitOfWork, IClock, ...), Appointments/Book, Appointments/Cancel,
                               Appointments/Queries (GetAvailability, GetAppointments, GetAppointmentById),
                               Catalog/Queries (dealerships, service types, vehicles), Allocation/, Common/ (Result<T>, Error)
    Scheduler.Infrastructure/  Persistence/ (SchedulerDbContext, Configurations/, Migrations/), Repositories/, Seeding/, Time/SystemClock
    Scheduler.Api/             Controllers/, Middleware/ExceptionHandlingMiddleware, ProblemDetails mapping, Program.cs, Dockerfile
  tests/
    Scheduler.Domain.Tests/
    Scheduler.Application.Tests/
    Scheduler.ArchitectureTests/
    Scheduler.IntegrationTests/      (real SQL Server via Testcontainers)
frontend/
  src/api/ (apiClient.ts, appointmentsApi.ts, types.ts)
  src/features/booking/ (BookingPage, BookingForm, TimeSlotPicker, BookingConfirmation, useAvailability, useBookAppointment)
  src/features/appointments/ (AppointmentsPage, AppointmentCard, useAppointments, useCancelAppointment)
  src/components/ (Button, Select, Alert, Spinner, ...)  src/lib/ (date helpers, formatters)  App.tsx, main.tsx
docs/  SPEC.md (this file), design/, build-plan-original.md
```
Layer rules: Domain → nothing. Application → Domain. Infrastructure → Application, Domain. Api → Application,
Infrastructure (DI wiring only). Application handlers never see `DbContext`.

## 4. Domain model and database (10 tables)
All times stored in **UTC** (`datetime2`). Dealership time zone (IANA id, e.g. `Asia/Ho_Chi_Minh`) is used only to
show times and to check opening hours / shifts.

| Table | Columns | Notes |
|---|---|---|
| Customers | Id, Name, Email, Phone | |
| Vehicles | Id, CustomerId, Vin, Make, Model, Year | belongs to one customer |
| Dealerships | Id, Name, TimeZoneId, OpenTime, CloseTime, IsActive | Open/Close are local `time` |
| ServiceTypes | Id, DealershipId, Name, DurationMinutes, RequiredSkill, MinimumTier, RequiredBayType, IsActive | duration multiple of 15 (round up to next 15 if not) |
| Technicians | Id, DealershipId, Name, ShiftStart, ShiftEnd, IsActive | shift = local `time`, same every day |
| TechnicianSkills | TechnicianId, Skill, Tier | PK (TechnicianId, Skill) |
| ServiceBays | Id, DealershipId, Name, BayType, IsActive | |
| Appointments | Id, CustomerId, VehicleId, DealershipId, ServiceTypeId, TechnicianId, ServiceBayId, StartUtc, EndUtc, Status, CreatedUtc, DurationSnapshot, RequiredSkillSnapshot, RequiredBayTypeSnapshot, RowVersion | snapshots = service rules at booking time; RowVersion = `rowversion` concurrency token |
| ResourceSlots | Id, ResourceType, ResourceId, SlotStartUtc, AppointmentId | **UNIQUE (ResourceType, ResourceId, SlotStartUtc)** |
| IdempotencyKeys | Key (PK, nvarchar(100)), RequestHash, AppointmentId, ResponseJson, CreatedUtc | saved in SAME transaction as the appointment |

Key constraint:
```sql
CREATE UNIQUE INDEX UX_ResourceSlots_Resource_Slot ON ResourceSlots (ResourceType, ResourceId, SlotStartUtc);
```

Enums (stored as strings, `HasConversion<string>()`, max length 30):
- `Skill`: General, Brakes, Tyres, EV, Diagnostics
- `SkillTier` (ordered, also store as string but compare by enum order): Trainee < Qualified < Senior < Master
- `BayType`: Standard, Lift, EV
- `AppointmentStatus`: Confirmed, Cancelled, Completed
- `ResourceType`: Technician, Bay

Rules:
- Start time must be on the 15-minute grid (:00/:15/:30/:45, seconds = 0) and in the future (via `IClock`).
- A 60-min job creates 4 slot rows for the technician + 4 for the bay, same transaction as the Appointment.
- Cancel: delete the appointment's ResourceSlots rows; the Appointment row stays with Status = Cancelled.
  Only `Confirmed` appointments can be cancelled.
- A bay "matches" when `BayType == RequiredBayType` (exact match).
- A technician qualifies when they have `RequiredSkill` with tier >= `MinimumTier`.
- The whole range must be inside dealership opening hours AND inside the technician's shift (local time, same day).

### Seed data (deterministic Ids, idempotent seeding)
Time zone for both dealerships: `Asia/Ho_Chi_Minh`.
- Dealership 1 "District 1 Service Centre", 08:00–17:00. Dealership 2 "Thu Duc Service Centre", 07:30–18:00.
- Customer 1 "Demo Customer", demo@example.com, +84 900 000 000.
- Vehicles (customer 1): 1 = Toyota Vios 2021, VIN `JTDBT923X71000001`; 2 = Honda CR-V 2023, VIN `7FARW2H58PE000002`.
- Service types:
  | Id | Dealer | Name | Min | Skill | MinTier | Bay |
  |---|---|---|---|---|---|---|
  | 1 | 1 | Oil change | 60 | General | Trainee | Standard |
  | 2 | 1 | Brake inspection | 90 | Brakes | Qualified | Lift |
  | 3 | 1 | Tyre rotation | 30 | Tyres | Trainee | Lift |
  | 4 | 1 | EV battery check | 60 | EV | Senior | EV |
  | 5 | 2 | Oil change | 60 | General | Trainee | Standard |
- Technicians (shift local time):
  | Id | Dealer | Name | Shift | Active | Skills |
  |---|---|---|---|---|---|
  | 1 | 1 | Nam | 08:00–17:00 | yes | General Master, Brakes Senior, Tyres Senior |
  | 2 | 1 | Lan | 08:00–17:00 | yes | General Qualified, Tyres Qualified, EV Senior |
  | 3 | 1 | Minh | 12:00–17:00 | yes | General Trainee, Brakes Qualified |
  | 4 | 2 | Hoa | 07:30–18:00 | yes | General Senior |
  | 5 | 1 | Tuan | 08:00–17:00 | **no** | General Master, Brakes Master, EV Master |
- Bays:
  | Id | Dealer | Name | Type | Active |
  |---|---|---|---|---|
  | 1 | 1 | Bay 1 | Standard | yes |
  | 2 | 1 | Bay 2 | Lift | yes |
  | 3 | 1 | Bay 3 | EV | yes |
  | 4 | 2 | Bay A | Standard | yes |
  | 5 | 1 | Bay 4 | Lift | **no** |

## 5. Booking flow (POST /api/appointments)
1. Validate (FluentValidation): required fields, startTime on grid, in the future. Vehicle belongs to customer,
   service type exists, is active and belongs to the dealership → otherwise 404 / 422 as per error table.
2. Idempotency: SHA-256 of a canonical form of the request body. Same key + same hash → return the saved response
   (same status 201 + same body, no new booking). Same key + different hash → 409 `IDEMPOTENCY_CONFLICT`.
3. Range = [start, start + duration) (half-open). Must be inside opening hours → else 422 `BOOKING_RULE_VIOLATION`.
4. Candidates (fast filter, may be stale): active qualified technicians at the dealership whose shift covers the range
   and with no slot rows in range; active bays of the required type with no slot rows in range.
5. Order by `IResourceAllocationStrategy` (default `LeastQualifiedFirstStrategy`: technician with the LOWEST tier in the
   required skill first, then fewest appointments that local day, then Id; bays: Id order — all matching bays are
   equally "simple" since match is exact).
6. For each pair: insert Appointment (+ snapshots) + all ResourceSlots + IdempotencyKey in ONE transaction.
7. SQL error 2601/2627 on the slot index → `SlotTaken`: roll back, clear change tracker, try next pair.
   (A duplicate on the IdempotencyKeys PK means a concurrent request with the same key: re-read the key and apply step 2.)
8. Success → 201 Created, `Location: /api/appointments/{id}`, body = AppointmentDto.
9. No pair works → 409 `NO_AVAILABILITY` with up to 3 nearby free start times (same day, closest to requested, ascending).
10. Transient SQL errors (deadlock 1205, timeouts): retry the whole attempt once; still failing → 503 `DATABASE_BUSY`.

`GET availability` returns every grid start time in that local day where at least one valid (technician, bay) pair is
free for the full duration and the start is in the future. It reserves nothing.

## 6. API contract (JSON camelCase, enums as strings)
Times in responses: UTC ISO-8601 with `Z` (e.g. `"2026-11-10T03:30:00Z"`). `startTime` in the booking request:
ISO-8601 **with offset** (e.g. `"2026-11-10T10:30:00+07:00"`); reject without offset (400).

| Method | Path | Response |
|---|---|---|
| GET | /api/dealerships | 200 `DealershipDto[]` |
| GET | /api/service-types?dealershipId={id} | 200 `ServiceTypeDto[]` (active only; filter optional) |
| GET | /api/customers/{id}/vehicles | 200 `VehicleDto[]`, 404 |
| GET | /api/dealerships/{id}/availability?serviceTypeId=&date=YYYY-MM-DD | 200 `AvailabilityDto`, 400, 404 |
| POST | /api/appointments (header `Idempotency-Key`, required, max 100 chars) | 201 `AppointmentDto`, 400, 404, 409, 422, 503 |
| GET | /api/appointments?customerId={id} | 200 `AppointmentDto[]`, newest first (StartUtc desc) |
| GET | /api/appointments/{id} | 200 `AppointmentDto`, 404 |
| POST | /api/appointments/{id}/cancel | 200 `AppointmentDto`, 404, 409 (`APPOINTMENT_NOT_CANCELLABLE`) |
| GET | /health/live | 200 |
| GET | /health/ready | 200, 503 (DB check) |

`date` in availability is the dealership-local calendar date.

```ts
DealershipDto   { id: number; name: string; timeZoneId: string; openTime: "HH:mm"; closeTime: "HH:mm" }
ServiceTypeDto  { id: number; dealershipId: number; name: string; durationMinutes: number;
                  requiredSkill: string; minimumTier: string; requiredBayType: string }
VehicleDto      { id: number; customerId: number; vin: string; make: string; model: string; year: number }
AvailabilityDto { dealershipId: number; serviceTypeId: number; date: "YYYY-MM-DD"; timeZoneId: string;
                  durationMinutes: number; slots: { startUtc: string; endUtc: string }[] }
BookAppointmentRequest { customerId: number; vehicleId: number; dealershipId: number;
                         serviceTypeId: number; startTime: string /* with offset */ }
AppointmentDto  { id: number; status: "Confirmed"|"Cancelled"|"Completed"; startUtc: string; endUtc: string;
                  createdUtc: string; customerId: number;
                  vehicle:     { id: number; make: string; model: string; year: number; vin: string };
                  dealership:  { id: number; name: string; timeZoneId: string };
                  serviceType: { id: number; name: string; durationMinutes: number };
                  technician:  { id: number; name: string };
                  serviceBay:  { id: number; name: string; bayType: string } }
```

### Errors: RFC 7807 ProblemDetails (`application/problem+json`)
Shape: `{ type, title, status, detail, code, traceId, errors?: {field: string[]}, suggestedStartTimes?: string[] }`
(`code`, `errors`, `suggestedStartTimes` are top-level extension members; `suggestedStartTimes` are UTC ISO strings.)

| code | HTTP | Retry? | Meaning |
|---|---|---|---|
| INVALID_REQUEST | 400 | No | missing field, bad format, not on 15-min grid, in the past, missing Idempotency-Key |
| NOT_FOUND | 404 | No | unknown customer, vehicle, dealership, service type or appointment |
| BOOKING_RULE_VIOLATION | 422 | No | vehicle not owned by customer, inactive service, service not offered at dealership, outside opening hours |
| NO_AVAILABILITY | 409 | No | valid request but no technician + bay pair free; includes suggestedStartTimes |
| IDEMPOTENCY_CONFLICT | 409 | No | same key, different body |
| APPOINTMENT_NOT_CANCELLABLE | 409 | No | cancel on an appointment that is not Confirmed (added by orchestrator; not in the original code table) |
| DATABASE_BUSY | 503 | Yes | DB too busy after one retry |
| INTERNAL_ERROR | 500 | — | unexpected bug (middleware) |

## 7. SOLID + patterns (each must be visible in code)
S: handler only runs the use case; validator separate; slot math in SlotGrid; SQL error mapping in the repository;
controllers only translate HTTP ↔ commands. O: new allocation rules = new `IResourceAllocationStrategy`.
L: every strategy returns an ordered list and never drops valid pairs; FakeClock swaps for SystemClock.
I: small interfaces (IAppointmentRepository, ITechnicianRepository, IServiceBayRepository, IIdempotencyStore, IClock,
IUnitOfWork). D: Application defines interfaces, Infrastructure implements them, wiring in Program.cs.

Patterns: Clean Architecture; Command/Handler (light CQRS, hand-written); Repository; Unit of Work; Strategy;
Factory Method (`Appointment.Create`, `ResourceSlot.For`); Value Object (`TimeRange`, `SlotGrid`); Result pattern
(`Result<T>`); Idempotent Receiver; Chain of Responsibility (middleware); DI.
Frontend: custom hooks (container/presentational), Adapter (`appointmentsApi.ts` converts JSON → typed UI objects).

## 8. Testing
| Level | What |
|---|---|
| Domain unit | TimeRange overlap (touching, inside, crossing), SlotGrid.Split, grid check, Appointment.Create rules |
| Application unit | least-qualified tech chosen; inactive/unqualified skipped; next pair tried on SlotTaken; suggestions on 409 |
| Architecture | Domain + Application never reference EF Core; Domain references nothing else in the solution |
| Integration | full API on real SQL Server: book, cancel then rebook, outside opening hours, error codes/status codes |
| Idempotency | same key + same body → same appointment; same key + different body → IDEMPOTENCY_CONFLICT |
| Concurrency | 20 parallel POSTs, same slot, 1 tech + 1 bay → exactly 1×201, 19×409, 1 appointment in DB |
| Frontend | form validation, 409 shows suggested times, cancel button |

## 9. Frontend (plan wins over design for behaviour; design for look)
Header (design): dark bar `#16202A`, clock logo, "Service Scheduler", nav "Book a service" / "My appointments",
"Demo customer" on the right. Background `#F4F3EF`, cards white with `#DAD7CF` border, radius 12px, accent `#C2410C`
(hover `#9A3412`), muted text `#5B6570`. Fonts: Archivo (headings 600/700), Inter (body). Touch targets ≥ 44px.

Book a service page:
- Vehicle: dropdown of demo customer's vehicles. Dealership: dropdown. Service type: shows duration; filtered by dealership.
- Date: date picker (today or later). Time: list of free start times from the availability endpoint, shown in dealership local time.
- Book button disabled while saving; ONE Idempotency-Key (`crypto.randomUUID()`) per form submit (a retry of the same submit reuses it).
- Success: confirmation card (service, local date + start–end, vehicle, dealership, technician, bay, reference, status).
- 409 NO_AVAILABILITY: "This time was just taken" + suggested times as buttons that pick that time.
- 422/400: show the message. 503: "We're busy right now… try again" with a Try again button (same key).
My appointments page: cards newest first with status badge; Cancel on Confirmed ones; list refreshes after cancel.
Code rules: components only render; data in hooks; hooks call `appointmentsApi.ts` (never fetch directly); all API
types in `api/types.ts` matching section 6. API base URL from `VITE_API_BASE_URL` (default `http://localhost:5080`).

## 10. Ports / runtime
API listens on `http://localhost:5080` (container port 8080 mapped to 5080). Web on `http://localhost:5173` (dev) /
`http://localhost:3000` (compose, nginx). CORS allows the web origins. DB on 1433, SA password via `.env`
(`MSSQL_SA_PASSWORD`, sample in `.env.example`). Migrations + seed run on API startup (dev/compose) when
`Database:MigrateOnStartup=true`.
