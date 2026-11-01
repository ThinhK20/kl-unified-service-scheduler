import type { AppointmentDto, AvailabilityDto, DealershipDto, ServiceTypeDto, VehicleDto } from '../api/types';

export const TZ = 'Asia/Ho_Chi_Minh';
export const DATE = '2030-01-15';

export const dealerships: DealershipDto[] = [
  { id: 1, name: 'District 1 Service Centre', timeZoneId: TZ, openTime: '08:00', closeTime: '17:00' },
  { id: 2, name: 'Thu Duc Service Centre', timeZoneId: TZ, openTime: '07:30', closeTime: '18:00' },
];

export const serviceTypesByDealer: Record<number, ServiceTypeDto[]> = {
  1: [
    { id: 1, dealershipId: 1, name: 'Oil change', durationMinutes: 60, requiredSkill: 'General', minimumTier: 'Trainee', requiredBayType: 'Standard' },
    { id: 2, dealershipId: 1, name: 'Brake inspection', durationMinutes: 90, requiredSkill: 'Brakes', minimumTier: 'Qualified', requiredBayType: 'Lift' },
  ],
  2: [
    { id: 5, dealershipId: 2, name: 'Oil change', durationMinutes: 60, requiredSkill: 'General', minimumTier: 'Trainee', requiredBayType: 'Standard' },
  ],
};

export const vehicles: VehicleDto[] = [
  { id: 1, customerId: 1, vin: 'JTDBT923X71000001', make: 'Toyota', model: 'Vios', year: 2021 },
  { id: 2, customerId: 1, vin: '7FARW2H58PE000002', make: 'Honda', model: 'CR-V', year: 2023 },
];

/** 10:00, 10:30, 11:00 local (UTC+7) on DATE, 60 minutes each. */
export const availabilityDto: AvailabilityDto = {
  dealershipId: 1,
  serviceTypeId: 1,
  date: DATE,
  timeZoneId: TZ,
  durationMinutes: 60,
  slots: [
    { startUtc: '2030-01-15T03:00:00Z', endUtc: '2030-01-15T04:00:00Z' },
    { startUtc: '2030-01-15T03:30:00Z', endUtc: '2030-01-15T04:30:00Z' },
    { startUtc: '2030-01-15T04:00:00Z', endUtc: '2030-01-15T05:00:00Z' },
  ],
};

export function appointmentDto(overrides: Partial<AppointmentDto> = {}): AppointmentDto {
  return {
    id: 42,
    status: 'Confirmed',
    startUtc: '2030-01-15T03:30:00Z',
    endUtc: '2030-01-15T04:30:00Z',
    createdUtc: '2030-01-01T00:00:00Z',
    customerId: 1,
    vehicle: { id: 1, make: 'Toyota', model: 'Vios', year: 2021, vin: 'JTDBT923X71000001' },
    dealership: { id: 1, name: 'District 1 Service Centre', timeZoneId: TZ },
    serviceType: { id: 1, name: 'Oil change', durationMinutes: 60 },
    technician: { id: 3, name: 'Minh' },
    serviceBay: { id: 1, name: 'Bay 1', bayType: 'Standard' },
    ...overrides,
  };
}
