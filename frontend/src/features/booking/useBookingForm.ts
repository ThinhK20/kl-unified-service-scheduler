import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import type { AssignmentPreview, BookAppointmentRequest, DayStatus, StartTimeStatus } from '../../api/types';
import {
  addMinutes,
  formatIsoDate,
  formatTime,
  formatTimeRange,
  startOfMonthIso,
  todayInBrowserZone,
  todayInZone,
  toZonedIsoString,
} from '../../lib/dateTime';
import { formatDuration } from '../../lib/formatters';
import { queryKeys } from '../../lib/queryKeys';
import { describeBookingError } from './bookingErrors';
import { createBookingSchema, emptyBookingForm, type BookingFormValues } from './bookingSchema';
import { useCustomer } from '../customers/customerContext';
import { useAssignmentPreview } from './useAssignmentPreview';
import { useAvailability, useAvailabilityDays } from './useAvailability';
import { useBookAppointment } from './useBookAppointment';
import { useDealerships, useServiceTypes, useVehicles } from './useCatalog';

export interface SlotOption {
  key: string;
  label: string;
}

/** A start time in the list. Past times are not shown at all. */
export interface TimeOption extends SlotOption {
  status: Exclude<StartTimeStatus, 'Past'>;
}

export type SlotsState = 'idle' | 'loading' | 'error' | 'empty' | 'ready';
export type DaysState = 'idle' | 'loading' | 'error' | 'ready';
/** "Who will serve you": hidden until an Available time is chosen. */
export type AssignmentState = 'hidden' | 'loading' | 'error' | 'ready';

const toId = (value: string): number | null => (value === '' ? null : Number(value));

const NO_NOTE = { trigger: '', text: '' };

function describeStaleDay(isoDate: string, status: DayStatus | null, forCar: boolean): string {
  const day = formatIsoDate(isoDate);
  if (status === 'Past') return `${day} has passed. Please pick another day.`;
  return `${day} is now fully booked${forCar ? ' for this car' : ''}. Please pick another day.`;
}

function describeStaleTime(time: string, status: StartTimeStatus | null): string {
  if (status === 'VehicleBooked') return `${time} is no longer available: this car is already booked then. Please pick another time.`;
  if (status === 'Past') return `${time} has passed. Please pick another time.`;
  return `${time} is no longer available. Please pick another time.`;
}

/**
 * Container hook for the booking page: form state, catalog data, availability,
 * the booking mutation and the text the summary shows. Components only render its output.
 */
export function useBookingForm() {
  const customer = useCustomer();
  const customerId = customer.customerId;
  const vehiclesQuery = useVehicles(customerId);
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
  const vehicleId = toId(values.vehicleId ?? '');
  const date = values.date ?? '';
  const startKey = values.startTime ?? '';

  const serviceTypesQuery = useServiceTypes(dealershipId);

  const vehicle = vehiclesQuery.data?.find((v) => v.id === vehicleId) ?? null;
  const dealership = dealershipsQuery.data?.find((d) => d.id === dealershipId) ?? null;
  const serviceType = serviceTypesQuery.data?.find((s) => s.id === serviceTypeId) ?? null;
  const timeZoneId = dealership?.timeZoneId ?? null;

  const minDate = timeZoneId ? todayInZone(timeZoneId) : todayInBrowserZone();

  // The month the calendar shows: the dealership's current month until the user pages (null), never before it.
  const [visibleMonth, setVisibleMonth] = useState<string | null>(null);
  const calendarMonth =
    visibleMonth === null || startOfMonthIso(visibleMonth) < startOfMonthIso(minDate) ? minDate : visibleMonth;

  const availabilityQuery = useAvailability({ dealershipId, serviceTypeId, date, vehicleId });
  const daysQuery = useAvailabilityDays({ dealershipId, serviceTypeId, month: calendarMonth, vehicleId });

  /** One line shown near the times when the app had to clear the user's pick, and what caused it. */
  const [note, setNote] = useState(NO_NOTE);

  // --- field change rules -------------------------------------------------------------
  const clearTime = useCallback(() => {
    setValue('startTime', '');
    setNote(NO_NOTE);
    resetBooking();
  }, [setValue, resetBooking, setNote]);

  const onDealershipChange = useCallback(() => {
    setValue('serviceTypeId', '');
    clearTime();
  }, [setValue, clearTime]);

  const selectDate = useCallback(
    (isoDate: string) => {
      if (isoDate === date) return;
      setValue('date', isoDate, { shouldValidate: formState.isSubmitted });
      clearTime();
    },
    [setValue, clearTime, formState.isSubmitted, date],
  );

  const selectTime = useCallback(
    (key: string) => {
      setValue('startTime', key, { shouldValidate: formState.isSubmitted });
      setNote(NO_NOTE);
      resetBooking();
    },
    [setValue, resetBooking, setNote, formState.isSubmitted],
  );

  // --- days (calendar) -----------------------------------------------------------------
  const canCheckDays = dealershipId !== null && serviceTypeId !== null;
  const dayStatuses = useMemo(
    () => (daysQuery.data ? new Map(daysQuery.data.days.map((d) => [d.date, d.status] as const)) : undefined),
    [daysQuery.data],
  );
  let daysState: DaysState = 'idle';
  if (canCheckDays) {
    if (daysQuery.isError) daysState = 'error';
    else if (daysQuery.data) daysState = 'ready';
    else daysState = 'loading';
  }

  // --- slots ---------------------------------------------------------------------------
  const times = availabilityQuery.data?.times;
  const slots: TimeOption[] =
    timeZoneId && times
      ? times
          .filter((t): t is typeof t & { status: TimeOption['status'] } => t.status !== 'Past')
          .map((t) => ({ key: t.key, label: formatTime(t.start, timeZoneId), status: t.status }))
      : [];
  const hasFreeTime = slots.some((s) => s.status === 'Available');

  let slotsState: SlotsState = 'idle';
  if (availabilityQuery.isFetching && !availabilityQuery.data) slotsState = 'loading';
  else if (availabilityQuery.isError) slotsState = 'error';
  else if (availabilityQuery.data) slotsState = hasFreeTime ? 'ready' : 'empty';
  if (dealershipId === null || serviceTypeId === null || date === '') slotsState = 'idle';

  // --- keep the pick valid when availability changes (other car, a 409, a refetch) --------
  // The data is keyed by vehicle, so it always describes the car that is chosen now.
  const selectedTimeStatus: StartTimeStatus | null =
    startKey && times ? (times.find((t) => t.key === startKey)?.status ?? null) : null;
  const selectedDayStatus: DayStatus | null = date ? (dayStatuses?.get(date) ?? null) : null;
  // After a 201 the refetch shows the just-booked time as taken; the form is behind the confirmation then.
  const isConfirmed = booking.appointment !== null;
  const staleDay = !isConfirmed && selectedDayStatus !== null && selectedDayStatus !== 'Available';
  const staleTime = !isConfirmed && !staleDay && selectedTimeStatus !== null && selectedTimeStatus !== 'Available' && timeZoneId !== null;
  const staleTrigger = staleDay
    ? `day|${date}|${selectedDayStatus}|${vehicleId}`
    : staleTime
      ? `time|${startKey}|${selectedTimeStatus}|${vehicleId}`
      : '';

  // Record the note while rendering (React's "adjust state when a value changes" pattern) ...
  if (staleTrigger && note.trigger !== staleTrigger) {
    setNote({
      trigger: staleTrigger,
      text: staleDay
        ? describeStaleDay(date, selectedDayStatus, vehicleId !== null)
        : describeStaleTime(formatTime(new Date(startKey), timeZoneId ?? 'UTC'), selectedTimeStatus),
    });
  }
  // ... and clear the form values after the render. The booking error box (and its suggestions) stays.
  useEffect(() => {
    if (staleDay) {
      setValue('date', '');
      setValue('startTime', '');
    } else if (staleTime) {
      setValue('startTime', '');
    }
  }, [staleDay, staleTime, setValue]);

  // --- who will serve you ----------------------------------------------------------------
  // Hidden while a booking error is shown for this time: the pick may be what just failed.
  const assignmentEnabled =
    !isConfirmed && booking.error === null && selectedTimeStatus === 'Available' && timeZoneId !== null;
  const assignmentQuery = useAssignmentPreview({
    dealershipId,
    serviceTypeId,
    startTime: startKey && timeZoneId ? toZonedIsoString(new Date(startKey), timeZoneId) : '',
    vehicleId,
    enabled: assignmentEnabled,
  });
  let assignmentState: AssignmentState = 'hidden';
  if (assignmentEnabled) {
    // Fetching (first load, a refetch after a booking invalidated it, Try again) never shows an old pick.
    if (assignmentQuery.isFetching) assignmentState = 'loading';
    else if (assignmentQuery.isError) assignmentState = 'error';
    else if (assignmentQuery.data) assignmentState = 'ready';
    else assignmentState = 'loading';
  }
  const assignment: AssignmentPreview | null = assignmentState === 'ready' ? (assignmentQuery.data ?? null) : null;
  const expected = assignment?.status === 'Assignable' ? assignment : null;

  // The preview says the time cannot be booked while the list still offers it: refresh the list (and the days),
  // which then shows the time as taken and clears it with a note.
  const queryClient = useQueryClient();
  const previewDisagrees = assignment !== null && assignment.status !== 'Assignable';
  const previewUpdatedAt = assignmentQuery.dataUpdatedAt;
  useEffect(() => {
    if (!previewDisagrees) return;
    void queryClient.invalidateQueries({ queryKey: queryKeys.availabilityAll });
    void queryClient.invalidateQueries({ queryKey: queryKeys.availabilityDaysAll });
  }, [previewDisagrees, previewUpdatedAt, queryClient]);

  // --- summary -------------------------------------------------------------------------
  const start = startKey ? new Date(startKey) : null;
  const end =
    start && serviceType
      ? (times?.find((s) => s.key === startKey)?.end ??
        addMinutes(start, serviceType.durationMinutes))
      : null;

  const summary = {
    vehicle: vehicle?.displayName ?? null,
    dealership: dealership?.name ?? null,
    service: serviceType ? `${serviceType.name} · ${formatDuration(serviceType.durationMinutes)}` : null,
    date: date ? formatIsoDate(date) : null,
    time: start && end && timeZoneId ? formatTimeRange(start, end, timeZoneId) : null,
    technician: expected?.technician ? `${expected.technician.name} (expected)` : null,
    bay: expected?.serviceBay ? `${expected.serviceBay.name} (expected)` : null,
  };

  // --- submit / retry ------------------------------------------------------------------
  const onSubmit = handleSubmit((v) => {
    const chosenDealership = dealershipsQuery.data?.find((d) => String(d.id) === v.dealershipId);
    if (!chosenDealership || customerId === null) return;
    const request: BookAppointmentRequest = {
      customerId,
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
    setNote(NO_NOTE);
    setVisibleMonth(null);
    resetBooking();
  }, [resetForm, resetBooking, setNote, setVisibleMonth]);

  return {
    form,
    errors: formState.errors,
    minDate,
    vehiclesQuery,
    dealershipsQuery,
    customerStatus: customer.status,
    retryCustomers: customer.retry,
    serviceTypesQuery,
    vehicles: vehiclesQuery.data ?? [],
    dealerships: dealershipsQuery.data ?? [],
    serviceTypes: serviceTypesQuery.data ?? [],
    hasDealership: dealershipId !== null,
    onDealershipChange,
    onServiceChange: clearTime,
    calendarMonth,
    onMonthChange: setVisibleMonth,
    dayStatuses,
    daysState,
    refetchDays: () => void daysQuery.refetch(),
    canPickDate: canCheckDays,
    selectDate,
    selectedDate: date,
    slots,
    slotsState,
    availabilityNote: note.text,
    assignmentState,
    assignment,
    refetchAssignment: () => void assignmentQuery.refetch(),
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
