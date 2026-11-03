// Seed data for the in-browser preview API. Same rows and ids as the real database seed
// (SPEC section 4, backend/src/Scheduler.Infrastructure/Seeding/SeedData.cs).

/** In the backend's Skill enum order (the setup endpoint lists skills in this order). */
export const SKILLS = ['General', 'Brakes', 'Tyres', 'EV', 'Diagnostics'] as const;
export type Skill = (typeof SKILLS)[number];
export type BayType = 'Standard' | 'Lift' | 'EV';
export type Status = 'Confirmed' | 'Cancelled' | 'Completed';

/** Ordered: compare by index. */
export const TIERS = ['Trainee', 'Qualified', 'Senior', 'Master'] as const;
export type Tier = (typeof TIERS)[number];

export interface SeedCustomer {
  id: number;
  name: string;
  email: string;
  phone: string;
}

export interface SeedVehicle {
  id: number;
  customerId: number;
  vin: string;
  make: string;
  model: string;
  year: number;
}

export interface SeedDealership {
  id: number;
  name: string;
  timeZoneId: string;
  openTime: string; // local "HH:mm"
  closeTime: string;
  isActive: boolean;
}

export interface SeedServiceType {
  id: number;
  dealershipId: number;
  name: string;
  durationMinutes: number;
  requiredSkill: Skill;
  minimumTier: Tier;
  requiredBayType: BayType;
  isActive: boolean;
}

export interface SeedTechnician {
  id: number;
  dealershipId: number;
  name: string;
  shiftStart: string; // local "HH:mm"
  shiftEnd: string;
  isActive: boolean;
  skills: Partial<Record<Skill, Tier>>;
}

export interface SeedBay {
  id: number;
  dealershipId: number;
  name: string;
  bayType: BayType;
  isActive: boolean;
}

export const TIME_ZONE = 'Asia/Ho_Chi_Minh';

/** Offset from UTC in minutes. Ho Chi Minh City is UTC+07:00 all year (no daylight saving time). */
export const ZONE_OFFSET_MINUTES: Record<string, number> = { [TIME_ZONE]: 7 * 60 };

export const customers: SeedCustomer[] = [
  { id: 1, name: 'An Nguyen', email: 'an.nguyen@example.com', phone: '+84 900 000 001' },
  { id: 2, name: 'Binh Tran', email: 'binh.tran@example.com', phone: '+84 900 000 002' },
  { id: 3, name: 'Chi Le', email: 'chi.le@example.com', phone: '+84 900 000 003' },
];

export const vehicles: SeedVehicle[] = [
  { id: 1, customerId: 1, vin: 'JTDBT923X71000001', make: 'Toyota', model: 'Vios', year: 2021 },
  { id: 2, customerId: 1, vin: '7FARW2H58PE000002', make: 'Honda', model: 'CR-V', year: 2023 },
  { id: 3, customerId: 2, vin: 'RLLV8EAA5RV000003', make: 'VinFast', model: 'VF 8', year: 2024 },
  { id: 4, customerId: 3, vin: 'JM3KFBCM1N0000004', make: 'Mazda', model: 'CX-5', year: 2022 },
  { id: 5, customerId: 3, vin: 'KNABE511AL0000005', make: 'Kia', model: 'Morning', year: 2020 },
];

export const dealerships: SeedDealership[] = [
  { id: 1, name: 'District 1 Service Centre', timeZoneId: TIME_ZONE, openTime: '08:00', closeTime: '17:00', isActive: true },
  { id: 2, name: 'Thu Duc Service Centre', timeZoneId: TIME_ZONE, openTime: '07:30', closeTime: '18:00', isActive: true },
];

export const serviceTypes: SeedServiceType[] = [
  { id: 1, dealershipId: 1, name: 'Oil change', durationMinutes: 60, requiredSkill: 'General', minimumTier: 'Trainee', requiredBayType: 'Standard', isActive: true },
  { id: 2, dealershipId: 1, name: 'Brake inspection', durationMinutes: 90, requiredSkill: 'Brakes', minimumTier: 'Qualified', requiredBayType: 'Lift', isActive: true },
  { id: 3, dealershipId: 1, name: 'Tyre rotation', durationMinutes: 30, requiredSkill: 'Tyres', minimumTier: 'Trainee', requiredBayType: 'Lift', isActive: true },
  { id: 4, dealershipId: 1, name: 'EV battery check', durationMinutes: 60, requiredSkill: 'EV', minimumTier: 'Senior', requiredBayType: 'EV', isActive: true },
  { id: 5, dealershipId: 2, name: 'Oil change', durationMinutes: 60, requiredSkill: 'General', minimumTier: 'Trainee', requiredBayType: 'Standard', isActive: true },
];

export const technicians: SeedTechnician[] = [
  { id: 1, dealershipId: 1, name: 'Nam', shiftStart: '08:00', shiftEnd: '17:00', isActive: true, skills: { General: 'Master', Brakes: 'Senior', Tyres: 'Senior' } },
  { id: 2, dealershipId: 1, name: 'Lan', shiftStart: '08:00', shiftEnd: '17:00', isActive: true, skills: { General: 'Qualified', Tyres: 'Qualified', EV: 'Senior' } },
  { id: 3, dealershipId: 1, name: 'Minh', shiftStart: '12:00', shiftEnd: '17:00', isActive: true, skills: { General: 'Trainee', Brakes: 'Qualified' } },
  { id: 4, dealershipId: 2, name: 'Hoa', shiftStart: '07:30', shiftEnd: '18:00', isActive: true, skills: { General: 'Senior' } },
  { id: 5, dealershipId: 1, name: 'Tuan', shiftStart: '08:00', shiftEnd: '17:00', isActive: false, skills: { General: 'Master', Brakes: 'Master', EV: 'Master' } },
];

export const bays: SeedBay[] = [
  { id: 1, dealershipId: 1, name: 'Bay 1', bayType: 'Standard', isActive: true },
  { id: 2, dealershipId: 1, name: 'Bay 2', bayType: 'Lift', isActive: true },
  { id: 3, dealershipId: 1, name: 'Bay 3', bayType: 'EV', isActive: true },
  { id: 4, dealershipId: 2, name: 'Bay A', bayType: 'Standard', isActive: true },
  { id: 5, dealershipId: 1, name: 'Bay 4', bayType: 'Lift', isActive: false },
];

/** Example appointments for the demo customer, placed relative to "today" so My appointments is not empty. */
export interface ExampleAppointment {
  dayOffset: number; // local days from today
  localTime: string; // "HH:mm"
  status: Status;
  vehicleId: number;
  serviceTypeId: number;
  technicianId: number; // what the allocation strategy would pick on an empty day
  serviceBayId: number;
}

export const exampleAppointments: ExampleAppointment[] = [
  { dayOffset: -30, localTime: '09:00', status: 'Completed', vehicleId: 1, serviceTypeId: 1, technicianId: 1, serviceBayId: 1 },
  { dayOffset: -5, localTime: '14:00', status: 'Cancelled', vehicleId: 2, serviceTypeId: 2, technicianId: 3, serviceBayId: 2 },
  { dayOffset: 3, localTime: '10:30', status: 'Confirmed', vehicleId: 2, serviceTypeId: 1, technicianId: 1, serviceBayId: 1 },
];
