// All TanStack Query keys in one place so invalidation stays consistent.

/** Mutation keys, e.g. to know elsewhere that a booking is being saved. */
export const mutationKeys = {
  bookAppointment: ['book-appointment'] as const,
};

export const queryKeys = {
  customers: ['customers'] as const,
  dealerships: ['dealerships'] as const,
  serviceTypes: (dealershipId: number) => ['service-types', dealershipId] as const,
  vehicles: (customerId: number) => ['vehicles', customerId] as const,
  availabilityAll: ['availability'] as const,
  availability: (dealershipId: number, serviceTypeId: number, date: string, vehicleId: number | null) =>
    ['availability', dealershipId, serviceTypeId, date, vehicleId] as const,
  availabilityDaysAll: ['availability-days'] as const,
  availabilityDays: (dealershipId: number, serviceTypeId: number, from: string, to: string, vehicleId: number | null) =>
    ['availability-days', dealershipId, serviceTypeId, from, to, vehicleId] as const,
  assignmentPreviewAll: ['assignment-preview'] as const,
  assignmentPreview: (dealershipId: number, serviceTypeId: number, startTime: string, vehicleId: number | null) =>
    ['assignment-preview', dealershipId, serviceTypeId, startTime, vehicleId] as const,
  appointmentsAll: ['appointments'] as const,
  appointments: (customerId: number) => ['appointments', customerId] as const,
};
