import { useMutation, useQueryClient } from '@tanstack/react-query';
import { cancelAppointment } from '../../api/appointmentsApi';
import type { Appointment } from '../../api/types';
import { queryKeys } from '../../lib/queryKeys';

/**
 * Cancels one appointment. On success AND on failure (e.g. 409 APPOINTMENT_NOT_CANCELLABLE because
 * it already started) it refreshes the list, and the free times (the slots may be free again).
 */
export function useCancelAppointment() {
  const queryClient = useQueryClient();
  const mutation = useMutation<Appointment, Error, number>({
    mutationFn: (id) => cancelAppointment(id),
    retry: false,
    onSettled: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.appointmentsAll }),
        queryClient.invalidateQueries({ queryKey: queryKeys.availabilityAll }),
      ]),
  });

  return {
    cancel: mutation.mutate,
    cancellingId: mutation.isPending ? mutation.variables : undefined,
    failedId: mutation.isError ? mutation.variables : undefined,
    error: mutation.error,
  };
}
