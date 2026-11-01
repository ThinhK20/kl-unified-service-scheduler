import type { Appointment } from '../../api/types';

/** SPEC 0c: only Confirmed appointments that have not started yet can be cancelled. */
export function isCancellable(appointment: Appointment, now: Date): boolean {
  return appointment.status === 'Confirmed' && appointment.start.getTime() > now.getTime();
}
