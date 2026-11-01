// Wire types: these mirror SPEC section 6 exactly. Do not change them without changing the SPEC.

export type HhMm = string; // "HH:mm"
export type IsoDate = string; // "YYYY-MM-DD"
export type UtcIsoString = string; // e.g. "2026-11-10T03:30:00Z"

export interface DealershipDto {
  id: number;
  name: string;
  timeZoneId: string;
  openTime: HhMm;
  closeTime: HhMm;
}

export interface ServiceTypeDto {
  id: number;
  dealershipId: number;
  name: string;
  durationMinutes: number;
  requiredSkill: string;
  minimumTier: string;
  requiredBayType: string;
}

export interface VehicleDto {
  id: number;
  customerId: number;
  vin: string;
  make: string;
  model: string;
  year: number;
}

export interface SlotDto {
  startUtc: UtcIsoString;
  endUtc: UtcIsoString;
}

export interface AvailabilityDto {
  dealershipId: number;
  serviceTypeId: number;
  date: IsoDate;
  timeZoneId: string;
  durationMinutes: number;
  slots: SlotDto[];
}

export interface BookAppointmentRequest {
  customerId: number;
  vehicleId: number;
  dealershipId: number;
  serviceTypeId: number;
  /** ISO-8601 with the dealership's offset, e.g. "2026-11-10T10:30:00+07:00". */
  startTime: string;
}

export type AppointmentStatus = 'Confirmed' | 'Cancelled' | 'Completed';

export interface AppointmentDto {
  id: number;
  status: AppointmentStatus;
  startUtc: UtcIsoString;
  endUtc: UtcIsoString;
  createdUtc: UtcIsoString;
  customerId: number;
  vehicle: { id: number; make: string; model: string; year: number; vin: string };
  dealership: { id: number; name: string; timeZoneId: string };
  serviceType: { id: number; name: string; durationMinutes: number };
  technician: { id: number; name: string };
  serviceBay: { id: number; name: string; bayType: string };
}

/** RFC 7807 ProblemDetails with the extension members from SPEC section 6. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
  suggestedStartTimes?: UtcIsoString[];
}

export type ApiErrorCode =
  | 'INVALID_REQUEST'
  | 'NOT_FOUND'
  | 'BOOKING_RULE_VIOLATION'
  | 'NO_AVAILABILITY'
  | 'IDEMPOTENCY_CONFLICT'
  | 'VEHICLE_ALREADY_BOOKED'
  | 'APPOINTMENT_NOT_CANCELLABLE'
  | 'DATABASE_BUSY'
  | 'INTERNAL_ERROR'
  /** Client side only: the request never got an HTTP response. */
  | 'NETWORK_ERROR'
  /** Client side only: an error response without a known code. */
  | 'UNKNOWN';

const KNOWN_CODES: ReadonlySet<string> = new Set<ApiErrorCode>([
  'INVALID_REQUEST',
  'NOT_FOUND',
  'BOOKING_RULE_VIOLATION',
  'NO_AVAILABILITY',
  'IDEMPOTENCY_CONFLICT',
  'VEHICLE_ALREADY_BOOKED',
  'APPOINTMENT_NOT_CANCELLABLE',
  'DATABASE_BUSY',
  'INTERNAL_ERROR',
]);

/** Typed error thrown by the API layer. `status` is 0 when there was no HTTP response. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: ApiErrorCode;
  readonly title: string;
  readonly detail: string | undefined;
  readonly errors: Record<string, string[]>;
  /** UTC ISO strings, exactly as the API sent them. */
  readonly suggestedStartTimes: UtcIsoString[];
  readonly traceId: string | undefined;

  constructor(init: {
    status: number;
    code: ApiErrorCode;
    title: string;
    detail?: string | undefined;
    errors?: Record<string, string[]> | undefined;
    suggestedStartTimes?: UtcIsoString[] | undefined;
    traceId?: string | undefined;
  }) {
    super(init.detail ?? init.title);
    this.name = 'ApiError';
    this.status = init.status;
    this.code = init.code;
    this.title = init.title;
    this.detail = init.detail;
    this.errors = init.errors ?? {};
    this.suggestedStartTimes = init.suggestedStartTimes ?? [];
    this.traceId = init.traceId;
  }

  static fromProblemDetails(status: number, problem: ProblemDetails | null): ApiError {
    const code = problem?.code && KNOWN_CODES.has(problem.code) ? (problem.code as ApiErrorCode) : 'UNKNOWN';
    return new ApiError({
      status,
      code,
      title: problem?.title ?? `Request failed (${status})`,
      detail: problem?.detail,
      errors: isStringArrayRecord(problem?.errors) ? problem.errors : undefined,
      suggestedStartTimes: Array.isArray(problem?.suggestedStartTimes)
        ? problem.suggestedStartTimes.filter((s): s is string => typeof s === 'string')
        : undefined,
      traceId: problem?.traceId,
    });
  }

  static network(cause: unknown): ApiError {
    const error = new ApiError({
      status: 0,
      code: 'NETWORK_ERROR',
      title: 'Network error',
      detail: 'We could not reach the server.',
    });
    (error as { cause?: unknown }).cause = cause;
    return error;
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

function isStringArrayRecord(value: unknown): value is Record<string, string[]> {
  if (typeof value !== 'object' || value === null) return false;
  return Object.values(value).every((v) => Array.isArray(v) && v.every((s) => typeof s === 'string'));
}

// ---------------------------------------------------------------------------
// UI types: what the Adapter (appointmentsApi.ts) returns to the rest of the app.
// Times are real Date objects (instants); format them with the dealership's zone.
// ---------------------------------------------------------------------------

export type Dealership = DealershipDto;
export type ServiceType = ServiceTypeDto;

export interface Vehicle extends VehicleDto {
  /** e.g. "Toyota Vios 2021" */
  displayName: string;
}

export interface TimeSlot {
  /** Stable id for the slot: the UTC ISO string of the start instant (Date.toISOString()). */
  key: string;
  start: Date;
  end: Date;
}

export interface Availability {
  dealershipId: number;
  serviceTypeId: number;
  date: IsoDate;
  timeZoneId: string;
  durationMinutes: number;
  slots: TimeSlot[];
}

export interface Appointment {
  id: number;
  status: AppointmentStatus;
  start: Date;
  end: Date;
  created: Date;
  customerId: number;
  timeZoneId: string;
  vehicle: AppointmentDto['vehicle'] & { displayName: string };
  dealership: AppointmentDto['dealership'];
  serviceType: AppointmentDto['serviceType'];
  technician: AppointmentDto['technician'];
  serviceBay: AppointmentDto['serviceBay'];
}
