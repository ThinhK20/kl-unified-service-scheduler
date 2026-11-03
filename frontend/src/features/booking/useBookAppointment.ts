import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback, useRef } from 'react';
import { bookAppointment } from '../../api/appointmentsApi';
import { isApiError, type Appointment, type BookAppointmentRequest } from '../../api/types';
import { createIdempotencyKey } from '../../lib/idempotency';
import { mutationKeys, queryKeys } from '../../lib/queryKeys';

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

  const invalidateAvailability = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.availabilityAll });
    void queryClient.invalidateQueries({ queryKey: queryKeys.availabilityDaysAll });
    void queryClient.invalidateQueries({ queryKey: queryKeys.assignmentPreviewAll });
  };

  const mutation = useMutation<Appointment, Error, BookingAttempt>({
    mutationKey: mutationKeys.bookAppointment,
    mutationFn: ({ request, idempotencyKey }) => bookAppointment(request, idempotencyKey),
    retry: false,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.appointmentsAll });
      invalidateAvailability();
    },
    onError: (error) => {
      // Any 409 (time taken, car busy, ...): the times, days and expected pick shown may be out of date.
      if (isApiError(error) && error.status === 409) invalidateAvailability();
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
