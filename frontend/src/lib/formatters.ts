import type { Dealership, ServiceType, Vehicle } from '../api/types';

export function formatDuration(minutes: number): string {
  return `${minutes} min`;
}

export function formatServiceOption(service: ServiceType): string {
  return `${service.name} · ${formatDuration(service.durationMinutes)}`;
}

export function formatDealershipOption(dealership: Dealership): string {
  return `${dealership.name} · open ${dealership.openTime}–${dealership.closeTime}`;
}

export function formatVehicleOption(vehicle: Vehicle): string {
  return `${vehicle.displayName} · VIN ${vehicle.vin}`;
}

/** Booking reference shown to the customer. */
export function formatReference(appointmentId: number): string {
  return `#${appointmentId}`;
}

