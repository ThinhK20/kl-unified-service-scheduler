import { useMutation, useQueryClient } from '@tanstack/react-query';
import { cancelAppointment } from '../../api/appointmentsApi';
import type { Appointment } from '../../api/types';
import { queryKeys } from '../../lib/queryKeys';

/** Cancels one appointment, then refreshes the list and the free times (the slots are free again). */
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
