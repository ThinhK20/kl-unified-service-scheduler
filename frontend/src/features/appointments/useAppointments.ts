import { useQuery } from '@tanstack/react-query';
import { getAppointments } from '../../api/appointmentsApi';
import { queryKeys } from '../../lib/queryKeys';

/** The customer's appointments, newest first (the API sorts them). */
export function useAppointments(customerId: number) {
  return useQuery({
    queryKey: queryKeys.appointments(customerId),
    queryFn: ({ signal }) => getAppointments(customerId, signal),
  });
}
