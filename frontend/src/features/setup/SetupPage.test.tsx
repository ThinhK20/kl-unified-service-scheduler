import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../../api/appointmentsApi';
import { ApiError, type DealershipSetupDto } from '../../api/types';
import { dealerships } from '../../test/fixtures';
import { renderWithProviders } from '../../test/render';
import { SetupPage } from './SetupPage';

vi.mock('../../api/appointmentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/appointmentsApi')>();
  return { ...actual, getDealerships: vi.fn(), getDealershipSetup: vi.fn() };
});

const mocked = vi.mocked(api);

const district1: DealershipSetupDto = {
  dealership: { ...dealerships[0]!, isActive: true },
  serviceTypes: [
    { id: 1, name: 'Oil change', durationMinutes: 60, requiredSkill: 'General', minimumTier: 'Trainee', requiredBayType: 'Standard', isActive: true, qualifiedTechnicianIds: [1, 2], matchingBayIds: [1] },
    { id: 4, name: 'EV battery check', durationMinutes: 60, requiredSkill: 'EV', minimumTier: 'Senior', requiredBayType: 'EV', isActive: true, qualifiedTechnicianIds: [2], matchingBayIds: [3] },
    { id: 6, name: 'Paint repair', durationMinutes: 120, requiredSkill: 'Diagnostics', minimumTier: 'Master', requiredBayType: 'Lift', isActive: false, qualifiedTechnicianIds: [], matchingBayIds: [] },
  ],
  technicians: [
    { id: 2, name: 'Lan', shiftStart: '08:00', shiftEnd: '17:00', isActive: true, skills: [{ skill: 'General', tier: 'Qualified' }, { skill: 'EV', tier: 'Senior' }] },
    { id: 1, name: 'Nam', shiftStart: '08:00', shiftEnd: '17:00', isActive: true, skills: [{ skill: 'General', tier: 'Master' }] },
    { id: 5, name: 'Tuan', shiftStart: '08:00', shiftEnd: '17:00', isActive: false, skills: [] },
  ],
  serviceBays: [
    { id: 1, name: 'Bay 1', bayType: 'Standard', isActive: true },
    { id: 3, name: 'Bay 3', bayType: 'EV', isActive: true },
    { id: 5, name: 'Bay 4', bayType: 'Lift', isActive: false },
  ],
};

const thuDuc: DealershipSetupDto = {
  dealership: { ...dealerships[1]!, isActive: true },
  serviceTypes: [],
  technicians: [{ id: 4, name: 'Hoa', shiftStart: '07:30', shiftEnd: '18:00', isActive: true, skills: [{ skill: 'General', tier: 'Senior' }] }],
  serviceBays: [{ id: 4, name: 'Bay A', bayType: 'Standard', isActive: true }],
};

beforeEach(() => {
  mocked.getDealerships.mockResolvedValue(dealerships);
  mocked.getDealershipSetup.mockImplementation(async (id) => (id === 2 ? thuDuc : district1));
});

const table = (name: string) => screen.getByRole('table', { name });
const rowOf = (tableName: string, rowName: string) =>
  within(table(tableName)).getByRole('rowheader', { name: new RegExp(`^${rowName}`) }).closest('tr') as HTMLElement;
const cells = (row: HTMLElement) => within(row).getAllByRole('cell').map((c) => c.textContent);

async function renderPage() {
  const user = userEvent.setup();
  renderWithProviders(<SetupPage />, { route: '/setup' });
  await screen.findByRole('table', { name: 'Services' });
  return user;
}

describe('SetupPage', () => {
  it('shows the first dealership: opening hours, services, technicians and bays from the data', async () => {
    await renderPage();
    expect(mocked.getDealershipSetup).toHaveBeenCalledWith(1, expect.anything());
    expect(screen.getByLabelText('Dealership')).toHaveValue('1');
    expect(screen.getByText('Open 08:00–17:00 every day (Asia/Ho_Chi_Minh time).')).toBeInTheDocument();

    expect(cells(rowOf('Services', 'Oil change'))).toEqual(['60 min', 'General, Trainee or above', 'Standard', 'Nam, Lan']);
    expect(cells(rowOf('Technicians', 'Lan'))).toEqual(['08:00–17:00', 'General · QualifiedEV · Senior', 'Active']);
    expect(within(screen.getByRole('list', { name: 'Skills of Lan' })).getAllByRole('listitem').map((li) => li.textContent)).toEqual([
      'General · Qualified',
      'EV · Senior',
    ]);
    expect(cells(rowOf('Service bays', 'Bay 3'))).toEqual(['EV', 'Active']);
    expect(screen.getByRole('heading', { name: 'How the system picks' })).toBeInTheDocument();
  });

  it('marks the only qualified technician with an "only one" badge, and nobody with "Nobody active"', async () => {
    await renderPage();
    const ev = rowOf('Services', 'EV battery check');
    expect(within(ev).getByText('only one')).toBeInTheDocument();
    expect(cells(ev)[3]).toBe('Lanonly one');
    expect(within(rowOf('Services', 'Oil change')).queryByText('only one')).not.toBeInTheDocument();
    expect(within(rowOf('Services', 'Paint repair')).getByText('Nobody active')).toBeInTheDocument();
  });

  it('greys inactive services, technicians and bays and says "Inactive"', async () => {
    await renderPage();
    const paint = rowOf('Services', 'Paint repair');
    expect(paint).toHaveClass('table__row--inactive');
    expect(within(paint).getByRole('rowheader')).toHaveTextContent('Paint repair · Inactive');
    expect(rowOf('Technicians', 'Tuan')).toHaveClass('table__row--inactive');
    expect(cells(rowOf('Technicians', 'Tuan'))).toEqual(['08:00–17:00', 'None', 'Inactive']);
    expect(cells(rowOf('Service bays', 'Bay 4'))).toEqual(['Lift', 'Inactive']);
    expect(rowOf('Service bays', 'Bay 1')).not.toHaveClass('table__row--inactive');
  });

  it('switching the dealership shows its setup', async () => {
    const user = await renderPage();
    await user.selectOptions(screen.getByLabelText('Dealership'), '2');

    expect(await screen.findByText('Open 07:30–18:00 every day (Asia/Ho_Chi_Minh time).')).toBeInTheDocument();
    expect(screen.getByText('This dealership has no services.')).toBeInTheDocument();
    expect(rowOf('Technicians', 'Hoa')).toBeInTheDocument();
    expect(within(table('Technicians')).queryByText('Lan')).not.toBeInTheDocument();
    expect(mocked.getDealershipSetup).toHaveBeenLastCalledWith(2, expect.anything());
  });

  it('says when the dealership is not active', async () => {
    mocked.getDealershipSetup.mockResolvedValue({ ...district1, dealership: { ...district1.dealership, isActive: false } });
    await renderPage();
    expect(screen.getByText('This dealership is not active, so it takes no bookings.')).toBeInTheDocument();
  });

  it('shows an error with Try again when the setup cannot be loaded', async () => {
    mocked.getDealershipSetup.mockRejectedValueOnce(ApiError.network(new TypeError('Failed to fetch')));
    const user = userEvent.setup();
    renderWithProviders(<SetupPage />, { route: '/setup' });

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('We could not load the setup of this dealership')).toBeInTheDocument();
    await user.click(within(alert).getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('table', { name: 'Services' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows loading, an error for the dealership list, and an empty state', async () => {
    mocked.getDealerships.mockRejectedValueOnce(ApiError.network(new TypeError('Failed to fetch'))).mockResolvedValueOnce([]);
    const user = userEvent.setup();
    renderWithProviders(<SetupPage />, { route: '/setup' });
    expect(screen.getByText('Loading dealerships…')).toBeInTheDocument();

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('We could not load the dealerships')).toBeInTheDocument();
    await user.click(within(alert).getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('There are no dealerships yet.')).toBeInTheDocument();
    expect(mocked.getDealershipSetup).not.toHaveBeenCalled();
  });
});
