import { z } from 'zod';
import { isIsoDate } from '../../lib/dateTime';

/**
 * Form values. Select and date inputs give strings; they are turned into numbers on submit.
 * `startTime` holds the selected slot key (UTC ISO string of the slot start).
 */
export interface BookingFormValues {
  vehicleId: string;
  dealershipId: string;
  serviceTypeId: string;
  date: string;
  startTime: string;
}

export const emptyBookingForm: BookingFormValues = {
  vehicleId: '',
  dealershipId: '',
  serviceTypeId: '',
  date: '',
  startTime: '',
};

/**
 * @param getMinDate earliest allowed date ("YYYY-MM-DD") for the chosen dealership (today in its time zone).
 */
export function createBookingSchema(getMinDate: (dealershipId: string) => string) {
  return z
    .object({
      vehicleId: z.string({ error: 'Please choose a vehicle' }).min(1, 'Please choose a vehicle'),
      dealershipId: z.string({ error: 'Please choose a dealership' }).min(1, 'Please choose a dealership'),
      serviceTypeId: z.string({ error: 'Please choose a service' }).min(1, 'Please choose a service'),
      date: z.string({ error: 'Please choose a date' }).refine(isIsoDate, 'Please choose a date'),
      startTime: z.string({ error: 'Please choose a start time' }).min(1, 'Please choose a start time'),
    })
    .superRefine((values, ctx) => {
      if (isIsoDate(values.date) && values.date < getMinDate(values.dealershipId)) {
        ctx.addIssue({ code: 'custom', path: ['date'], message: 'Please choose today or a later date' });
      }
    });
}
