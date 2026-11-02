// A small in-memory copy of the Scheduler API for the browser preview.
// It follows the real backend rules (SPEC sections 4-6 and 0b/0c; Scheduler.Application:
// BookAppointmentHandler, DaySchedule, ScarcityAwareStrategy, CancelAppointmentHandler) and answers with
// the same JSON and ProblemDetails (same codes, titles and messages).
// Times are numbers (UTC milliseconds) inside this module and ISO strings with "Z" on the wire.

import {
  bays,
  customers,
  dealerships,
  exampleAppointments,
  serviceTypes,
  technicians,
  TIERS,
  vehicles,
  ZONE_OFFSET_MINUTES,
  type SeedBay,
  type SeedDealership,
  type SeedServiceType,
  type SeedTechnician,
  type Status,
} from './seed';

const SLOT_MS = 15 * 60_000;
const DAY_MS = 24 * 60 * 60_000;
const MAX_SUGGESTIONS = 3;
const MAX_IDEMPOTENCY_KEY_LENGTH = 100;
const MIN_YEAR = 2000;
const MAX_YEAR = 9998;
const MAX_RANGE_DAYS = 42;

/** Availability contract v2, section 1. First match wins, in this order. */
export type StartTimeStatus = 'Past' | 'VehicleBooked' | 'Booked' | 'Available';
/** Availability contract v2, section 2. */
export type DayStatus = 'Past' | 'FullyBooked' | 'Available';

// ---------------------------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------------------------

export interface StoredAppointment {
  id: number;
  status: Status;
  customerId: number;
  vehicleId: number;
  dealershipId: number;
  serviceTypeId: number;
  technicianId: number;
  serviceBayId: number;
  startUtc: number;
  endUtc: number;
  createdUtc: number;
  durationSnapshot: number;
}

interface IdempotencyRecord {
  requestHash: string;
  appointmentId: number;
  response: unknown;
}

export interface MockState {
  version: 1;
  nextId: number;
  appointments: StoredAppointment[];
  idempotency: Record<string, IdempotencyRecord>;
}

export interface MockRequest {
  method: string;
  /** Path and query, e.g. "/api/appointments?customerId=1". */
  url: string;
  /** Header names in lower case. */
  headers: Record<string, string>;
  body: string | null;
}

export interface MockResponse {
  status: number;
  headers: Record<string, string>;
  body: unknown;
}

export interface MockBackendOptions {
  /** Current time (UTC ms). Defaults to Date.now. */
  now?: () => number;
  /** Starting state. Defaults to a fresh seed. */
  state?: MockState;
  /** Called after every change, e.g. to save the state. */
  onChange?: (state: MockState) => void;
}

// ---------------------------------------------------------------------------------------------
// Time helpers (fixed offsets; the seeded zone has no daylight saving time)
// ---------------------------------------------------------------------------------------------

function offsetMs(timeZoneId: string): number {
  const minutes = ZONE_OFFSET_MINUTES[timeZoneId];
  if (minutes === undefined) throw new Error(`Unknown time zone '${timeZoneId}'.`);
  return minutes * 60_000;
}

/** Local calendar date ("YYYY-MM-DD") of a UTC instant. */
export function localDateOf(utc: number, timeZoneId: string): string {
  return new Date(utc + offsetMs(timeZoneId)).toISOString().slice(0, 10);
}

/** UTC instant of a local date + local "HH:mm". */
export function localToUtc(localDate: string, hhmm: string, timeZoneId: string): number {
  const [y, m, d] = localDate.split('-').map(Number) as [number, number, number];
  const [hh, mm] = hhmm.split(':').map(Number) as [number, number];
  return Date.UTC(y, m - 1, d, hh, mm) - offsetMs(timeZoneId);
}

/** Adds whole days to a "YYYY-MM-DD" date. */
export function addDays(localDate: string, days: number): string {
  const [y, m, d] = localDate.split('-').map(Number) as [number, number, number];
  return new Date(Date.UTC(y, m - 1, d) + days * DAY_MS).toISOString().slice(0, 10);
}

export function formatUtc(utc: number): string {
  return new Date(utc).toISOString().replace(/\.\d{3}Z$/, 'Z');
}

const isOnGrid = (utc: number) => utc % SLOT_MS === 0;

interface Range {
  start: number;
  end: number;
}

const overlaps = (a: Range, b: Range) => a.start < b.end && b.start < a.end;

/** A daily local window [open, close] on the local day of the range start covers the whole range. */
function windowCovers(open: string, close: string, range: Range, timeZoneId: string): boolean {
  const day = localDateOf(range.start, timeZoneId);
  return localToUtc(day, open, timeZoneId) <= range.start && range.end <= localToUtc(day, close, timeZoneId);
}

/**
 * Parses an ISO-8601 date-time that MUST have an offset (same regex as IsoDateTime.cs).
 * Returns UTC ms and whether the fraction of a second is zero, or null when the format is wrong.
 */
export function parseWithOffset(value: string): { utc: number; wholeSeconds: boolean } | null {
  const match =
    /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?([Zz]|([+-])(\d{2}):(\d{2}))$/.exec(value);
  if (!match) return null;
  const [, y, mo, d, h, mi, s = '0', fraction = '', zone, sign, oh, om] = match;
  const year = Number(y);
  const month = Number(mo);
  const day = Number(d);
  const hour = Number(h);
  const minute = Number(mi);
  const second = Number(s);
  if (month < 1 || month > 12 || hour > 23 || minute > 59 || second > 59) return null;
  const local = Date.UTC(year, month - 1, day, hour, minute, second);
  const check = new Date(local);
  if (check.getUTCFullYear() !== year || check.getUTCMonth() !== month - 1 || check.getUTCDate() !== day) return null;
  let offsetMinutes = 0;
  if (zone !== 'Z' && zone !== 'z') {
    const ohN = Number(oh);
    const omN = Number(om);
    if (ohN > 14 || omN > 59) return null;
    offsetMinutes = (sign === '-' ? -1 : 1) * (ohN * 60 + omN);
  }
  const ms = Number(fraction.padEnd(3, '0').slice(0, 3));
  return { utc: local + ms - offsetMinutes * 60_000, wholeSeconds: /^0*$/.test(fraction) };
}

function isIsoDate(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return false;
  const [y, m, d] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(Date.UTC(y, m - 1, d));
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d;
}

const yearInRange = (year: number) => year >= MIN_YEAR && year <= MAX_YEAR;

/** The FluentValidation messages for a required "YYYY-MM-DD" query value, or null when it is fine. */
function dateFailure(field: string, value: string): string | null {
  if (value === '') return `${field} is required.`;
  if (!isIsoDate(value)) return `${field} must be in the format YYYY-MM-DD.`;
  if (!yearInRange(Number(value.slice(0, 4)))) return `${field} must be between the years ${MIN_YEAR} and ${MAX_YEAR}.`;
  return null;
}

/**
 * Optional `vehicleId` query value: absent = ignore the car; otherwise a positive 32-bit integer. Like the real API,
 * a present but empty value ("?vehicleId=") is 400, not "absent".
 */
function readVehicleId(query: URLSearchParams, failures: [string, string][]): number | null {
  const raw = query.get('vehicleId');
  if (raw === null) return null;
  if (!/^\d+$/.test(raw) || Number(raw) <= 0 || Number(raw) > 2_147_483_647) {
    failures.push(['vehicleId', 'vehicleId must be a positive number.']);
    return null;
  }
  return Number(raw);
}

// ---------------------------------------------------------------------------------------------
// Errors (same codes, titles and messages as the real API: ApiProblems.cs, Error.cs, DomainErrors.cs)
// ---------------------------------------------------------------------------------------------

type ErrorCode =
  | 'INVALID_REQUEST'
  | 'NOT_FOUND'
  | 'BOOKING_RULE_VIOLATION'
  | 'NO_AVAILABILITY'
  | 'IDEMPOTENCY_CONFLICT'
  | 'VEHICLE_ALREADY_BOOKED'
  | 'APPOINTMENT_NOT_CANCELLABLE'
  | 'DATABASE_BUSY'
  | 'INTERNAL_ERROR';

const PROBLEM: Record<ErrorCode, { status: number; title: string }> = {
  INVALID_REQUEST: { status: 400, title: 'Invalid request' },
  NOT_FOUND: { status: 404, title: 'Not found' },
  BOOKING_RULE_VIOLATION: { status: 422, title: 'Booking rule violation' },
  NO_AVAILABILITY: { status: 409, title: 'No availability' },
  IDEMPOTENCY_CONFLICT: { status: 409, title: 'Idempotency conflict' },
  VEHICLE_ALREADY_BOOKED: { status: 409, title: 'Vehicle already booked' },
  APPOINTMENT_NOT_CANCELLABLE: { status: 409, title: 'Appointment cannot be cancelled' },
  DATABASE_BUSY: { status: 503, title: 'Database busy' },
  INTERNAL_ERROR: { status: 500, title: 'Internal error' },
};

const TYPE_BY_STATUS: Record<number, string> = {
  400: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
  404: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
  405: 'https://tools.ietf.org/html/rfc9110#section-15.5.6',
  409: 'https://tools.ietf.org/html/rfc9110#section-15.5.10',
  422: 'https://tools.ietf.org/html/rfc9110#section-15.5.21',
  500: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
  503: 'https://tools.ietf.org/html/rfc9110#section-15.6.4',
};

interface AppError {
  code: ErrorCode;
  message: string;
  errors?: Record<string, string[]>;
  suggestedStartTimes?: number[];
  /** Overrides the status (used for 405). */
  status?: number;
}

const notFound = (entity: string, id: number | string): AppError => ({
  code: 'NOT_FOUND',
  message: `${entity} ${id} was not found.`,
});
const rule = (message: string): AppError => ({ code: 'BOOKING_RULE_VIOLATION', message });

const MESSAGES = {
  vehicleNotOwned: 'The vehicle does not belong to the customer.',
  dealershipInactive: 'The dealership is not active.',
  serviceInactive: 'The service type is not active.',
  serviceNotOffered: 'The service type is not offered at this dealership.',
  outsideOpeningHours: 'The appointment must be inside the dealership opening hours.',
  startNotInFuture: 'startTime must be in the future.',
  noAvailability: 'No technician and bay are free for the whole time you picked.',
  idempotencyConflict: 'This Idempotency-Key was already used with a different request body.',
  vehicleAlreadyBooked: 'This vehicle already has a booking at that time.',
  alreadyStarted: 'This appointment has already started or passed.',
  unreadable: 'The request could not be read. Check the JSON body and the parameters.',
  manyErrors: 'The request is not valid. See the field errors.',
} as const;

/** Field errors -> INVALID_REQUEST, like ValidationErrors.ToError (one error: its message; more: a summary). */
function validationError(failures: [field: string, message: string][]): AppError {
  const errors: Record<string, string[]> = {};
  for (const [field, message] of failures) {
    const list = (errors[field] ??= []);
    if (!list.includes(message)) list.push(message);
  }
  return {
    code: 'INVALID_REQUEST',
    message: failures.length === 1 ? failures[0]![1] : MESSAGES.manyErrors,
    errors,
  };
}

// ---------------------------------------------------------------------------------------------
// Seed state
// ---------------------------------------------------------------------------------------------

export function createSeedState(nowUtc: number): MockState {
  const timeZoneId = dealerships[0]!.timeZoneId;
  const today = localDateOf(nowUtc, timeZoneId);
  const appointments = exampleAppointments.map((example, index): StoredAppointment => {
    const service = serviceTypes.find((s) => s.id === example.serviceTypeId)!;
    const start = localToUtc(addDays(today, example.dayOffset), example.localTime, timeZoneId);
    return {
      id: index + 1,
      status: example.status,
      customerId: 1,
      vehicleId: example.vehicleId,
      dealershipId: service.dealershipId,
      serviceTypeId: service.id,
      technicianId: example.technicianId,
      serviceBayId: example.serviceBayId,
      startUtc: start,
      endUtc: start + service.durationMinutes * 60_000,
      createdUtc: Math.min(nowUtc, start - 2 * DAY_MS),
      durationSnapshot: service.durationMinutes,
    };
  });
  return { version: 1, nextId: appointments.length + 1, appointments, idempotency: {} };
}

// ---------------------------------------------------------------------------------------------
// Resource rules
// ---------------------------------------------------------------------------------------------

const tierIndex = (tier: string | undefined) => (tier === undefined ? -1 : TIERS.indexOf(tier as (typeof TIERS)[number]));

function isQualified(technician: SeedTechnician, service: SeedServiceType): boolean {
  const tier = technician.skills[service.requiredSkill];
  return tier !== undefined && tierIndex(tier) >= tierIndex(service.minimumTier);
}

interface Pair {
  technician: SeedTechnician;
  bay: SeedBay;
}

/**
 * ScarcityAwareStrategy (SPEC 0c): technicians ordered by
 * (1) fewest other active services they are the ONLY active qualified technician for,
 * (2) fewest other active services they are qualified for, (3) lowest tier in the required skill,
 * (4) fewest Confirmed appointments that local day, (5) lowest id. Bays by id. Technician-major pairs.
 */
export function orderPairs(
  service: SeedServiceType,
  freeTechnicians: SeedTechnician[],
  freeBays: SeedBay[],
  confirmedCounts: ReadonlyMap<number, number>,
): Pair[] {
  const dealershipTechnicians = technicians.filter((t) => t.isActive && t.dealershipId === service.dealershipId);
  const otherServices = serviceTypes.filter(
    (s) => s.isActive && s.dealershipId === service.dealershipId && s.id !== service.id,
  );
  const qualifiedCount = new Map(
    otherServices.map((s) => [s.id, dealershipTechnicians.filter((t) => isQualified(t, s)).length]),
  );
  const soleCover = (t: SeedTechnician) =>
    otherServices.filter((s) => isQualified(t, s) && qualifiedCount.get(s.id) === 1).length;
  const otherQualified = (t: SeedTechnician) => otherServices.filter((s) => isQualified(t, s)).length;

  const keys = (t: SeedTechnician) => [
    isQualified(t, service) ? 0 : 1,
    soleCover(t),
    otherQualified(t),
    tierIndex(t.skills[service.requiredSkill]) < 0 ? Number.MAX_SAFE_INTEGER : tierIndex(t.skills[service.requiredSkill]),
    confirmedCounts.get(t.id) ?? 0,
    t.id,
  ];
  const sortedTechnicians = [...freeTechnicians].sort((a, b) => {
    const ka = keys(a);
    const kb = keys(b);
    for (let i = 0; i < ka.length; i++) if (ka[i] !== kb[i]) return ka[i]! - kb[i]!;
    return 0;
  });
  const sortedBays = [...freeBays].sort((a, b) => a.id - b.id);
  return sortedTechnicians.flatMap((technician) => sortedBays.map((bay) => ({ technician, bay })));
}

// ---------------------------------------------------------------------------------------------
// The API
// ---------------------------------------------------------------------------------------------

type Handler = (match: RegExpMatchArray, query: URLSearchParams, request: MockRequest) => MockResponse;

interface Route {
  pattern: RegExp;
  methods: Partial<Record<string, Handler>>;
}

export class MockSchedulerBackend {
  private state: MockState;
  private readonly now: () => number;
  private readonly onChange: ((state: MockState) => void) | undefined;
  private readonly routes: Route[];

  constructor(options: MockBackendOptions = {}) {
    this.now = options.now ?? (() => Date.now());
    this.state = options.state ?? createSeedState(this.now());
    this.onChange = options.onChange;
    this.routes = [
      { pattern: /^\/api\/dealerships$/, methods: { GET: () => this.ok(this.getDealerships()) } },
      { pattern: /^\/api\/dealerships\/(-?\d+)\/availability$/, methods: { GET: (m, q) => this.getAvailability(Number(m[1]), q) } },
      {
        pattern: /^\/api\/dealerships\/(-?\d+)\/availability\/days$/,
        methods: { GET: (m, q) => this.getAvailabilityDays(Number(m[1]), q) },
      },
      { pattern: /^\/api\/service-types$/, methods: { GET: (_m, q) => this.getServiceTypes(q) } },
      { pattern: /^\/api\/customers\/(-?\d+)\/vehicles$/, methods: { GET: (m) => this.getVehicles(Number(m[1])) } },
      {
        pattern: /^\/api\/appointments$/,
        methods: { GET: (_m, q) => this.getAppointments(q), POST: (_m, _q, r) => this.book(r) },
      },
      { pattern: /^\/api\/appointments\/(-?\d+)$/, methods: { GET: (m) => this.getAppointment(Number(m[1])) } },
      { pattern: /^\/api\/appointments\/(-?\d+)\/cancel$/, methods: { POST: (m) => this.cancel(Number(m[1])) } },
      { pattern: /^\/health\/(live|ready)$/, methods: { GET: () => this.ok({ status: 'Healthy' }) } },
    ];
  }

  /** The current state (for saving). */
  snapshot(): MockState {
    return this.state;
  }

  /** Back to the seed data, with the examples placed around today. */
  reset(): void {
    this.state = createSeedState(this.now());
    this.changed();
  }

  handle(request: MockRequest): MockResponse {
    const url = new URL(request.url, 'http://preview.local');
    const path = url.pathname.replace(/\/+$/, '') || '/';
    const method = request.method.toUpperCase();
    for (const route of this.routes) {
      const match = route.pattern.exec(path);
      if (!match) continue;
      const handler = route.methods[method];
      if (!handler) {
        return this.problem(path, { code: 'INVALID_REQUEST', status: 405, message: `${method} is not allowed here.` });
      }
      try {
        return handler(match, url.searchParams, { ...request, url: path });
      } catch {
        return this.problem(path, { code: 'INTERNAL_ERROR', message: 'An unexpected error occurred.' });
      }
    }
    return this.problem(path, { code: 'NOT_FOUND', message: `No resource at ${path}.` });
  }

  // --- catalog -----------------------------------------------------------------------------

  private getDealerships() {
    return dealerships
      .filter((d) => d.isActive)
      .map(({ id, name, timeZoneId, openTime, closeTime }) => ({ id, name, timeZoneId, openTime, closeTime }));
  }

  private getServiceTypes(query: URLSearchParams): MockResponse {
    const raw = query.get('dealershipId');
    let dealershipId: number | null = null;
    if (raw !== null && raw !== '') {
      if (!/^-?\d+$/.test(raw)) return this.problem('/api/service-types', this.unreadable('dealershipId'));
      dealershipId = Number(raw);
    }
    return this.ok(
      serviceTypes
        .filter((s) => s.isActive && (dealershipId === null || s.dealershipId === dealershipId))
        .map(({ id, dealershipId: d, name, durationMinutes, requiredSkill, minimumTier, requiredBayType }) => ({
          id,
          dealershipId: d,
          name,
          durationMinutes,
          requiredSkill,
          minimumTier,
          requiredBayType,
        })),
    );
  }

  private getVehicles(customerId: number): MockResponse {
    const path = `/api/customers/${customerId}/vehicles`;
    if (!customers.some((c) => c.id === customerId)) return this.problem(path, notFound('Customer', customerId));
    return this.ok(vehicles.filter((v) => v.customerId === customerId).sort((a, b) => a.id - b.id));
  }

  // --- availability ------------------------------------------------------------------------

  private getAvailability(dealershipId: number, query: URLSearchParams): MockResponse {
    const path = `/api/dealerships/${dealershipId}/availability`;
    const rawService = query.get('serviceTypeId');
    if (rawService !== null && rawService !== '' && !/^-?\d+$/.test(rawService)) {
      return this.problem(path, this.unreadable('serviceTypeId'));
    }
    const serviceTypeId = rawService ? Number(rawService) : 0;
    const date = query.get('date') ?? '';

    const failures: [string, string][] = [];
    if (dealershipId <= 0) failures.push(['dealershipId', 'dealershipId is required.']);
    if (serviceTypeId <= 0) failures.push(['serviceTypeId', 'serviceTypeId is required.']);
    const dateError = dateFailure('date', date);
    if (dateError) failures.push(['date', dateError]);
    const vehicleId = readVehicleId(query, failures);
    if (failures.length > 0) return this.problem(path, validationError(failures));

    const offer = this.availabilityOffer(dealershipId, serviceTypeId, vehicleId);
    if ('error' in offer) return this.problem(path, offer.error);
    const { dealership, service } = offer;

    const times = this.dayTimes(dealership, service, date, this.now(), vehicleId).map((t) => ({
      startUtc: formatUtc(t.start),
      endUtc: formatUtc(t.end),
      status: t.status,
    }));
    return this.ok({
      dealershipId: dealership.id,
      serviceTypeId: service.id,
      date,
      timeZoneId: dealership.timeZoneId,
      durationMinutes: service.durationMinutes,
      slots: times.filter((t) => t.status === 'Available').map(({ startUtc, endUtc }) => ({ startUtc, endUtc })),
      times,
    });
  }

  private getAvailabilityDays(dealershipId: number, query: URLSearchParams): MockResponse {
    const path = `/api/dealerships/${dealershipId}/availability/days`;
    const rawService = query.get('serviceTypeId');
    if (rawService !== null && rawService !== '' && !/^-?\d+$/.test(rawService)) {
      return this.problem(path, this.unreadable('serviceTypeId'));
    }
    const serviceTypeId = rawService ? Number(rawService) : 0;
    const from = query.get('from') ?? '';
    const to = query.get('to') ?? '';

    const failures: [string, string][] = [];
    if (dealershipId <= 0) failures.push(['dealershipId', 'dealershipId is required.']);
    if (serviceTypeId <= 0) failures.push(['serviceTypeId', 'serviceTypeId is required.']);
    const fromError = dateFailure('from', from);
    if (fromError) failures.push(['from', fromError]);
    const toError = dateFailure('to', to);
    if (toError) failures.push(['to', toError]);
    if (!fromError && !toError) {
      if (to < from) failures.push(['to', 'to must be on or after from.']);
      else if ((Date.parse(to) - Date.parse(from)) / DAY_MS + 1 > MAX_RANGE_DAYS) {
        failures.push(['to', `The range can be at most ${MAX_RANGE_DAYS} days.`]);
      }
    }
    const vehicleId = readVehicleId(query, failures);
    if (failures.length > 0) return this.problem(path, validationError(failures));

    const offer = this.availabilityOffer(dealershipId, serviceTypeId, vehicleId);
    if ('error' in offer) return this.problem(path, offer.error);
    const { dealership, service } = offer;

    const now = this.now();
    const today = localDateOf(now, dealership.timeZoneId);
    const days: { date: string; availableCount: number; status: DayStatus }[] = [];
    for (let date = from; date <= to; date = addDays(date, 1)) {
      if (date < today) {
        days.push({ date, availableCount: 0, status: 'Past' });
        continue;
      }
      const availableCount = this.dayTimes(dealership, service, date, now, vehicleId).filter(
        (t) => t.status === 'Available',
      ).length;
      days.push({ date, availableCount, status: availableCount === 0 ? 'FullyBooked' : 'Available' });
    }
    return this.ok({
      dealershipId: dealership.id,
      serviceTypeId: service.id,
      from,
      to,
      timeZoneId: dealership.timeZoneId,
      durationMinutes: service.durationMinutes,
      days,
    });
  }

  /** The 404s and offer rules both availability endpoints share (the vehicle has no ownership check). */
  private availabilityOffer(
    dealershipId: number,
    serviceTypeId: number,
    vehicleId: number | null,
  ): { error: AppError } | { dealership: SeedDealership; service: SeedServiceType } {
    const dealership = dealerships.find((d) => d.id === dealershipId);
    if (!dealership) return { error: notFound('Dealership', dealershipId) };
    const service = serviceTypes.find((s) => s.id === serviceTypeId);
    if (!service) return { error: notFound('Service type', serviceTypeId) };
    if (vehicleId !== null && !vehicles.some((v) => v.id === vehicleId)) return { error: notFound('Vehicle', vehicleId) };
    const ruleError = this.offerRules(dealership, service);
    if (ruleError) return { error: ruleError };
    return { dealership, service };
  }

  private offerRules(dealership: SeedDealership, service: SeedServiceType): AppError | null {
    if (!dealership.isActive) return rule(MESSAGES.dealershipInactive);
    if (!service.isActive) return rule(MESSAGES.serviceInactive);
    if (service.dealershipId !== dealership.id) return rule(MESSAGES.serviceNotOffered);
    return null;
  }

  /** Slot rows: every appointment that is not Cancelled holds its technician, bay and vehicle slots. */
  private holding(): StoredAppointment[] {
    return this.state.appointments.filter((a) => a.status !== 'Cancelled');
  }

  private isFree(kind: 'technicianId' | 'serviceBayId' | 'vehicleId', id: number, range: Range): boolean {
    return !this.holding().some((a) => a[kind] === id && overlaps(range, { start: a.startUtc, end: a.endUtc }));
  }

  private openingHours(dealership: SeedDealership, localDate: string): Range {
    return {
      start: localToUtc(localDate, dealership.openTime, dealership.timeZoneId),
      end: localToUtc(localDate, dealership.closeTime, dealership.timeZoneId),
    };
  }

  /** DaySchedule.FindPairs: free (technician, bay) pairs for the whole range, in strategy order. */
  findPairs(dealership: SeedDealership, service: SeedServiceType, localDate: string, range: Range): Pair[] {
    const hours = this.openingHours(dealership, localDate);
    if (range.start < hours.start || range.end > hours.end || !isOnGrid(range.start) || !isOnGrid(range.end)) return [];

    const freeTechnicians = technicians.filter(
      (t) =>
        t.isActive &&
        t.dealershipId === dealership.id &&
        isQualified(t, service) &&
        windowCovers(t.shiftStart, t.shiftEnd, range, dealership.timeZoneId) &&
        this.isFree('technicianId', t.id, range),
    );
    if (freeTechnicians.length === 0) return [];
    const freeBays = bays.filter(
      (b) =>
        b.isActive &&
        b.dealershipId === dealership.id &&
        b.bayType === service.requiredBayType &&
        this.isFree('serviceBayId', b.id, range),
    );
    if (freeBays.length === 0) return [];

    const dayRange = {
      start: localToUtc(localDate, '00:00', dealership.timeZoneId),
      end: localToUtc(addDays(localDate, 1), '00:00', dealership.timeZoneId),
    };
    const counts = new Map<number, number>();
    for (const a of this.state.appointments) {
      if (a.status === 'Confirmed' && a.startUtc >= dayRange.start && a.startUtc < dayRange.end) {
        counts.set(a.technicianId, (counts.get(a.technicianId) ?? 0) + 1);
      }
    }
    return orderPairs(service, freeTechnicians, freeBays, counts);
  }

  /** Every 15-minute grid start in opening hours where start + duration <= closing time (the starts FreeRanges walks). */
  private gridRanges(dealership: SeedDealership, service: SeedServiceType, localDate: string): Range[] {
    const hours = this.openingHours(dealership, localDate);
    const duration = service.durationMinutes * 60_000;
    const result: Range[] = [];
    for (let start = Math.ceil(hours.start / SLOT_MS) * SLOT_MS; start + duration <= hours.end; start += SLOT_MS) {
      result.push({ start, end: start + duration });
    }
    return result;
  }

  /**
   * Contract v2 section 1: every grid start with its status, first match wins:
   * Past (start <= now), VehicleBooked (the car holds a slot in the range, same check as booking),
   * Booked (no free pair for the whole range, same FindPairs as booking), Available.
   */
  dayTimes(
    dealership: SeedDealership,
    service: SeedServiceType,
    localDate: string,
    nowUtc: number,
    vehicleId: number | null,
  ): (Range & { status: StartTimeStatus })[] {
    return this.gridRanges(dealership, service, localDate).map((range) => {
      let status: StartTimeStatus = 'Available';
      if (range.start <= nowUtc) status = 'Past';
      else if (vehicleId !== null && !this.isFree('vehicleId', vehicleId, range)) status = 'VehicleBooked';
      else if (this.findPairs(dealership, service, localDate, range).length === 0) status = 'Booked';
      return { ...range, status };
    });
  }

  /** DaySchedule.FreeRanges: grid starts in opening hours, after `afterUtc`, with at least one free pair. */
  freeRanges(dealership: SeedDealership, service: SeedServiceType, localDate: string, afterUtc: number): Range[] {
    return this.dayTimes(dealership, service, localDate, afterUtc, null)
      .filter((t) => t.status === 'Available')
      .map(({ start, end }) => ({ start, end }));
  }

  /** DaySchedule.SuggestStartTimes: nearest free starts (earlier wins a tie), skipping car-busy times, ascending. */
  private suggest(
    dealership: SeedDealership,
    service: SeedServiceType,
    localDate: string,
    requested: number,
    vehicleId: number,
  ): number[] {
    return this.freeRanges(dealership, service, localDate, this.now())
      .filter((range) => this.isFree('vehicleId', vehicleId, range))
      .map((range) => range.start)
      .filter((start) => start !== requested)
      .sort((a, b) => Math.abs(a - requested) - Math.abs(b - requested) || a - b)
      .slice(0, MAX_SUGGESTIONS)
      .sort((a, b) => a - b);
  }

  // --- booking -----------------------------------------------------------------------------

  private book(request: MockRequest): MockResponse {
    const path = '/api/appointments';
    const result = this.tryBook(request);
    if ('error' in result) return this.problem(path, result.error);
    return {
      status: 201,
      headers: { 'Content-Type': 'application/json; charset=utf-8', Location: `/api/appointments/${result.id}` },
      body: result.dto,
    };
  }

  private tryBook(request: MockRequest): { error: AppError } | { id: number; dto: unknown } {
    // Model binding: the body must be a JSON object with numbers / a string.
    let body: Record<string, unknown>;
    try {
      const parsed: unknown = request.body ? JSON.parse(request.body) : null;
      if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return { error: this.unreadable('body') };
      body = parsed as Record<string, unknown>;
    } catch {
      return { error: this.unreadable('body') };
    }
    const intField = (name: string): number | AppError => {
      const value = body[name];
      if (value === undefined || value === null) return 0;
      return typeof value === 'number' && Number.isInteger(value) ? value : this.unreadable(name);
    };
    const ids = ['customerId', 'vehicleId', 'dealershipId', 'serviceTypeId'].map(intField);
    const badField = ids.find((v): v is AppError => typeof v !== 'number');
    if (badField) return { error: badField };
    const [customerId, vehicleId, dealershipId, serviceTypeId] = ids as [number, number, number, number];
    const rawStart = body.startTime;
    if (rawStart !== undefined && rawStart !== null && typeof rawStart !== 'string') {
      return { error: this.unreadable('startTime') };
    }
    const startTime = typeof rawStart === 'string' ? rawStart : '';
    const key = request.headers['idempotency-key'] ?? '';

    // Step 1a: request shape (BookAppointmentValidator).
    const failures: [string, string][] = [];
    if (key.trim() === '') failures.push(['Idempotency-Key', 'The Idempotency-Key header is required.']);
    else if (key.length > MAX_IDEMPOTENCY_KEY_LENGTH) {
      failures.push(['Idempotency-Key', `The Idempotency-Key header must be at most ${MAX_IDEMPOTENCY_KEY_LENGTH} characters.`]);
    }
    if (customerId <= 0) failures.push(['customerId', 'customerId is required.']);
    if (vehicleId <= 0) failures.push(['vehicleId', 'vehicleId is required.']);
    if (dealershipId <= 0) failures.push(['dealershipId', 'dealershipId is required.']);
    if (serviceTypeId <= 0) failures.push(['serviceTypeId', 'serviceTypeId is required.']);
    const parsed = startTime.trim() === '' ? null : parseWithOffset(startTime);
    if (startTime.trim() === '') failures.push(['startTime', 'startTime is required.']);
    else if (!parsed) {
      failures.push([
        'startTime',
        'startTime must be an ISO-8601 date and time with an offset, for example 2026-11-10T10:30:00+07:00.',
      ]);
    } else if (!yearInRange(new Date(parsed.utc).getUTCFullYear())) {
      failures.push(['startTime', `startTime must be between the years ${MIN_YEAR} and ${MAX_YEAR}.`]);
    } else if (!parsed.wholeSeconds || !isOnGrid(parsed.utc)) {
      failures.push(['startTime', 'startTime must be on the 15-minute grid (:00, :15, :30 or :45, with zero seconds).']);
    }
    if (failures.length > 0 || !parsed) return { error: validationError(failures) };
    const startUtc = parsed.utc;

    // Step 1b: rules that need the data.
    if (!customers.some((c) => c.id === customerId)) return { error: notFound('Customer', customerId) };
    const vehicle = vehicles.find((v) => v.id === vehicleId);
    if (!vehicle) return { error: notFound('Vehicle', vehicleId) };
    const dealership = dealerships.find((d) => d.id === dealershipId);
    if (!dealership) return { error: notFound('Dealership', dealershipId) };
    const service = serviceTypes.find((s) => s.id === serviceTypeId);
    if (!service) return { error: notFound('Service type', serviceTypeId) };
    if (vehicle.customerId !== customerId) return { error: rule(MESSAGES.vehicleNotOwned) };
    const offerError = this.offerRules(dealership, service);
    if (offerError) return { error: offerError };

    // Step 2: idempotency (a replay wins over the time rules below). Keys are case-sensitive.
    const requestHash = ['v1', customerId, vehicleId, dealershipId, serviceTypeId, formatUtc(startUtc)].join('|');
    const saved = this.state.idempotency[key];
    if (saved) {
      if (saved.requestHash !== requestHash) return { error: { code: 'IDEMPOTENCY_CONFLICT', message: MESSAGES.idempotencyConflict } };
      return { id: saved.appointmentId, dto: saved.response };
    }

    // Step 1c: in the future.
    const now = this.now();
    if (startUtc <= now) {
      return { error: validationError([['startTime', MESSAGES.startNotInFuture]]) };
    }

    // Step 3: whole half-open range inside opening hours.
    const range = { start: startUtc, end: startUtc + service.durationMinutes * 60_000 };
    if (!windowCovers(dealership.openTime, dealership.closeTime, range, dealership.timeZoneId)) {
      return { error: rule(MESSAGES.outsideOpeningHours) };
    }

    // The car is a reserved resource too (SPEC 0c).
    if (!this.isFree('vehicleId', vehicleId, range)) {
      return { error: { code: 'VEHICLE_ALREADY_BOOKED', message: MESSAGES.vehicleAlreadyBooked } };
    }

    // Steps 4-8: the first pair in strategy order wins (no concurrent writers in the browser).
    const localDate = localDateOf(startUtc, dealership.timeZoneId);
    const pair = this.findPairs(dealership, service, localDate, range)[0];
    if (!pair) {
      // Step 9.
      return {
        error: {
          code: 'NO_AVAILABILITY',
          message: MESSAGES.noAvailability,
          suggestedStartTimes: this.suggest(dealership, service, localDate, startUtc, vehicleId),
        },
      };
    }

    const appointment: StoredAppointment = {
      id: this.state.nextId,
      status: 'Confirmed',
      customerId,
      vehicleId,
      dealershipId,
      serviceTypeId,
      technicianId: pair.technician.id,
      serviceBayId: pair.bay.id,
      startUtc: range.start,
      endUtc: range.end,
      createdUtc: now,
      durationSnapshot: service.durationMinutes,
    };
    const dto = this.toDto(appointment);
    this.state = {
      ...this.state,
      nextId: this.state.nextId + 1,
      appointments: [...this.state.appointments, appointment],
      idempotency: { ...this.state.idempotency, [key]: { requestHash, appointmentId: appointment.id, response: dto } },
    };
    this.changed();
    return { id: appointment.id, dto };
  }

  /**
   * Demo only (the guided tour): another customer takes the first free technician + bay at this start,
   * so the next booking of the same time gets 409 NO_AVAILABILITY, like a real race. The row has
   * customerId 0 and vehicleId 0, so it never shows in the demo customer's list. Returns false when
   * nothing was free.
   */
  simulateOtherCustomerBooking(dealershipId: number, serviceTypeId: number, startUtc: number): boolean {
    const dealership = dealerships.find((d) => d.id === dealershipId);
    const service = serviceTypes.find((s) => s.id === serviceTypeId);
    if (!dealership || !service) return false;
    const range = { start: startUtc, end: startUtc + service.durationMinutes * 60_000 };
    const pair = this.findPairs(dealership, service, localDateOf(startUtc, dealership.timeZoneId), range)[0];
    if (!pair) return false;
    const appointment: StoredAppointment = {
      id: this.state.nextId,
      status: 'Confirmed',
      customerId: 0,
      vehicleId: 0,
      dealershipId,
      serviceTypeId,
      technicianId: pair.technician.id,
      serviceBayId: pair.bay.id,
      startUtc: range.start,
      endUtc: range.end,
      createdUtc: this.now(),
      durationSnapshot: service.durationMinutes,
    };
    this.state = { ...this.state, nextId: this.state.nextId + 1, appointments: [...this.state.appointments, appointment] };
    this.changed();
    return true;
  }

  // --- appointments ------------------------------------------------------------------------

  private getAppointments(query: URLSearchParams): MockResponse {
    const path = '/api/appointments';
    const raw = query.get('customerId');
    if (raw !== null && raw !== '' && !/^-?\d+$/.test(raw)) return this.problem(path, this.unreadable('customerId'));
    const customerId = raw ? Number(raw) : 0;
    if (customerId <= 0) return this.problem(path, validationError([['customerId', 'customerId is required.']]));
    return this.ok(
      this.state.appointments
        .filter((a) => a.customerId === customerId)
        .sort((a, b) => b.startUtc - a.startUtc || b.id - a.id)
        .map((a) => this.toDto(a)),
    );
  }

  private getAppointment(id: number): MockResponse {
    const appointment = this.state.appointments.find((a) => a.id === id);
    if (!appointment) return this.problem(`/api/appointments/${id}`, notFound('Appointment', id));
    return this.ok(this.toDto(appointment));
  }

  private cancel(id: number): MockResponse {
    const path = `/api/appointments/${id}/cancel`;
    const appointment = this.state.appointments.find((a) => a.id === id);
    if (!appointment) return this.problem(path, notFound('Appointment', id));
    if (appointment.status !== 'Confirmed') {
      return this.problem(path, {
        code: 'APPOINTMENT_NOT_CANCELLABLE',
        message: `Only confirmed appointments can be cancelled. This appointment is ${appointment.status}.`,
      });
    }
    if (appointment.startUtc <= this.now()) {
      return this.problem(path, { code: 'APPOINTMENT_NOT_CANCELLABLE', message: MESSAGES.alreadyStarted });
    }
    const cancelled: StoredAppointment = { ...appointment, status: 'Cancelled' };
    this.state = { ...this.state, appointments: this.state.appointments.map((a) => (a.id === id ? cancelled : a)) };
    this.changed();
    return this.ok(this.toDto(cancelled));
  }

  private toDto(a: StoredAppointment) {
    const vehicle = vehicles.find((v) => v.id === a.vehicleId)!;
    const dealership = dealerships.find((d) => d.id === a.dealershipId)!;
    const service = serviceTypes.find((s) => s.id === a.serviceTypeId)!;
    const technician = technicians.find((t) => t.id === a.technicianId)!;
    const bay = bays.find((b) => b.id === a.serviceBayId)!;
    return {
      id: a.id,
      status: a.status,
      startUtc: formatUtc(a.startUtc),
      endUtc: formatUtc(a.endUtc),
      createdUtc: formatUtc(a.createdUtc),
      customerId: a.customerId,
      vehicle: { id: vehicle.id, make: vehicle.make, model: vehicle.model, year: vehicle.year, vin: vehicle.vin },
      dealership: { id: dealership.id, name: dealership.name, timeZoneId: dealership.timeZoneId },
      serviceType: { id: service.id, name: service.name, durationMinutes: a.durationSnapshot },
      technician: { id: technician.id, name: technician.name },
      serviceBay: { id: bay.id, name: bay.name, bayType: bay.bayType },
    };
  }

  // --- responses ---------------------------------------------------------------------------

  private ok(body: unknown): MockResponse {
    return { status: 200, headers: { 'Content-Type': 'application/json; charset=utf-8' }, body };
  }

  private unreadable(field: string): AppError {
    return { code: 'INVALID_REQUEST', message: MESSAGES.unreadable, errors: { [field]: ['The value is not valid.'] } };
  }

  private problem(path: string, error: AppError): MockResponse {
    const { status: mappedStatus, title } = PROBLEM[error.code];
    const status = error.status ?? mappedStatus;
    const body: Record<string, unknown> = {
      type: TYPE_BY_STATUS[status] ?? 'about:blank',
      title,
      status,
      detail: error.message,
      instance: path,
      code: error.code,
      traceId: `preview-${Math.random().toString(16).slice(2, 10)}`,
    };
    if (error.errors && Object.keys(error.errors).length > 0) body.errors = error.errors;
    if (error.code === 'NO_AVAILABILITY') body.suggestedStartTimes = (error.suggestedStartTimes ?? []).map(formatUtc);
    const headers: Record<string, string> = { 'Content-Type': 'application/problem+json; charset=utf-8' };
    if (status === 503) headers['Retry-After'] = '1';
    return { status, headers, body };
  }

  private changed(): void {
    this.onChange?.(this.state);
  }
}
