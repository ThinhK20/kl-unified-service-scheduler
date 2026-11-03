import { useQuery } from '@tanstack/react-query';
import { getCustomers } from '../../api/appointmentsApi';
import { queryKeys } from '../../lib/queryKeys';

/** The demo customers, ordered by id. */
export function useCustomers() {
  return useQuery({
    queryKey: queryKeys.customers,
    queryFn: ({ signal }) => getCustomers(signal),
    staleTime: 5 * 60_000,
  });
}
