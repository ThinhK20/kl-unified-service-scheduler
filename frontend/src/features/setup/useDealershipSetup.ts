import { useQuery } from '@tanstack/react-query';
import { getDealershipSetup } from '../../api/appointmentsApi';
import { queryKeys } from '../../lib/queryKeys';

/** The setup of one dealership. Waits until a dealership is chosen. */
export function useDealershipSetup(dealershipId: number | null) {
  return useQuery({
    queryKey: queryKeys.dealershipSetup(dealershipId ?? 0),
    queryFn: ({ signal }) => getDealershipSetup(dealershipId as number, signal),
    enabled: dealershipId !== null,
    staleTime: 5 * 60_000,
  });
}
