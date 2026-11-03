import type {
  AppointmentDto,
  AvailabilityDaysDto,
  AssignmentPreviewDto,
  AvailabilityDto,
  CustomerDto,
  DayStatus,
  DealershipDto,
  ServiceTypeDto,
  StartTimeStatus,
  VehicleDto,
} from '../api/types';

export const TZ = 'Asia/Ho_Chi_Minh';
export const DATE = '2030-01-15';
/** "Now" for page tests: 10:00 on 10 Jan 2030 in Ho Chi Minh City, so the calendar opens on January 2030. */
export const NOW = '2030-01-10T03:00:00Z';
export const TODAY = '2030-01-10';

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

export const customers: CustomerDto[] = [
  { id: 1, name: 'An Nguyen', email: 'an.nguyen@example.com', phone: '+84 900 000 001' },
  { id: 2, name: 'Binh Tran', email: 'binh.tran@example.com', phone: '+84 900 000 002' },
  { id: 3, name: 'Chi Le', email: 'chi.le@example.com', phone: '+84 900 000 003' },
];

export const vehiclesByCustomer: Record<number, VehicleDto[]> = {
  2: [{ id: 3, customerId: 2, vin: 'RLLV8EAA5RV000003', make: 'VinFast', model: 'VF 8', year: 2024 }],
  3: [
    { id: 4, customerId: 3, vin: 'JM3KFBCM1N0000004', make: 'Mazda', model: 'CX-5', year: 2022 },
    { id: 5, customerId: 3, vin: 'KNABE511AL0000005', make: 'Kia', model: 'Morning', year: 2020 },
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
  times: [
    { startUtc: '2030-01-15T03:00:00Z', endUtc: '2030-01-15T04:00:00Z', status: 'Available' },
    { startUtc: '2030-01-15T03:30:00Z', endUtc: '2030-01-15T04:30:00Z', status: 'Available' },
    { startUtc: '2030-01-15T04:00:00Z', endUtc: '2030-01-15T05:00:00Z', status: 'Available' },
  ],
};

/**
 * The same three starts (10:00, 10:30, 11:00 local) on DATE with the given statuses,
 * plus 09:30 which is always Past (and must not be shown).
 */
export function availabilityWith(statuses: [StartTimeStatus, StartTimeStatus, StartTimeStatus]): AvailabilityDto {
  const starts = ['03:00', '03:30', '04:00'];
  const times = [
    { startUtc: '2030-01-15T02:30:00Z', endUtc: '2030-01-15T03:30:00Z', status: 'Past' as const },
    ...starts.map((hhmm, i) => {
      const start = new Date(`${DATE}T${hhmm}:00Z`);
      return {
        startUtc: start.toISOString().replace('.000Z', 'Z'),
        endUtc: new Date(start.getTime() + 3_600_000).toISOString().replace('.000Z', 'Z'),
        status: statuses[i] as StartTimeStatus,
      };
    }),
  ];
  return {
    ...availabilityDto,
    slots: times.filter((t) => t.status === 'Available').map(({ startUtc, endUtc }) => ({ startUtc, endUtc })),
    times,
  };
}

/** One entry per date from..to: before TODAY Past, otherwise Available (3 times) unless overridden. */
export function availabilityDaysDto(
  from: string,
  to: string,
  overrides: Record<string, DayStatus> = {},
): AvailabilityDaysDto {
  const days: AvailabilityDaysDto['days'] = [];
  for (let t = Date.parse(`${from}T00:00:00Z`); t <= Date.parse(`${to}T00:00:00Z`); t += 86_400_000) {
    const date = new Date(t).toISOString().slice(0, 10);
    const status: DayStatus = overrides[date] ?? (date < TODAY ? 'Past' : 'Available');
    days.push({ date, status, availableCount: status === 'Available' ? 3 : 0 });
  }
  return { dealershipId: 1, serviceTypeId: 1, from, to, timeZoneId: TZ, durationMinutes: 60, days };
}

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

/** Oil change 10:30-11:30 local on DATE: Minh and Bay 1 will serve. */
export function assignmentPreviewDto(overrides: Partial<AssignmentPreviewDto> = {}): AssignmentPreviewDto {
  return {
    dealershipId: 1,
    serviceTypeId: 1,
    timeZoneId: TZ,
    startUtc: '2030-01-15T03:30:00Z',
    endUtc: '2030-01-15T04:30:00Z',
    status: 'Assignable',
    technician: { id: 3, name: 'Minh' },
    serviceBay: { id: 1, name: 'Bay 1', bayType: 'Standard' },
    technicians: [
      { id: 3, name: 'Minh', state: 'WillServe' },
      { id: 2, name: 'Lan', state: 'Free' },
      { id: 1, name: 'Nam', state: 'Busy' },
      { id: 6, name: 'Thao', state: 'OffShift' },
      { id: 7, name: 'Vy', state: 'NotQualified' },
      { id: 5, name: 'Tuan', state: 'Inactive' },
    ],
    serviceBays: [
      { id: 1, name: 'Bay 1', bayType: 'Standard', state: 'WillServe' },
      { id: 6, name: 'Bay 5', bayType: 'Standard', state: 'Busy' },
      { id: 2, name: 'Bay 2', bayType: 'Lift', state: 'WrongType' },
      { id: 5, name: 'Bay 4', bayType: 'Lift', state: 'Inactive' },
    ],
    ...overrides,
  };
}
