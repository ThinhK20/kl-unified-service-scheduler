import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback, useRef } from 'react';
import { bookAppointment } from '../../api/appointmentsApi';
import { isApiError, type Appointment, type BookAppointmentRequest } from '../../api/types';
import { createIdempotencyKey } from '../../lib/idempotency';
import { queryKeys } from '../../lib/queryKeys';

interface BookingAttempt {
  request: BookAppointmentRequest;
  idempotencyKey: string;
}

/**
 * Books an appointment.
 * - `book()` is one form submit: it creates ONE new Idempotency-Key.
 * - `retry()` sends the last attempt again with the SAME key and body (used after 503 / network error).
 */
export function useBookAppointment() {
  const queryClient = useQueryClient();
  const lastAttempt = useRef<BookingAttempt | null>(null);

  const mutation = useMutation<Appointment, Error, BookingAttempt>({
    mutationFn: ({ request, idempotencyKey }) => bookAppointment(request, idempotencyKey),
    retry: false,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.appointmentsAll });
      void queryClient.invalidateQueries({ queryKey: queryKeys.availabilityAll });
    },
    onError: (error) => {
      // Someone took the time: the free-times list is out of date.
      if (isApiError(error) && error.code === 'NO_AVAILABILITY') {
        void queryClient.invalidateQueries({ queryKey: queryKeys.availabilityAll });
      }
    },
  });

  const { mutate, reset } = mutation;

  const book = useCallback(
    (request: BookAppointmentRequest) => {
      const attempt: BookingAttempt = { request, idempotencyKey: createIdempotencyKey() };
      lastAttempt.current = attempt;
      mutate(attempt);
    },
    [mutate],
  );

  const retry = useCallback(() => {
    if (lastAttempt.current) mutate(lastAttempt.current);
  }, [mutate]);

  return {
    book,
    retry,
    reset,
    isSaving: mutation.isPending,
    error: mutation.error,
    appointment: mutation.data ?? null,
  };
}
