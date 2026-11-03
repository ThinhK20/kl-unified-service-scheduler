import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../../api/appointmentsApi';
import { ApiError } from '../../api/types';
import { AppHeader } from '../../components/AppHeader';
import { appointmentDto, customers, dealerships, serviceTypesByDealer, vehicles, vehiclesByCustomer } from '../../test/fixtures';
import { renderWithProviders, type RenderOptions } from '../../test/render';
import { AppointmentsPage } from '../appointments/AppointmentsPage';
import { BookingPage } from '../booking/BookingPage';
import { CUSTOMER_STORAGE_KEY } from './customerStorage';

vi.mock('../../api/appointmentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/appointmentsApi')>();
  return {
    ...actual,
    getCustomers: vi.fn(),
    getDealerships: vi.fn(),
    getServiceTypes: vi.fn(),
    getCustomerVehicles: vi.fn(),
    getAppointments: vi.fn(),
    bookAppointment: vi.fn(),
  };
});

const mocked = vi.mocked(api);

function memoryStorage(initial: Record<string, string> = {}): Storage {
  const data = new Map(Object.entries(initial));
  return {
    get length() {
      return data.size;
    },
    clear: () => data.clear(),
    getItem: (k) => data.get(k) ?? null,
    key: (i) => [...data.keys()][i] ?? null,
    removeItem: (k) => void data.delete(k),
    setItem: (k, v) => void data.set(k, v),
  };
}

const blockedStorage = {
  getItem: () => {
    throw new Error('blocked');
  },
  setItem: () => {
    throw new Error('blocked');
  },
  removeItem: () => {
    throw new Error('blocked');
  },
} as unknown as Storage;

beforeEach(() => {
  mocked.getCustomers.mockResolvedValue(customers);
  mocked.getDealerships.mockResolvedValue(dealerships);
  mocked.getServiceTypes.mockImplementation(async (id) => serviceTypesByDealer[id] ?? []);
  mocked.getCustomerVehicles.mockImplementation(async (customerId) =>
    (customerId === 1 ? vehicles : (vehiclesByCustomer[customerId] ?? [])).map(api.toVehicle),
  );
  mocked.getAppointments.mockImplementation(async (customerId) =>
    customerId === 2
      ? [api.toAppointment(appointmentDto({ id: 9, customerId: 2, serviceType: { id: 2, name: 'Brake inspection', durationMinutes: 90 } }))]
      : [api.toAppointment(appointmentDto())],
  );
});

const switcher = () => screen.getByRole('combobox', { name: 'Booking as' });
const vehicleOptions = () =>
  within(screen.getByLabelText('Vehicle'))
    .getAllByRole('option')
    .map((o) => o.textContent);

function renderBooking(options: RenderOptions = {}) {
  const user = userEvent.setup();
  const rendered = renderWithProviders(
    <>
      <AppHeader />
      <BookingPage />
    </>,
    { customers: null, ...options },
  );
  return { user, ...rendered };
}

describe('Booking as (customer switcher)', () => {
  it('lists the customers and starts with the first one', async () => {
    renderBooking();
    await screen.findByLabelText('Vehicle');
    expect(within(switcher()).getAllByRole('option').map((o) => o.textContent)).toEqual(['An Nguyen', 'Binh Tran', 'Chi Le']);
    expect(switcher()).toHaveValue('1');
    expect(vehicleOptions()).toContain('Toyota Vios 2021 · VIN JTDBT923X71000001');
  });

  it("switching shows the other customer's cars and starts a fresh booking form", async () => {
    const { user } = renderBooking();
    await screen.findByLabelText('Vehicle');
    await user.selectOptions(screen.getByLabelText('Vehicle'), '1');
    await user.selectOptions(screen.getByLabelText('Service centre'), '1');

    await user.selectOptions(switcher(), '3');

    await waitFor(() => expect(vehicleOptions()).toEqual(['Choose a vehicle', 'Mazda CX-5 2022 · VIN JM3KFBCM1N0000004', 'Kia Morning 2020 · VIN KNABE511AL0000005']));
    expect(screen.getByLabelText('Vehicle')).toHaveValue('');
    expect(screen.getByLabelText('Service centre')).toHaveValue('');
    expect(mocked.getCustomerVehicles).toHaveBeenLastCalledWith(3, expect.anything());
  });

  it("switching on My appointments shows that customer's appointments", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <>
        <AppHeader />
        <AppointmentsPage />
      </>,
      { customers: null, route: '/appointments' },
    );
    await screen.findByRole('article', { name: 'Oil change' });

    await user.selectOptions(switcher(), '2');

    expect(await screen.findByRole('article', { name: 'Brake inspection' })).toBeInTheDocument();
    expect(screen.queryByRole('article', { name: 'Oil change' })).not.toBeInTheDocument();
    expect(mocked.getAppointments).toHaveBeenLastCalledWith(2, expect.anything());
  });

  it('remembers the selection in storage and falls back to the first customer for an unknown id', async () => {
    const storage = memoryStorage({ [CUSTOMER_STORAGE_KEY]: '3' });
    const first = renderBooking({ storage });
    await screen.findByRole('option', { name: /Mazda CX-5/ });
    expect(switcher()).toHaveValue('3');
    await first.user.selectOptions(switcher(), '2');
    expect(storage.getItem(CUSTOMER_STORAGE_KEY)).toBe('2');
    first.unmount();

    renderBooking({ storage: memoryStorage({ [CUSTOMER_STORAGE_KEY]: '99' }) });
    await screen.findByLabelText('Vehicle');
    expect(switcher()).toHaveValue('1');
  });

  it('still works when storage is blocked', async () => {
    const { user } = renderBooking({ storage: blockedStorage });
    await screen.findByLabelText('Vehicle');
    expect(switcher()).toHaveValue('1');

    await user.selectOptions(switcher(), '2');

    expect(switcher()).toHaveValue('2');
    expect(await screen.findByRole('option', { name: /VinFast VF 8/ })).toBeInTheDocument();
  });

  it('shows an error with Try again when the customers cannot be loaded', async () => {
    mocked.getCustomers.mockRejectedValueOnce(ApiError.network(new TypeError('Failed to fetch')));
    const { user } = renderBooking();

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('We could not load the booking options')).toBeInTheDocument();
    expect(switcher()).toBeDisabled();

    await user.click(within(alert).getByRole('button', { name: 'Try again' }));

    expect(await screen.findByLabelText('Vehicle')).toBeInTheDocument();
    expect(switcher()).toHaveValue('1');
  });

  it('keeps working when a background refetch of the customers fails', async () => {
    const { queryClient } = renderBooking();
    await screen.findByRole('option', { name: /Toyota Vios/ });
    mocked.getCustomers.mockRejectedValue(ApiError.network(new TypeError('Failed to fetch')));

    await queryClient.refetchQueries({ queryKey: ['customers'] });

    expect(switcher()).toBeEnabled();
    expect(switcher()).toHaveValue('1');
    expect(screen.getByLabelText('Vehicle')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
