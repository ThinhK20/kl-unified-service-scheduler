// Adapter: the only module the hooks talk to. It calls apiClient and turns wire DTOs
// (UTC strings) into UI types (Date objects, display names).
import { apiRequest } from './apiClient';
import type {
  Appointment,
  AssignmentPreview,
  AssignmentPreviewDto,
  AssignmentStatus,
  BayState,
  Customer,
  CustomerDto,
  AppointmentDto,
  Availability,
  AvailabilityDays,
  AvailabilityDaysDto,
  AvailabilityDto,
  BookAppointmentRequest,
  DayAvailability,
  DayAvailabilityDto,
  DayStatus,
  Dealership,
  DealershipDto,
  ServiceType,
  ServiceTypeDto,
  StartTime,
  StartTimeDto,
  StartTimeStatus,
  TechnicianState,
  TimeSlot,
  Vehicle,
  VehicleDto,
} from './types';

export function vehicleDisplayName(v: { make: string; model: string; year: number }): string {
  return `${v.make} ${v.model} ${v.year}`;
}

export function toTimeSlot(startUtc: string, endUtc: string): TimeSlot {
  const start = new Date(startUtc);
  return { key: start.toISOString(), start, end: new Date(endUtc) };
}

const START_TIME_STATUSES: ReadonlySet<string> = new Set<StartTimeStatus>(['Past', 'VehicleBooked', 'Booked', 'Available']);
const DAY_STATUSES: ReadonlySet<string> = new Set<DayStatus>(['Past', 'FullyBooked', 'Available']);

/** An unknown status is never offered as bookable. */
function toStartTimeStatus(value: string): StartTimeStatus {
  return START_TIME_STATUSES.has(value) ? (value as StartTimeStatus) : 'Booked';
}

function toDayStatus(dto: DayAvailabilityDto): DayStatus {
  if (DAY_STATUSES.has(dto.status)) return dto.status;
  return dto.availableCount > 0 ? 'Available' : 'FullyBooked';
}

export function toStartTime(dto: StartTimeDto): StartTime {
  return { ...toTimeSlot(dto.startUtc, dto.endUtc), status: toStartTimeStatus(dto.status) };
}

export function toAvailability(dto: AvailabilityDto): Availability {
  const slots = dto.slots.map((s) => toTimeSlot(s.startUtc, s.endUtc));
  return {
    dealershipId: dto.dealershipId,
    serviceTypeId: dto.serviceTypeId,
    date: dto.date,
    timeZoneId: dto.timeZoneId,
    durationMinutes: dto.durationMinutes,
    slots,
    // An API without `times` (before contract v2) only knows the free starts.
    times: dto.times ? dto.times.map(toStartTime) : slots.map((s) => ({ ...s, status: 'Available' as const })),
  };
}

export function toDayAvailability(dto: DayAvailabilityDto): DayAvailability {
  return { date: dto.date, availableCount: dto.availableCount, status: toDayStatus(dto) };
}

export function toAvailabilityDays(dto: AvailabilityDaysDto): AvailabilityDays {
  return {
    dealershipId: dto.dealershipId,
    serviceTypeId: dto.serviceTypeId,
    from: dto.from,
    to: dto.to,
    timeZoneId: dto.timeZoneId,
    durationMinutes: dto.durationMinutes,
    days: dto.days.map(toDayAvailability),
  };
}

const ASSIGNMENT_STATUSES: ReadonlySet<string> = new Set<AssignmentStatus>(['Past', 'VehicleBooked', 'NoAvailability', 'Assignable']);
const TECHNICIAN_STATES: ReadonlySet<string> = new Set<TechnicianState>(['WillServe', 'Free', 'Busy', 'OffShift', 'NotQualified', 'Inactive']);
const BAY_STATES: ReadonlySet<string> = new Set<BayState>(['WillServe', 'Free', 'Busy', 'WrongType', 'Inactive']);

/** Unknown values from a newer API are shown as "not available" rather than promised. */
export function toAssignmentPreview(dto: AssignmentPreviewDto): AssignmentPreview {
  const status: AssignmentStatus = ASSIGNMENT_STATUSES.has(dto.status) ? dto.status : 'NoAvailability';
  const assignable = status === 'Assignable' && dto.technician !== null && dto.serviceBay !== null;
  return {
    dealershipId: dto.dealershipId,
    serviceTypeId: dto.serviceTypeId,
    timeZoneId: dto.timeZoneId,
    start: new Date(dto.startUtc),
    end: new Date(dto.endUtc),
    status: assignable ? status : status === 'Assignable' ? 'NoAvailability' : status,
    technician: assignable ? dto.technician : null,
    serviceBay: assignable ? dto.serviceBay : null,
    technicians: dto.technicians.map((t) => ({ ...t, state: TECHNICIAN_STATES.has(t.state) ? t.state : 'Busy' })),
    serviceBays: dto.serviceBays.map((b) => ({ ...b, state: BAY_STATES.has(b.state) ? b.state : 'Busy' })),
  };
}

export function toVehicle(dto: VehicleDto): Vehicle {
  return { ...dto, displayName: vehicleDisplayName(dto) };
}

export function toAppointment(dto: AppointmentDto): Appointment {
  return {
    id: dto.id,
    status: dto.status,
    start: new Date(dto.startUtc),
    end: new Date(dto.endUtc),
    created: new Date(dto.createdUtc),
    customerId: dto.customerId,
    timeZoneId: dto.dealership.timeZoneId,
    vehicle: { ...dto.vehicle, displayName: vehicleDisplayName(dto.vehicle) },
    dealership: dto.dealership,
    serviceType: dto.serviceType,
    technician: dto.technician,
    serviceBay: dto.serviceBay,
  };
}

export async function getDealerships(signal?: AbortSignal): Promise<Dealership[]> {
  return apiRequest<DealershipDto[]>('/api/dealerships', { signal });
}

export async function getServiceTypes(dealershipId: number, signal?: AbortSignal): Promise<ServiceType[]> {
  const query = new URLSearchParams({ dealershipId: String(dealershipId) });
  return apiRequest<ServiceTypeDto[]>(`/api/service-types?${query.toString()}`, { signal });
}

export async function getCustomers(signal?: AbortSignal): Promise<Customer[]> {
  return apiRequest<CustomerDto[]>('/api/customers', { signal });
}

export async function getCustomerVehicles(customerId: number, signal?: AbortSignal): Promise<Vehicle[]> {
  const dtos = await apiRequest<VehicleDto[]>(`/api/customers/${customerId}/vehicles`, { signal });
  return dtos.map(toVehicle);
}

export async function getAvailability(
  params: { dealershipId: number; serviceTypeId: number; date: string; vehicleId?: number | null },
  signal?: AbortSignal,
): Promise<Availability> {
  const query = new URLSearchParams({ serviceTypeId: String(params.serviceTypeId), date: params.date });
  if (params.vehicleId != null) query.set('vehicleId', String(params.vehicleId));
  const dto = await apiRequest<AvailabilityDto>(
    `/api/dealerships/${params.dealershipId}/availability?${query.toString()}`,
    { signal },
  );
  return toAvailability(dto);
}

/** Per-day status for an inclusive range of dealership-local dates (at most 42 days). */
export async function getAvailabilityDays(
  params: { dealershipId: number; serviceTypeId: number; from: string; to: string; vehicleId?: number | null },
  signal?: AbortSignal,
): Promise<AvailabilityDays> {
  const query = new URLSearchParams({
    serviceTypeId: String(params.serviceTypeId),
    from: params.from,
    to: params.to,
  });
  if (params.vehicleId != null) query.set('vehicleId', String(params.vehicleId));
  const dto = await apiRequest<AvailabilityDaysDto>(
    `/api/dealerships/${params.dealershipId}/availability/days?${query.toString()}`,
    { signal },
  );
  return toAvailabilityDays(dto);
}

/** Who would serve this booking if it were made now. Reserves nothing. */
export async function getAssignmentPreview(
  params: {
    dealershipId: number;
    serviceTypeId: number;
    /** ISO-8601 with an offset, e.g. "2026-11-10T10:30:00+07:00" (same format as the booking request). */
    startTime: string;
    vehicleId?: number | null;
  },
  signal?: AbortSignal,
): Promise<AssignmentPreview> {
  const query = new URLSearchParams({ serviceTypeId: String(params.serviceTypeId), startTime: params.startTime });
  if (params.vehicleId != null) query.set('vehicleId', String(params.vehicleId));
  const dto = await apiRequest<AssignmentPreviewDto>(
    `/api/dealerships/${params.dealershipId}/assignment-preview?${query.toString()}`,
    { signal },
  );
  return toAssignmentPreview(dto);
}

export async function bookAppointment(request: BookAppointmentRequest, idempotencyKey: string): Promise<Appointment> {
  const dto = await apiRequest<AppointmentDto>('/api/appointments', {
    method: 'POST',
    body: request,
    headers: { 'Idempotency-Key': idempotencyKey },
  });
  return toAppointment(dto);
}

export async function getAppointments(customerId: number, signal?: AbortSignal): Promise<Appointment[]> {
  const query = new URLSearchParams({ customerId: String(customerId) });
  const dtos = await apiRequest<AppointmentDto[]>(`/api/appointments?${query.toString()}`, { signal });
  return dtos.map(toAppointment);
}

export async function cancelAppointment(id: number): Promise<Appointment> {
  const dto = await apiRequest<AppointmentDto>(`/api/appointments/${id}/cancel`, { method: 'POST' });
  return toAppointment(dto);
}
