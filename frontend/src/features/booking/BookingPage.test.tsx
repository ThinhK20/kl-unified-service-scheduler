import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../../api/appointmentsApi';
import { ApiError, type Appointment } from '../../api/types';
import * as idempotency from '../../lib/idempotency';
import {
  appointmentDto,
  availabilityDto,
  DATE,
  dealerships,
  serviceTypesByDealer,
  vehicles,
} from '../../test/fixtures';
import { renderWithProviders } from '../../test/render';
import { BookingPage } from './BookingPage';

vi.mock('../../api/appointmentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/appointmentsApi')>();
  return {
    ...actual,
    getDealerships: vi.fn(),
    getServiceTypes: vi.fn(),
    getCustomerVehicles: vi.fn(),
    getAvailability: vi.fn(),
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
  mocked.getDealerships.mockResolvedValue(dealerships);
  mocked.getServiceTypes.mockImplementation(async (id) => serviceTypesByDealer[id] ?? []);
  mocked.getCustomerVehicles.mockResolvedValue(vehicles.map(api.toVehicle));
  mocked.getAvailability.mockResolvedValue(api.toAvailability(availabilityDto));
  mocked.bookAppointment.mockResolvedValue(api.toAppointment(appointmentDto()));
  let n = 0;
  createKey.mockImplementation(() => `generated-key-${++n}`);
});

type User = ReturnType<typeof userEvent.setup>;

async function renderPage() {
  const user = userEvent.setup();
  renderWithProviders(<BookingPage />);
  await screen.findByLabelText('Vehicle');
  return user;
}

async function chooseAllButTime(user: User) {
  await user.selectOptions(screen.getByLabelText('Vehicle'), '1');
  await user.selectOptions(screen.getByLabelText('Service centre'), '1');
  await screen.findByRole('option', { name: 'Oil change · 60 min' });
  await user.selectOptions(screen.getByLabelText('Service type'), '1');
  fireEvent.change(screen.getByLabelText('Date'), { target: { value: DATE } });
  await screen.findByRole('group', { name: 'Free start times' });
}

const timeButton = (label: string) =>
  within(screen.getByRole('group', { name: 'Free start times' })).getByRole('button', { name: label });

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
      { dealershipId: 1, serviceTypeId: 1, date: DATE },
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

  it('shows a message when the day has no free times', async () => {
    mocked.getAvailability.mockResolvedValue(api.toAvailability({ ...availabilityDto, slots: [] }));
    const user = await renderPage();
    await user.selectOptions(screen.getByLabelText('Service centre'), '1');
    await screen.findByRole('option', { name: 'Oil change · 60 min' });
    await user.selectOptions(screen.getByLabelText('Service type'), '1');
    fireEvent.change(screen.getByLabelText('Date'), { target: { value: DATE } });

    expect(await screen.findByText(/No free times on this day/)).toBeInTheDocument();
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
});
