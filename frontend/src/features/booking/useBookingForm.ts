import { zodResolver } from '@hookform/resolvers/zod';
import { useCallback, useMemo } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import type { BookAppointmentRequest } from '../../api/types';
import { DEMO_CUSTOMER_ID } from '../../config';
import {
  addMinutes,
  formatIsoDate,
  formatTime,
  formatTimeRange,
  todayInBrowserZone,
  todayInZone,
  toZonedIsoString,
} from '../../lib/dateTime';
import { formatDuration } from '../../lib/formatters';
import { describeBookingError } from './bookingErrors';
import { createBookingSchema, emptyBookingForm, type BookingFormValues } from './bookingSchema';
import { useAvailability } from './useAvailability';
import { useBookAppointment } from './useBookAppointment';
import { useDealerships, useServiceTypes, useVehicles } from './useCatalog';

export interface SlotOption {
  key: string;
  label: string;
}

export type SlotsState = 'idle' | 'loading' | 'error' | 'empty' | 'ready';

const toId = (value: string): number | null => (value === '' ? null : Number(value));

/**
 * Container hook for the booking page: form state, catalog data, availability,
 * the booking mutation and the text the summary shows. Components only render its output.
 */
export function useBookingForm() {
  const vehiclesQuery = useVehicles(DEMO_CUSTOMER_ID);
  const dealershipsQuery = useDealerships();

  // The schema needs the dealership's time zone to know what "today" is.
  // react-hook-form reads the latest resolver on every render.
  const dealershipList = dealershipsQuery.data;
  const schema = useMemo(
    () =>
      createBookingSchema((dealershipId) => {
        const dealership = dealershipList?.find((d) => String(d.id) === dealershipId);
        return dealership ? todayInZone(dealership.timeZoneId) : todayInBrowserZone();
      }),
    [dealershipList],
  );

  const form = useForm<BookingFormValues>({
    resolver: zodResolver(schema),
    defaultValues: emptyBookingForm,
  });
  const { control, setValue, handleSubmit, formState, reset: resetForm } = form;
  const values = useWatch({ control });
  const booking = useBookAppointment();
  const { reset: resetBooking } = booking;

  const dealershipId = toId(values.dealershipId ?? '');
  const serviceTypeId = toId(values.serviceTypeId ?? '');
  const date = values.date ?? '';
  const startKey = values.startTime ?? '';

  const serviceTypesQuery = useServiceTypes(dealershipId);
  const availabilityQuery = useAvailability({ dealershipId, serviceTypeId, date });

  const vehicle = vehiclesQuery.data?.find((v) => v.id === toId(values.vehicleId ?? '')) ?? null;
  const dealership = dealershipsQuery.data?.find((d) => d.id === dealershipId) ?? null;
  const serviceType = serviceTypesQuery.data?.find((s) => s.id === serviceTypeId) ?? null;
  const timeZoneId = dealership?.timeZoneId ?? null;

  const minDate = timeZoneId ? todayInZone(timeZoneId) : todayInBrowserZone();

  // --- field change rules -------------------------------------------------------------
  const clearTime = useCallback(() => {
    setValue('startTime', '');
    resetBooking();
  }, [setValue, resetBooking]);

  const onDealershipChange = useCallback(() => {
    setValue('serviceTypeId', '');
    clearTime();
  }, [setValue, clearTime]);

  const selectTime = useCallback(
    (key: string) => {
      setValue('startTime', key, { shouldValidate: formState.isSubmitted });
      resetBooking();
    },
    [setValue, resetBooking, formState.isSubmitted],
  );

  // --- slots ---------------------------------------------------------------------------
  const slots: SlotOption[] =
    timeZoneId && availabilityQuery.data
      ? availabilityQuery.data.slots.map((s) => ({ key: s.key, label: formatTime(s.start, timeZoneId) }))
      : [];

  let slotsState: SlotsState = 'idle';
  if (availabilityQuery.isFetching && !availabilityQuery.data) slotsState = 'loading';
  else if (availabilityQuery.isError) slotsState = 'error';
  else if (availabilityQuery.data) slotsState = slots.length === 0 ? 'empty' : 'ready';
  if (dealershipId === null || serviceTypeId === null || date === '') slotsState = 'idle';

  // --- summary -------------------------------------------------------------------------
  const start = startKey ? new Date(startKey) : null;
  const end =
    start && serviceType
      ? (availabilityQuery.data?.slots.find((s) => s.key === startKey)?.end ??
        addMinutes(start, serviceType.durationMinutes))
      : null;

  const summary = {
    vehicle: vehicle?.displayName ?? null,
    dealership: dealership?.name ?? null,
    service: serviceType ? `${serviceType.name} · ${formatDuration(serviceType.durationMinutes)}` : null,
    date: date ? formatIsoDate(date) : null,
    time: start && end && timeZoneId ? formatTimeRange(start, end, timeZoneId) : null,
  };

  // --- submit / retry ------------------------------------------------------------------
  const onSubmit = handleSubmit((v) => {
    const chosenDealership = dealershipsQuery.data?.find((d) => String(d.id) === v.dealershipId);
    if (!chosenDealership) return;
    const request: BookAppointmentRequest = {
      customerId: DEMO_CUSTOMER_ID,
      vehicleId: Number(v.vehicleId),
      dealershipId: Number(v.dealershipId),
      serviceTypeId: Number(v.serviceTypeId),
      startTime: toZonedIsoString(new Date(v.startTime), chosenDealership.timeZoneId),
    };
    booking.book(request);
  });

  const errorView = booking.error ? describeBookingError(booking.error) : null;
  const suggestions: SlotOption[] =
    errorView && timeZoneId
      ? errorView.suggestedStarts.map((d) => ({ key: d.toISOString(), label: formatTime(d, timeZoneId) }))
      : [];

  const startOver = useCallback(() => {
    resetForm(emptyBookingForm);
    resetBooking();
  }, [resetForm, resetBooking]);

  return {
    form,
    errors: formState.errors,
    minDate,
    vehiclesQuery,
    dealershipsQuery,
    serviceTypesQuery,
    vehicles: vehiclesQuery.data ?? [],
    dealerships: dealershipsQuery.data ?? [],
    serviceTypes: serviceTypesQuery.data ?? [],
    hasDealership: dealershipId !== null,
    onDealershipChange,
    onServiceOrDateChange: clearTime,
    slots,
    slotsState,
    refetchSlots: () => void availabilityQuery.refetch(),
    selectedSlotKey: startKey,
    selectTime,
    summary,
    onSubmit,
    isSaving: booking.isSaving,
    errorView,
    suggestions,
    retry: booking.retry,
    appointment: booking.appointment,
    startOver,
  };
}

export type BookingFormModel = ReturnType<typeof useBookingForm>;
