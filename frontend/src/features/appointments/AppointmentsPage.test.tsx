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

// Real clock: "now" is 2026, so 2030 is in the future and 2020 is in the past.
const confirmed = appointmentDto({ id: 7, status: 'Confirmed' }); // 2030-01-15 10:30 local
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
const pastConfirmed = appointmentDto({
  id: 4,
  status: 'Confirmed',
  startUtc: '2020-03-02T02:00:00Z',
  endUtc: '2020-03-02T03:00:00Z',
  serviceType: { id: 3, name: 'EV battery check', durationMinutes: 60 },
});

const card = (name: string) => screen.getByRole('article', { name });
const cancelButton = (name: string) => within(card(name)).getByRole('button', { name: /^Cancel / });

async function renderPage() {
  const user = userEvent.setup();
  const rendered = renderWithProviders(<AppointmentsPage />, { route: '/appointments' });
  await screen.findByRole('article', { name: 'Oil change' });
  return { user, ...rendered };
}

beforeEach(() => {
  mocked.getAppointments.mockResolvedValue([confirmed, completed, cancelled, pastConfirmed].map(api.toAppointment));
});

describe('AppointmentsPage', () => {
  it('shows Cancel only on Confirmed appointments that have not started', async () => {
    await renderPage();
    expect(mocked.getAppointments).toHaveBeenCalledWith(1, expect.anything());

    expect(within(card('Oil change')).getByText(/10:30 – 11:30/)).toBeInTheDocument();
    expect(cancelButton('Oil change')).toBeInTheDocument();
    expect(within(card('Brake inspection')).queryByRole('button')).not.toBeInTheDocument();
    expect(within(card('Tyre rotation')).queryByRole('button')).not.toBeInTheDocument();
    // Confirmed but in the past: no Cancel.
    expect(within(card('EV battery check')).getByText('Confirmed')).toBeInTheDocument();
    expect(within(card('EV battery check')).queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /^Cancel / })).toHaveLength(1);
  });

  it('shows the year on cards that are not in the current year', async () => {
    await renderPage();
    expect(within(card('Oil change')).getByText(/15 Jan 2030 · 10:30/)).toBeInTheDocument();
    expect(within(card('EV battery check')).getByText(/2 Mar 2020 · 09:00/)).toBeInTheDocument();
  });

  it('asks for confirmation inline; "Keep it" and Escape do not cancel', async () => {
    const { user } = await renderPage();

    await user.click(cancelButton('Oil change'));
    const confirm = within(card('Oil change')).getByRole('group', { name: 'Cancel this appointment?' });
    expect(within(confirm).getByRole('button', { name: 'Keep it' })).toHaveFocus();

    await user.click(within(confirm).getByRole('button', { name: 'Keep it' }));
    expect(within(card('Oil change')).queryByRole('group')).not.toBeInTheDocument();
    expect(cancelButton('Oil change')).toHaveFocus();

    await user.click(cancelButton('Oil change'));
    expect(within(card('Oil change')).getByRole('group', { name: 'Cancel this appointment?' })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(within(card('Oil change')).queryByRole('group')).not.toBeInTheDocument();
    expect(cancelButton('Oil change')).toHaveFocus();

    expect(mocked.cancelAppointment).not.toHaveBeenCalled();
  });

  it('"Yes, cancel" calls the API and refreshes the list and free times', async () => {
    mocked.cancelAppointment.mockResolvedValue(api.toAppointment({ ...confirmed, status: 'Cancelled' }));
    const { user, queryClient } = await renderPage();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    mocked.getAppointments.mockResolvedValue(
      [{ ...confirmed, status: 'Cancelled' as const }, completed, cancelled, pastConfirmed].map(api.toAppointment),
    );

    await user.click(cancelButton('Oil change'));
    await user.click(within(card('Oil change')).getByRole('button', { name: 'Yes, cancel' }));

    expect(mocked.cancelAppointment).toHaveBeenCalledWith(7);
    await waitFor(() => expect(within(card('Oil change')).getByText('Cancelled')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /^Cancel / })).not.toBeInTheDocument();
    expect(mocked.getAppointments).toHaveBeenCalledTimes(2);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['appointments'] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['availability'] });
  });

  it('on 409 APPOINTMENT_NOT_CANCELLABLE shows the detail and refreshes the list', async () => {
    mocked.cancelAppointment.mockRejectedValue(
      new ApiError({
        status: 409,
        code: 'APPOINTMENT_NOT_CANCELLABLE',
        title: 'Not cancellable',
        detail: 'This appointment has already started or passed.',
      }),
    );
    const { user } = await renderPage();

    await user.click(cancelButton('Oil change'));
    await user.click(within(card('Oil change')).getByRole('button', { name: 'Yes, cancel' }));

    const alert = await within(card('Oil change')).findByRole('alert');
    expect(alert).toHaveTextContent('This appointment has already started or passed.');
    await waitFor(() => expect(mocked.getAppointments).toHaveBeenCalledTimes(2));
    expect(within(card('Oil change')).queryByRole('group')).not.toBeInTheDocument();
  });

  it('shows an empty state', async () => {
    mocked.getAppointments.mockResolvedValue([]);
    renderWithProviders(<AppointmentsPage />, { route: '/appointments' });
    expect(await screen.findByText('You have no appointments yet.')).toBeInTheDocument();
  });
});
