import { useQuery } from '@tanstack/react-query';
import { getAppointments } from '../../api/appointmentsApi';
import { queryKeys } from '../../lib/queryKeys';

/** The customer's appointments, newest first (the API sorts them). */
export function useAppointments(customerId: number | null) {
  return useQuery({
    queryKey: queryKeys.appointments(customerId ?? 0),
    queryFn: ({ signal }) => getAppointments(customerId as number, signal),
    enabled: customerId !== null,
  });
}
