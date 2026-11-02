import { useQuery } from '@tanstack/react-query';
import { getAvailability, getAvailabilityDays } from '../../api/appointmentsApi';
import { endOfMonthIso, isIsoDate, startOfMonthIso } from '../../lib/dateTime';
import { queryKeys } from '../../lib/queryKeys';

export interface AvailabilityParams {
  dealershipId: number | null;
  serviceTypeId: number | null;
  /** Dealership-local calendar date, "YYYY-MM-DD". */
  date: string;
  /** The chosen car, so times it is already booked at show as such. Null = ignore the car. */
  vehicleId: number | null;
}

/** Every start time of one day with its status. Only runs when dealership, service and date are set. */
export function useAvailability({ dealershipId, serviceTypeId, date, vehicleId }: AvailabilityParams) {
  const enabled = dealershipId !== null && serviceTypeId !== null && isIsoDate(date);
  return useQuery({
    queryKey: queryKeys.availability(dealershipId ?? 0, serviceTypeId ?? 0, date, vehicleId),
    queryFn: ({ signal }) =>
      getAvailability(
        { dealershipId: dealershipId as number, serviceTypeId: serviceTypeId as number, date, vehicleId },
        signal,
      ),
    enabled,
    // Free times change quickly; always refetch when the user comes back to them.
    staleTime: 0,
  });
}

export interface AvailabilityDaysParams {
  dealershipId: number | null;
  serviceTypeId: number | null;
  /** Any date in the month the calendar shows ("YYYY-MM-DD"). */
  month: string;
  vehicleId: number | null;
}

/** Status of every day in the visible calendar month. */
export function useAvailabilityDays({ dealershipId, serviceTypeId, month, vehicleId }: AvailabilityDaysParams) {
  const enabled = dealershipId !== null && serviceTypeId !== null && isIsoDate(month);
  const from = enabled ? startOfMonthIso(month) : '';
  const to = enabled ? endOfMonthIso(month) : '';
  return useQuery({
    queryKey: queryKeys.availabilityDays(dealershipId ?? 0, serviceTypeId ?? 0, from, to, vehicleId),
    queryFn: ({ signal }) =>
      getAvailabilityDays(
        { dealershipId: dealershipId as number, serviceTypeId: serviceTypeId as number, from, to, vehicleId },
        signal,
      ),
    enabled,
    staleTime: 0,
  });
}
