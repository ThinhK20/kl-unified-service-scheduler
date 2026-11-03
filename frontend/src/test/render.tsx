import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { MemoryRouter } from 'react-router-dom';
import type { CustomerDto } from '../api/types';
import { CustomerProvider } from '../features/customers/CustomerProvider';
import { queryKeys } from '../lib/queryKeys';
import { customers as customerFixtures } from './fixtures';

export interface RenderOptions {
  route?: string;
  /** Customers already in the cache (no request). null = let the page fetch them (mock getCustomers). */
  customers?: CustomerDto[] | null;
  /** Where the "Booking as" selection is kept. Default: nowhere. */
  storage?: Storage | null;
}

export function renderWithProviders(ui: ReactElement, { route = '/', customers = customerFixtures, storage = null }: RenderOptions = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } },
  });
  if (customers) queryClient.setQueryData(queryKeys.customers, customers);
  const result = render(
    <QueryClientProvider client={queryClient}>
      <CustomerProvider storage={storage}>
        <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
      </CustomerProvider>
    </QueryClientProvider>,
  );
  return { ...result, queryClient };
}
