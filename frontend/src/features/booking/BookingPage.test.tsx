import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../../api/appointmentsApi';
import { ApiError, type Appointment } from '../../api/types';
import * as idempotency from '../../lib/idempotency';
import {
  appointmentDto,
  assignmentPreviewDto,
  availabilityDaysDto,
  availabilityDto,
  availabilityWith,
  customers,
  DATE,
  dealerships,
  NOW,
  serviceTypesByDealer,
  vehicles,
} from '../../test/fixtures';
import { renderWithProviders } from '../../test/render';
import { AppHeader } from '../../components/AppHeader';
import { BookingPage } from './BookingPage';
import { useBookingForm } from './useBookingForm';
import { CustomerProvider } from '../customers/CustomerProvider';
import { queryKeys } from '../../lib/queryKeys';

vi.mock('../../api/appointmentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/appointmentsApi')>();
  return {
    ...actual,
    getDealerships: vi.fn(),
    getServiceTypes: vi.fn(),
    getCustomerVehicles: vi.fn(),
    getAvailability: vi.fn(),
    getAvailabilityDays: vi.fn(),
    getAssignmentPreview: vi.fn(),
    bookAppointment: vi.fn(),
  };
});

vi.mock('../../lib/idempotency', () => ({ createIdempotencyKey: vi.fn() }));

const mocked = vi.mocked(api);
const createKey = vi.mocked(idempotency.createIdempotencyKey);

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => {
    resolve = r;
  });
  return { promise, resolve };
}

beforeEach(() => {
  // Only Date is faked: "today" is 10 Jan 2030 in Ho Chi Minh City, so the calendar opens on January 2030.
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(NOW));
  mocked.getAvailabilityDays.mockImplementation(async ({ from, to }) => api.toAvailabilityDays(availabilityDaysDto(from, to)));
  mocked.getDealerships.mockResolvedValue(dealerships);
  mocked.getAssignmentPreview.mockResolvedValue(api.toAssignmentPreview(assignmentPreviewDto()));
  mocked.getServiceTypes.mockImplementation(async (id) => serviceTypesByDealer[id] ?? []);
  mocked.getCustomerVehicles.mockResolvedValue(vehicles.map(api.toVehicle));
  mocked.getAvailability.mockResolvedValue(api.toAvailability(availabilityDto));
  mocked.bookAppointment.mockResolvedValue(api.toAppointment(appointmentDto()));
  let n = 0;
  createKey.mockImplementation(() => `generated-key-${++n}`);
});

afterEach(() => {
  vi.useRealTimers();
});

type User = ReturnType<typeof userEvent.setup>;
let lastRender: ReturnType<typeof renderWithProviders>;

async function renderPage(storage: Storage | null = null) {
  const user = userEvent.setup();
  lastRender = renderWithProviders(<BookingPage />, { storage });
  await screen.findByLabelText('Vehicle');
  return user;
}

/** The day button in the calendar, by the start of its accessible name, e.g. "Tue 15 Jan 2030". */
const dayButton = (label: string) => screen.getByRole('button', { name: new RegExp(`^(Today, )?${label}`) });

async function chooseDealershipAndService(user: User) {
  await user.selectOptions(screen.getByLabelText('Service centre'), '1');
  await screen.findByRole('option', { name: 'Oil change · 60 min' });
  await user.selectOptions(screen.getByLabelText('Service type'), '1');
  // The calendar knows the days once the month is loaded.
  await screen.findByRole('button', { name: 'Fri 18 Jan 2030' });
}

async function chooseAllButTime(user: User) {
  await user.selectOptions(screen.getByLabelText('Vehicle'), '1');
  await chooseDealershipAndService(user);
  await user.click(dayButton('Tue 15 Jan 2030'));
  await screen.findByRole('group', { name: 'Start times' });
}

const timesGroup = () => screen.getByRole('group', { name: 'Start times' });
const timeButton = (label: string) => within(timesGroup()).getByRole('button', { name: label });

const summaryValue = (term: string) => {
  const summary = screen.getByLabelText('Booking summary');
  const dt = within(summary).getByText(term);
  return dt.nextElementSibling?.textContent;
};

describe('BookingPage', () => {
  it('does not submit without the required fields and shows what is missing', async () => {
    const user = await renderPage();

    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    expect(await screen.findByText('Please choose a vehicle')).toBeInTheDocument();
    expect(screen.getByText('Please choose a dealership')).toBeInTheDocument();
    expect(screen.getByText('Please choose a service')).toBeInTheDocument();
    expect(screen.getByText('Please choose a date')).toBeInTheDocument();
    expect(screen.getByText('Please choose a start time')).toBeInTheDocument();
    expect(mocked.bookAppointment).not.toHaveBeenCalled();
  });

  it('shows free times in dealership time and selecting one updates the summary', async () => {
    const user = await renderPage();
    await chooseAllButTime(user);

    expect(mocked.getAvailability).toHaveBeenLastCalledWith(
      { dealershipId: 1, serviceTypeId: 1, date: DATE, vehicleId: 1 },
      expect.anything(),
    );
    // 03:00Z, 03:30Z, 04:00Z are 10:00, 10:30, 11:00 in Ho Chi Minh City (tests run in New York time).
    expect(timeButton('10:00')).toHaveAttribute('aria-pressed', 'false');
    expect(timeButton('11:00')).toBeInTheDocument();

    await user.click(timeButton('10:30'));

    expect(timeButton('10:30')).toHaveAttribute('aria-pressed', 'true');
    expect(summaryValue('Time')).toBe('10:30 – 11:30');
    expect(summaryValue('Service')).toBe('Oil change · 60 min');
    expect(summaryValue('Date')).toBe('Tue 15 Jan 2030');
    expect(summaryValue('Vehicle')).toBe('Toyota Vios 2021');
  });

  it('sends startTime with the dealership offset and shows the confirmation card', async () => {
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    expect(await screen.findByRole('heading', { name: "You're booked" })).toBeInTheDocument();
    expect(mocked.bookAppointment).toHaveBeenCalledWith(
      { customerId: 1, vehicleId: 1, dealershipId: 1, serviceTypeId: 1, startTime: '2030-01-15T10:30:00+07:00' },
      expect.any(String),
    );
    expect(screen.getByText('Minh')).toBeInTheDocument();
    expect(screen.getByText('Bay 1')).toBeInTheDocument();
    expect(screen.getByText('#42')).toBeInTheDocument();
    expect(screen.getByText('Confirmed')).toBeInTheDocument();
  });

  it('books for the customer chosen in "Booking as"', async () => {
    const storage = { getItem: () => '3', setItem: () => undefined } as unknown as Storage;
    const user = await renderPage(storage);
    expect(mocked.getCustomerVehicles).toHaveBeenCalledWith(3, expect.anything());
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    await screen.findByRole('heading', { name: "You're booked" });
    expect(mocked.bookAppointment).toHaveBeenCalledWith(expect.objectContaining({ customerId: 3, vehicleId: 1 }), expect.any(String));
  });

  it('disables the Book button while saving', async () => {
    const pending = deferred<Appointment>();
    mocked.bookAppointment.mockReturnValue(pending.promise);
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:00'));
    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    expect(await screen.findByRole('button', { name: 'Booking…' })).toBeDisabled();
    pending.resolve(api.toAppointment(appointmentDto()));
    expect(await screen.findByRole('heading', { name: "You're booked" })).toBeInTheDocument();
  });

  it('shows a message when the day has no free times, with the taken times under it', async () => {
    mocked.getAvailability.mockResolvedValue(api.toAvailability(availabilityWith(['Booked', 'Booked', 'VehicleBooked'])));
    const user = await renderPage();
    await chooseDealershipAndService(user);
    await user.click(dayButton('Tue 15 Jan 2030'));

    expect(await screen.findByText(/No free times on this day/)).toBeInTheDocument();
    const buttons = within(timesGroup()).getAllByRole('button');
    expect(buttons.map((b) => b.getAttribute('aria-label'))).toEqual([
      '10:00, booked',
      '10:30, booked',
      '11:00, your car is booked',
    ]);
    expect(buttons.every((b) => b.getAttribute('aria-disabled') === 'true')).toBe(true);
  });

  it('changing the dealership resets the service and the time', async () => {
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));

    await user.selectOptions(screen.getByLabelText('Service centre'), '2');

    expect(screen.getByLabelText('Service type')).toHaveValue('');
    expect(summaryValue('Time')).toBe('Not chosen yet');
    expect(summaryValue('Service')).toBe('Not chosen yet');
  });

  it('on 409 NO_AVAILABILITY shows suggested times in local time, and clicking one selects it', async () => {
    mocked.bookAppointment.mockRejectedValueOnce(
      new ApiError({
        status: 409,
        code: 'NO_AVAILABILITY',
        title: 'No availability',
        suggestedStartTimes: ['2030-01-15T04:00:00Z', '2030-01-15T05:15:00Z'],
      }),
    );
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('This time was just taken')).toBeInTheDocument();
    const suggestions = within(alert).getByRole('group', { name: 'Suggested start times' });
    expect(within(suggestions).getAllByRole('button').map((b) => b.textContent)).toEqual(['11:00', '12:15']);

    await user.click(within(suggestions).getByRole('button', { name: '11:00' }));

    expect(summaryValue('Time')).toBe('11:00 – 12:00');
    expect(timeButton('11:00')).toHaveAttribute('aria-pressed', 'true');
    expect(timeButton('10:30')).toHaveAttribute('aria-pressed', 'false');
    // The free-times list is refreshed after a conflict.
    await waitFor(() => expect(mocked.getAvailability.mock.calls.length).toBeGreaterThanOrEqual(2));

    await user.click(screen.getByRole('button', { name: 'Book appointment' }));
    await screen.findByRole('heading', { name: "You're booked" });
    expect(mocked.bookAppointment).toHaveBeenLastCalledWith(
      expect.objectContaining({ startTime: '2030-01-15T11:00:00+07:00' }),
      expect.any(String),
    );
  });

  it('after 503 "Try again" reuses the same Idempotency-Key; a new submit gets a new key', async () => {
    createKey.mockReturnValueOnce('key-1').mockReturnValueOnce('key-2');
    const busy = new ApiError({ status: 503, code: 'DATABASE_BUSY', title: 'Busy' });
    mocked.bookAppointment.mockRejectedValueOnce(busy).mockRejectedValueOnce(busy).mockRejectedValueOnce(busy);

    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText("We're busy right now")).toBeInTheDocument();
    await user.click(within(alert).getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(mocked.bookAppointment).toHaveBeenCalledTimes(2));

    const [first, second] = mocked.bookAppointment.mock.calls;
    expect(second?.[1]).toBe('key-1');
    expect(first?.[1]).toBe('key-1');
    expect(second?.[0]).toEqual(first?.[0]);

    // The user changes the time and submits again: that is a new submit with a new key.
    await screen.findByText("We're busy right now");
    await user.click(timeButton('11:00'));
    await user.click(screen.getByRole('button', { name: 'Book appointment' }));
    await waitFor(() => expect(mocked.bookAppointment).toHaveBeenCalledTimes(3));
    expect(mocked.bookAppointment.mock.calls[2]?.[1]).toBe('key-2');
    expect(createKey).toHaveBeenCalledTimes(2);
  });

  it.each([
    [422, 'BOOKING_RULE_VIOLATION', "This booking isn't allowed", 'The time is outside opening hours.'],
    [404, 'NOT_FOUND', 'Something in this booking was not found', 'Vehicle 1 was not found.'],
    [400, 'INVALID_REQUEST', 'Please check your booking', 'The start time must be in the future.'],
    [409, 'IDEMPOTENCY_CONFLICT', 'This booking request was already used', 'Key reused with a different body.'],
  ] as const)('shows the detail for %i %s', async (status, code, title, detail) => {
    mocked.bookAppointment.mockRejectedValueOnce(new ApiError({ status, code, title: 'Problem', detail }));
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText(title)).toBeInTheDocument();
    expect(within(alert).getByText(detail)).toBeInTheDocument();
    expect(within(alert).queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
  });

  it('on 409 VEHICLE_ALREADY_BOOKED shows the detail and no suggested times', async () => {
    mocked.bookAppointment.mockRejectedValueOnce(
      new ApiError({
        status: 409,
        code: 'VEHICLE_ALREADY_BOOKED',
        title: 'Vehicle already booked',
        detail: 'This vehicle already has a booking at that time.',
        // Even if the API sent some, they must not be offered for this error.
        suggestedStartTimes: ['2030-01-15T04:00:00Z'],
      }),
    );
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('This vehicle already has a booking at that time')).toBeInTheDocument();
    expect(within(alert).getByText('This vehicle already has a booking at that time.')).toBeInTheDocument();
    expect(within(alert).getByText('Please pick another time or another vehicle.')).toBeInTheDocument();
    expect(within(alert).queryByText('This time was just taken')).not.toBeInTheDocument();
    expect(within(alert).queryByRole('group', { name: 'Suggested start times' })).not.toBeInTheDocument();
    expect(within(alert).queryByRole('button')).not.toBeInTheDocument();
    // The times and days are refetched so the car's busy times show as such.
    await waitFor(() => expect(mocked.getAvailability).toHaveBeenCalledTimes(2));
    expect(mocked.getAvailabilityDays).toHaveBeenCalledTimes(2);
  });

  it('shows a network error with a Try again button', async () => {
    mocked.bookAppointment.mockRejectedValueOnce(ApiError.network(new TypeError('Failed to fetch')));
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText("We can't reach the server")).toBeInTheDocument();
    expect(within(alert).getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('on 409 NO_AVAILABILITY refetches the times and the days; the taken time shows as Booked and is cleared', async () => {
    mocked.bookAppointment.mockRejectedValueOnce(
      new ApiError({ status: 409, code: 'NO_AVAILABILITY', title: 'No availability', suggestedStartTimes: ['2030-01-15T04:00:00Z'] }),
    );
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    mocked.getAvailability.mockResolvedValue(api.toAvailability(availabilityWith(['Available', 'Booked', 'Available'])));
    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    expect(await within(timesGroup()).findByRole('button', { name: '10:30, booked' })).toHaveAttribute('aria-disabled', 'true');
    expect(mocked.getAvailabilityDays).toHaveBeenCalledTimes(2);
    expect(screen.getByText('10:30 is no longer available. Please pick another time.')).toBeInTheDocument();
    expect(summaryValue('Time')).toBe('Not chosen yet');
    // The suggestions box stays.
    const suggestions = within(screen.getByRole('alert')).getByRole('group', { name: 'Suggested start times' });
    await user.click(within(suggestions).getByRole('button', { name: '11:00' }));
    expect(summaryValue('Time')).toBe('11:00 – 12:00');
    expect(screen.queryByText(/is no longer available/)).not.toBeInTheDocument();
  });
});

describe('BookingPage calendar', () => {
  it('is disabled until a dealership and a service are chosen', async () => {
    const user = await renderPage();
    expect(screen.getByText('Choose a dealership and a service first.')).toBeInTheDocument();
    expect(dayButton('Tue 15 Jan 2030')).toBeDisabled();
    expect(mocked.getAvailabilityDays).not.toHaveBeenCalled();

    await chooseDealershipAndService(user);

    expect(screen.queryByText('Choose a dealership and a service first.')).not.toBeInTheDocument();
    expect(dayButton('Tue 15 Jan 2030')).toBeEnabled();
    expect(mocked.getAvailabilityDays).toHaveBeenLastCalledWith(
      { dealershipId: 1, serviceTypeId: 1, from: '2030-01-01', to: '2030-01-31', vehicleId: null },
      expect.anything(),
    );
  });

  it('marks past and fully booked days: focusable, announced with the reason, not selectable', async () => {
    mocked.getAvailabilityDays.mockImplementation(async ({ from, to }) =>
      api.toAvailabilityDays(availabilityDaysDto(from, to, { '2030-01-18': 'FullyBooked' })),
    );
    const user = await renderPage();
    await user.selectOptions(screen.getByLabelText('Service centre'), '1');
    await screen.findByRole('option', { name: 'Oil change · 60 min' });
    await user.selectOptions(screen.getByLabelText('Service type'), '1');

    const full = await screen.findByRole('button', { name: 'Fri 18 Jan 2030, fully booked' });
    expect(full).toHaveAttribute('aria-disabled', 'true');
    expect(full).not.toBeDisabled();
    const past = screen.getByRole('button', { name: 'Wed 9 Jan 2030, past date' });
    expect(past).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('button', { name: 'Today, Thu 10 Jan 2030' })).not.toHaveAttribute('aria-disabled');
    expect(screen.getByRole('list', { name: 'Calendar legend' })).toHaveTextContent(/Fully booked/);

    await user.click(full);
    await user.click(past);

    expect(summaryValue('Date')).toBe('Not chosen yet');
    expect(mocked.getAvailability).not.toHaveBeenCalled();
  });

  it('moves with the arrow keys (also onto unavailable days) and selects with Enter', async () => {
    mocked.getAvailabilityDays.mockImplementation(async ({ from, to }) =>
      api.toAvailabilityDays(availabilityDaysDto(from, to, { '2030-01-16': 'FullyBooked' })),
    );
    const user = await renderPage();
    await chooseDealershipAndService(user);

    dayButton('Tue 15 Jan 2030').focus();
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('button', { name: 'Wed 16 Jan 2030, fully booked' })).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(summaryValue('Date')).toBe('Not chosen yet');

    await user.keyboard('{ArrowDown}');
    expect(dayButton('Wed 23 Jan 2030')).toHaveFocus();
    await user.keyboard('{Enter}');

    expect(summaryValue('Date')).toBe('Wed 23 Jan 2030');
    expect(dayButton('Wed 23 Jan 2030')).toHaveAccessibleName('Wed 23 Jan 2030, selected');
    expect(mocked.getAvailability).toHaveBeenLastCalledWith(
      { dealershipId: 1, serviceTypeId: 1, date: '2030-01-23', vehicleId: null },
      expect.anything(),
    );
  });

  it('clicking the selected day again keeps the chosen time', async () => {
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));

    await user.click(dayButton('Tue 15 Jan 2030'));

    expect(summaryValue('Time')).toBe('10:30 – 11:30');
    expect(timeButton('10:30')).toHaveAttribute('aria-pressed', 'true');
    expect(mocked.getAvailability).toHaveBeenCalledTimes(1);
  });

  it("opens on the dealership's month even when the browser is already in the next month", async () => {
    // 19:00 on 31 Jan in Ho Chi Minh City is already 1 Feb in Kiritimati (UTC+14).
    vi.stubEnv('TZ', 'Pacific/Kiritimati');
    vi.setSystemTime(new Date('2030-01-31T12:00:00Z'));
    try {
      expect(new Date().getDate()).toBe(1);
      const user = await renderPage();
      await user.selectOptions(screen.getByLabelText('Service centre'), '1');
      await screen.findByRole('option', { name: 'Oil change · 60 min' });
      await user.selectOptions(screen.getByLabelText('Service type'), '1');

      expect(await screen.findByRole('button', { name: /^Today, Thu 31 Jan 2030/ })).toBeInTheDocument();
      expect(mocked.getAvailabilityDays).toHaveBeenLastCalledWith(
        expect.objectContaining({ from: '2030-01-01', to: '2030-01-31' }),
        expect.anything(),
      );
    } finally {
      vi.unstubAllEnvs();
    }
  });

  it('cannot page before this month; paging forward loads that month', async () => {
    const user = await renderPage();
    await chooseDealershipAndService(user);

    expect(screen.getByRole('button', { name: /previous month/i })).toHaveAttribute('aria-disabled', 'true');
    await user.click(screen.getByRole('button', { name: /next month/i }));
    await user.click(screen.getByRole('button', { name: /next month/i }));

    expect(await screen.findByRole('button', { name: 'Fri 1 Mar 2030' })).toBeInTheDocument();
    expect(mocked.getAvailabilityDays).toHaveBeenLastCalledWith(
      expect.objectContaining({ from: '2030-03-01', to: '2030-03-31' }),
      expect.anything(),
    );
    expect(screen.getByRole('button', { name: /previous month/i })).not.toHaveAttribute('aria-disabled');
  });

  it('shows an error with Try again when the month cannot be loaded, and still lets the user pick a date', async () => {
    mocked.getAvailabilityDays.mockRejectedValueOnce(ApiError.network(new TypeError('Failed to fetch')));
    const user = await renderPage();
    await user.selectOptions(screen.getByLabelText('Service centre'), '1');
    await screen.findByRole('option', { name: 'Oil change · 60 min' });
    await user.selectOptions(screen.getByLabelText('Service type'), '1');

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('We could not check which days are free')).toBeInTheDocument();
    await user.click(dayButton('Tue 15 Jan 2030'));
    expect(summaryValue('Date')).toBe('Tue 15 Jan 2030');

    await user.click(within(alert).getByRole('button', { name: 'Try again' }));
    await screen.findByRole('button', { name: 'Fri 18 Jan 2030' });
    expect(screen.queryByText('We could not check which days are free')).not.toBeInTheDocument();
  });
});

describe('BookingPage start times', () => {
  it('shows taken times disabled with the reason, and hides past times', async () => {
    mocked.getAvailability.mockResolvedValue(api.toAvailability(availabilityWith(['Booked', 'VehicleBooked', 'Available'])));
    const user = await renderPage();
    await chooseAllButTime(user);

    const group = timesGroup();
    const booked = within(group).getByRole('button', { name: '10:00, booked' });
    expect(booked).toHaveAttribute('aria-disabled', 'true');
    expect(booked).not.toBeDisabled(); // focusable, so keyboard users hear the reason
    expect(booked).toHaveTextContent('10:00Booked');
    expect(booked).toHaveClass('slot--booked');
    const carBooked = within(group).getByRole('button', { name: '10:30, your car is booked' });
    expect(carBooked).toHaveAttribute('aria-disabled', 'true');
    expect(carBooked).toHaveTextContent('Your car is booked');
    expect(timeButton('11:00')).toBeEnabled();
    expect(within(group).queryByText('09:30')).not.toBeInTheDocument();
    expect(within(group).getAllByRole('button')).toHaveLength(3);

    // Taken times can be reached with Tab and clicking them does nothing.
    await user.click(booked);
    await user.click(carBooked);
    expect(summaryValue('Time')).toBe('Not chosen yet');
    timeButton('11:00').focus();
    await user.tab({ shift: true });
    expect(carBooked).toHaveFocus();
  });

  it('asks for availability with the chosen car; a car change that makes the time unavailable clears it with a note', async () => {
    mocked.getAvailability.mockImplementation(async ({ vehicleId }) =>
      api.toAvailability(vehicleId === 2 ? availabilityWith(['Available', 'VehicleBooked', 'Available']) : availabilityDto),
    );
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:00'));
    await user.click(timeButton('10:30'));

    await user.selectOptions(screen.getByLabelText('Vehicle'), '2');

    expect(await screen.findByText('10:30 is no longer available: this car is already booked then. Please pick another time.')).toBeInTheDocument();
    expect(within(timesGroup()).getByRole('button', { name: '10:30, your car is booked' })).toHaveAttribute('aria-disabled', 'true');
    expect(summaryValue('Time')).toBe('Not chosen yet');
    expect(summaryValue('Vehicle')).toBe('Honda CR-V 2023');
    expect(mocked.getAvailability).toHaveBeenLastCalledWith(expect.objectContaining({ vehicleId: 2 }), expect.anything());
    expect(mocked.getAvailabilityDays).toHaveBeenLastCalledWith(expect.objectContaining({ vehicleId: 2 }), expect.anything());

    await user.click(timeButton('11:00'));
    expect(screen.queryByText(/is no longer available/)).not.toBeInTheDocument();
  });

  it('keeps the time when it is still free for the new car', async () => {
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('11:00'));

    await user.selectOptions(screen.getByLabelText('Vehicle'), '2');

    await waitFor(() => expect(mocked.getAvailability).toHaveBeenLastCalledWith(expect.objectContaining({ vehicleId: 2 }), expect.anything()));
    expect(await within(await screen.findByRole('group', { name: 'Start times' })).findByRole('button', { name: '11:00' })).toHaveAttribute('aria-pressed', 'true');
    expect(summaryValue('Time')).toBe('11:00 – 12:00');
    expect(screen.queryByText(/no longer available/)).not.toBeInTheDocument();
  });

  it('clears the date with a note when it becomes fully booked for the new car', async () => {
    mocked.getAvailabilityDays.mockImplementation(async ({ from, to, vehicleId }) =>
      api.toAvailabilityDays(availabilityDaysDto(from, to, vehicleId === 2 ? { [DATE]: 'FullyBooked' } : {})),
    );
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));

    await user.selectOptions(screen.getByLabelText('Vehicle'), '2');

    expect(await screen.findByText('Tue 15 Jan 2030 is now fully booked for this car. Please pick another day.')).toBeInTheDocument();
    expect(summaryValue('Date')).toBe('Not chosen yet');
    expect(summaryValue('Time')).toBe('Not chosen yet');
    expect(dayButton('Tue 15 Jan 2030')).toHaveAccessibleName('Tue 15 Jan 2030, fully booked');
  });
});

describe('useBookingForm after a successful booking', () => {
  it('does not clear the booked time when the refetch shows it as taken', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
    queryClient.setQueryData(queryKeys.customers, customers);
    const wrapper = ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={queryClient}>
        <CustomerProvider storage={null}>{children}</CustomerProvider>
      </QueryClientProvider>
    );
    let booked = false;
    mocked.bookAppointment.mockImplementation(async () => {
      booked = true;
      return api.toAppointment(appointmentDto());
    });
    mocked.getAvailability.mockImplementation(async () =>
      api.toAvailability(booked ? availabilityWith(['Available', 'Booked', 'Available']) : availabilityDto),
    );
    const { result } = renderHook(() => useBookingForm(), { wrapper });
    await waitFor(() => expect(result.current.dealerships).toHaveLength(2));
    await waitFor(() => expect(result.current.vehicles).toHaveLength(2));

    act(() => {
      result.current.form.setValue('vehicleId', '1');
      result.current.form.setValue('dealershipId', '1');
      result.current.form.setValue('serviceTypeId', '1');
    });
    act(() => result.current.selectDate(DATE));
    await waitFor(() => expect(result.current.slotsState).toBe('ready'));
    act(() => result.current.selectTime('2030-01-15T03:30:00.000Z'));
    await act(async () => {
      await result.current.onSubmit();
    });

    await waitFor(() => expect(result.current.appointment).not.toBeNull());
    await waitFor(() => expect(mocked.getAvailability).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(result.current.slots.find((s) => s.key === '2030-01-15T03:30:00.000Z')?.status).toBe('Booked'));
    expect(result.current.form.getValues('startTime')).toBe('2030-01-15T03:30:00.000Z');
    expect(result.current.availabilityNote).toBe('');
  });
});

describe('BookingPage "Who will serve you"', () => {
  const panel = () => screen.getByRole('region', { name: 'Who will serve you' });
  const rows = (list: string) =>
    within(within(panel()).getByRole('list', { name: list }))
      .getAllByRole('listitem')
      .map((li) => li.textContent);

  it('stays hidden until an available time is chosen, then asks with dealership, service, time and car', async () => {
    const user = await renderPage();
    await chooseAllButTime(user);
    expect(screen.queryByRole('region', { name: 'Who will serve you' })).not.toBeInTheDocument();
    expect(mocked.getAssignmentPreview).not.toHaveBeenCalled();

    await user.click(timeButton('10:30'));

    expect(await within(await screen.findByRole('region', { name: 'Who will serve you' })).findByText('Expected pick — confirmed when you book.')).toBeInTheDocument();
    expect(mocked.getAssignmentPreview).toHaveBeenLastCalledWith(
      { dealershipId: 1, serviceTypeId: 1, startTime: '2030-01-15T10:30:00+07:00', vehicleId: 1 },
      expect.anything(),
    );
  });

  it('lists every technician and bay with its state, highlights the pick and adds it to the summary', async () => {
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    await within(await screen.findByRole('region', { name: 'Who will serve you' })).findByRole('list', { name: 'Technician' });

    expect(rows('Technician')).toEqual([
      'Minh: Will serve you',
      'Lan: Free',
      'Nam: Busy at this time',
      'Thao: Off shift at 10:30',
      'Vy: Not qualified',
      'Tuan: Inactive',
    ]);
    expect(rows('Service bay')).toEqual([
      'Bay 1 · Standard: Will serve you',
      'Bay 5 · Standard: Busy at this time',
      'Bay 2 · Lift: Wrong bay type',
      'Bay 4 · Lift: Inactive',
    ]);
    const technicianList = within(panel()).getByRole('list', { name: 'Technician' });
    expect(within(technicianList).getByText('Minh').closest('li')).toHaveClass('assign__row--pick');
    expect(within(technicianList).getByText('Tuan').closest('li')).toHaveClass('assign__row--inactive');
    expect(summaryValue('Technician')).toBe('Minh (expected)');
    expect(summaryValue('Bay')).toBe('Bay 1 (expected)');
  });

  it('says why when the time cannot be assigned, without a pick in the summary', async () => {
    mocked.getAssignmentPreview.mockResolvedValue(
      api.toAssignmentPreview(
        assignmentPreviewDto({
          status: 'NoAvailability',
          technician: null,
          serviceBay: null,
          technicians: [{ id: 3, name: 'Minh', state: 'Busy' }],
          serviceBays: [{ id: 1, name: 'Bay 1', bayType: 'Standard', state: 'Busy' }],
        }),
      ),
    );
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));

    expect(await screen.findByText('Nobody is free for the whole time any more. Please pick another time.')).toBeInTheDocument();
    expect(rows('Technician')).toEqual(['Minh: Busy at this time']);
    expect(within(panel()).queryByText('Will serve you')).not.toBeInTheDocument();
    expect(within(screen.getByLabelText('Booking summary')).queryByText('Technician')).not.toBeInTheDocument();
  });

  it('shows loading, then an error with Try again', async () => {
    const pending = deferred<Awaited<ReturnType<typeof api.getAssignmentPreview>>>();
    mocked.getAssignmentPreview.mockReturnValueOnce(pending.promise);
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));

    expect(await within(panel()).findByText('Checking who is free…')).toBeInTheDocument();
    mocked.getAssignmentPreview.mockRejectedValueOnce(ApiError.network(new TypeError('Failed to fetch')));
    await user.click(timeButton('11:00'));
    const alert = await within(panel()).findByRole('alert');
    expect(within(alert).getByText('We could not check who will serve you')).toBeInTheDocument();

    const retry = deferred<Awaited<ReturnType<typeof api.getAssignmentPreview>>>();
    mocked.getAssignmentPreview.mockReturnValueOnce(retry.promise);
    await user.click(within(alert).getByRole('button', { name: 'Try again' }));
    expect(await within(panel()).findByText('Checking who is free…')).toBeInTheDocument();
    expect(within(panel()).queryByRole('alert')).not.toBeInTheDocument();
    retry.resolve(api.toAssignmentPreview(assignmentPreviewDto()));
    expect(await within(panel()).findByText('Expected pick — confirmed when you book.')).toBeInTheDocument();
  });

  it.each([
    ['409 NO_AVAILABILITY', new ApiError({ status: 409, code: 'NO_AVAILABILITY', title: 'No availability' })],
    ['409 IDEMPOTENCY_CONFLICT', new ApiError({ status: 409, code: 'IDEMPOTENCY_CONFLICT', title: 'Conflict' })],
    ['201', null],
  ] as const)('invalidates the expected pick with the availability after %s', async (_name, error) => {
    if (error) mocked.bookAppointment.mockRejectedValueOnce(error);
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    await within(await screen.findByRole('region', { name: 'Who will serve you' })).findByRole('list', { name: 'Technician' });
    const invalidate = vi.spyOn(lastRender.queryClient, 'invalidateQueries');

    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: ['assignment-preview'] }));
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['availability'] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['availability-days'] });
    if (error) await waitFor(() => expect(mocked.getAssignmentPreview).toHaveBeenCalledTimes(2));
    else expect(await screen.findByRole('heading', { name: "You're booked" })).toBeInTheDocument();
  });

  it('announces the result politely, with the pick for screen readers', async () => {
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    const status = await within(await screen.findByRole('region', { name: 'Who will serve you' })).findByText('Minh and Bay 1.');
    expect(status.closest('[role="status"]')).toHaveTextContent('Expected pick — confirmed when you book.Minh and Bay 1.');
  });

  it('asks again when the car or the time changes', async () => {
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    await within(await screen.findByRole('region', { name: 'Who will serve you' })).findByRole('list', { name: 'Technician' });

    await user.click(timeButton('11:00'));
    await waitFor(() =>
      expect(mocked.getAssignmentPreview).toHaveBeenLastCalledWith(
        expect.objectContaining({ startTime: '2030-01-15T11:00:00+07:00', vehicleId: 1 }),
        expect.anything(),
      ),
    );
    await user.selectOptions(screen.getByLabelText('Vehicle'), '2');
    await waitFor(() =>
      expect(mocked.getAssignmentPreview).toHaveBeenLastCalledWith(
        expect.objectContaining({ startTime: '2030-01-15T11:00:00+07:00', vehicleId: 2 }),
        expect.anything(),
      ),
    );
    expect(await within(panel()).findByRole('list', { name: 'Technician' })).toBeInTheDocument();
  });

  it('after a 409 hides the old pick and drops the expected rows from the summary', async () => {
    mocked.bookAppointment.mockRejectedValueOnce(new ApiError({ status: 409, code: 'IDEMPOTENCY_CONFLICT', title: 'Conflict' }));
    const user = await renderPage();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    await within(await screen.findByRole('region', { name: 'Who will serve you' })).findByRole('list', { name: 'Technician' });
    expect(summaryValue('Technician')).toBe('Minh (expected)');

    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    await screen.findByText('This booking request was already used');
    expect(screen.queryByRole('region', { name: 'Who will serve you' })).not.toBeInTheDocument();
    expect(screen.queryByText('Will serve you')).not.toBeInTheDocument();
    const summary = screen.getByLabelText('Booking summary');
    expect(within(summary).queryByText('Technician')).not.toBeInTheDocument();
    expect(within(summary).queryByText('Bay')).not.toBeInTheDocument();

    // Picking the time again clears the error and asks again.
    await user.click(timeButton('11:00'));
    expect(await within(await screen.findByRole('region', { name: 'Who will serve you' })).findByRole('list', { name: 'Technician' })).toBeInTheDocument();
  });

  it('refreshes the times and days when the preview says the time is no longer bookable', async () => {
    let refreshed = false;
    mocked.getAssignmentPreview.mockImplementation(async () => {
      refreshed = true;
      return api.toAssignmentPreview(
        assignmentPreviewDto({ status: 'VehicleBooked', technician: null, serviceBay: null }),
      );
    });
    const user = await renderPage();
    await chooseAllButTime(user);
    mocked.getAvailability.mockImplementation(async () =>
      api.toAvailability(refreshed ? availabilityWith(['Available', 'VehicleBooked', 'Available']) : availabilityDto),
    );
    await user.click(timeButton('10:30'));

    await waitFor(() => expect(mocked.getAvailabilityDays).toHaveBeenCalledTimes(2));
    expect(
      await screen.findByText('10:30 is no longer available: this car is already booked then. Please pick another time.'),
    ).toBeInTheDocument();
    expect(within(timesGroup()).getByRole('button', { name: '10:30, your car is booked' })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.queryByRole('region', { name: 'Who will serve you' })).not.toBeInTheDocument();
  });
});

describe('Booking as while a booking is being saved', () => {
  it('locks the customer switcher with a hint, so the confirmation is not lost', async () => {
    const pending = deferred<Appointment>();
    mocked.bookAppointment.mockReturnValue(pending.promise);
    const user = userEvent.setup();
    renderWithProviders(
      <>
        <AppHeader />
        <BookingPage />
      </>,
    );
    await screen.findByLabelText('Vehicle');
    const switcher = screen.getByRole('combobox', { name: 'Booking as' });
    expect(switcher).toBeEnabled();
    await chooseAllButTime(user);
    await user.click(timeButton('10:30'));
    await user.click(screen.getByRole('button', { name: 'Book appointment' }));

    await waitFor(() => expect(switcher).toBeDisabled());
    expect(switcher).toHaveAccessibleDescription('Saving your booking…');

    pending.resolve(api.toAppointment(appointmentDto()));
    expect(await screen.findByRole('heading', { name: "You're booked" })).toBeInTheDocument();
    await waitFor(() => expect(switcher).toBeEnabled());
    expect(switcher).not.toHaveAccessibleDescription();
  });
});
