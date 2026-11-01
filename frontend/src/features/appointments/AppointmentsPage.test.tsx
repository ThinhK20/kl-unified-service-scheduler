import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../../api/appointmentsApi';
import { ApiError } from '../../api/types';
import { appointmentDto } from '../../test/fixtures';
import { renderWithProviders } from '../../test/render';
import { AppointmentsPage } from './AppointmentsPage';

vi.mock('../../api/appointmentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/appointmentsApi')>();
  return { ...actual, getAppointments: vi.fn(), cancelAppointment: vi.fn() };
});

const mocked = vi.mocked(api);

const confirmed = appointmentDto({ id: 7, status: 'Confirmed' });
const completed = appointmentDto({
  id: 6,
  status: 'Completed',
  startUtc: '2029-12-01T07:00:00Z',
  endUtc: '2029-12-01T08:30:00Z',
  serviceType: { id: 2, name: 'Brake inspection', durationMinutes: 90 },
});
const cancelled = appointmentDto({
  id: 5,
  status: 'Cancelled',
  startUtc: '2029-11-01T01:15:00Z',
  endUtc: '2029-11-01T02:15:00Z',
  serviceType: { id: 1, name: 'Tyre rotation', durationMinutes: 60 },
});

const card = (name: string) => screen.getByRole('article', { name });

beforeEach(() => {
  mocked.getAppointments.mockResolvedValue([confirmed, completed, cancelled].map(api.toAppointment));
});

describe('AppointmentsPage', () => {
  it('lists appointments in dealership time with a Cancel button only on Confirmed ones', async () => {
    renderWithProviders(<AppointmentsPage />, { route: '/appointments' });

    await screen.findByRole('article', { name: 'Oil change' });
    expect(mocked.getAppointments).toHaveBeenCalledWith(1, expect.anything());

    expect(within(card('Oil change')).getByText(/10:30 – 11:30/)).toBeInTheDocument();
    expect(within(card('Oil change')).getByText('Confirmed')).toBeInTheDocument();
    expect(within(card('Oil change')).getByRole('button', { name: /Cancel/ })).toBeInTheDocument();
    expect(within(card('Brake inspection')).getByText('Completed')).toBeInTheDocument();
    expect(within(card('Brake inspection')).queryByRole('button')).not.toBeInTheDocument();
    expect(within(card('Tyre rotation')).getByText('Cancelled')).toBeInTheDocument();
    expect(within(card('Tyre rotation')).queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /Cancel/ })).toHaveLength(1);
  });

  it('cancel calls the API and refreshes the list and free times', async () => {
    mocked.cancelAppointment.mockResolvedValue(api.toAppointment({ ...confirmed, status: 'Cancelled' }));
    const user = userEvent.setup();
    const { queryClient } = renderWithProviders(<AppointmentsPage />, { route: '/appointments' });
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    await screen.findByRole('article', { name: 'Oil change' });

    mocked.getAppointments.mockResolvedValue(
      [{ ...confirmed, status: 'Cancelled' as const }, completed, cancelled].map(api.toAppointment),
    );
    await user.click(within(card('Oil change')).getByRole('button', { name: /Cancel/ }));

    expect(mocked.cancelAppointment).toHaveBeenCalledWith(7);
    await waitFor(() => expect(within(card('Oil change')).getByText('Cancelled')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /Cancel/ })).not.toBeInTheDocument();
    expect(mocked.getAppointments).toHaveBeenCalledTimes(2);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['appointments'] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['availability'] });
  });

  it('shows the API message when cancel fails', async () => {
    mocked.cancelAppointment.mockRejectedValue(
      new ApiError({
        status: 409,
        code: 'APPOINTMENT_NOT_CANCELLABLE',
        title: 'Not cancellable',
        detail: 'Only confirmed appointments can be cancelled.',
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<AppointmentsPage />, { route: '/appointments' });
    await screen.findByRole('article', { name: 'Oil change' });

    await user.click(within(card('Oil change')).getByRole('button', { name: /Cancel/ }));

    const alert = await within(card('Oil change')).findByRole('alert');
    expect(alert).toHaveTextContent('Only confirmed appointments can be cancelled.');
  });

  it('shows an empty state', async () => {
    mocked.getAppointments.mockResolvedValue([]);
    renderWithProviders(<AppointmentsPage />, { route: '/appointments' });
    expect(await screen.findByText('You have no appointments yet.')).toBeInTheDocument();
  });
});
