import { useQuery } from '@tanstack/react-query';
import { getCustomerVehicles, getDealerships, getServiceTypes } from '../../api/appointmentsApi';
import { queryKeys } from '../../lib/queryKeys';

const CATALOG_STALE_MS = 5 * 60_000;

/** The customer's vehicles. Waits until a customer is known. */
export function useVehicles(customerId: number | null) {
  return useQuery({
    queryKey: queryKeys.vehicles(customerId ?? 0),
    queryFn: ({ signal }) => getCustomerVehicles(customerId as number, signal),
    enabled: customerId !== null,
    staleTime: CATALOG_STALE_MS,
  });
}

export function useDealerships() {
  return useQuery({
    queryKey: queryKeys.dealerships,
    queryFn: ({ signal }) => getDealerships(signal),
    staleTime: CATALOG_STALE_MS,
  });
}

export function useServiceTypes(dealershipId: number | null) {
  return useQuery({
    queryKey: queryKeys.serviceTypes(dealershipId ?? 0),
    queryFn: ({ signal }) => getServiceTypes(dealershipId as number, signal),
    enabled: dealershipId !== null,
    staleTime: CATALOG_STALE_MS,
  });
}
