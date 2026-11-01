import { useQuery } from '@tanstack/react-query';
import { getAvailability } from '../../api/appointmentsApi';
import { isIsoDate } from '../../lib/dateTime';
import { queryKeys } from '../../lib/queryKeys';

export interface AvailabilityParams {
  dealershipId: number | null;
  serviceTypeId: number | null;
  /** Dealership-local calendar date, "YYYY-MM-DD". */
  date: string;
}

/** Free start times for one service on one day. Only runs when all three inputs are set. */
export function useAvailability({ dealershipId, serviceTypeId, date }: AvailabilityParams) {
  const enabled = dealershipId !== null && serviceTypeId !== null && isIsoDate(date);
  return useQuery({
    queryKey: queryKeys.availability(dealershipId ?? 0, serviceTypeId ?? 0, date),
    queryFn: ({ signal }) =>
      getAvailability({ dealershipId: dealershipId as number, serviceTypeId: serviceTypeId as number, date }, signal),
    enabled,
    // Free times change quickly; always refetch when the user comes back to them.
    staleTime: 0,
  });
}
