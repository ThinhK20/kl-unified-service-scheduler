// Adapter: the only module the hooks talk to. It calls apiClient and turns wire DTOs
// (UTC strings) into UI types (Date objects, display names).
import { apiRequest } from './apiClient';
import type {
  Appointment,
  AppointmentDto,
  Availability,
  AvailabilityDto,
  BookAppointmentRequest,
  Dealership,
  DealershipDto,
  ServiceType,
  ServiceTypeDto,
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

export function toAvailability(dto: AvailabilityDto): Availability {
  return {
    dealershipId: dto.dealershipId,
    serviceTypeId: dto.serviceTypeId,
    date: dto.date,
    timeZoneId: dto.timeZoneId,
    durationMinutes: dto.durationMinutes,
    slots: dto.slots.map((s) => toTimeSlot(s.startUtc, s.endUtc)),
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

export async function getCustomerVehicles(customerId: number, signal?: AbortSignal): Promise<Vehicle[]> {
  const dtos = await apiRequest<VehicleDto[]>(`/api/customers/${customerId}/vehicles`, { signal });
  return dtos.map(toVehicle);
}

export async function getAvailability(
  params: { dealershipId: number; serviceTypeId: number; date: string },
  signal?: AbortSignal,
): Promise<Availability> {
  const query = new URLSearchParams({ serviceTypeId: String(params.serviceTypeId), date: params.date });
  const dto = await apiRequest<AvailabilityDto>(
    `/api/dealerships/${params.dealershipId}/availability?${query.toString()}`,
    { signal },
  );
  return toAvailability(dto);
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
