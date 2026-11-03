import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { CustomerContext, type CustomerContextValue, type CustomerStatus } from './customerContext';
import { defaultCustomerStorage, readStoredCustomerId, writeStoredCustomerId } from './customerStorage';
import { useCustomers } from './useCustomers';

export interface CustomerProviderProps {
  children: ReactNode;
  /** Where the selection is remembered. Defaults to localStorage; null = remember nothing. */
  storage?: Storage | null;
}

/**
 * Keeps the "Booking as" customer. The stored id is used when it is in the customer list,
 * otherwise the first customer.
 */
export function CustomerProvider({ children, storage }: CustomerProviderProps) {
  const [store] = useState(() => (storage === undefined ? defaultCustomerStorage() : storage));
  const [selectedId, setSelectedId] = useState(() => readStoredCustomerId(store));
  const customersQuery = useCustomers();
  const { refetch } = customersQuery;

  const customers = useMemo(() => customersQuery.data ?? [], [customersQuery.data]);
  const customer = customers.find((c) => c.id === selectedId) ?? customers[0] ?? null;
  let status: CustomerStatus = 'loading';
  // A failed background refetch keeps the cached list: only "no list at all" (or an empty one) is an error.
  if (customersQuery.data) status = customer ? 'ready' : 'error';
  else if (customersQuery.isError) status = 'error';

  const selectCustomer = useCallback(
    (id: number) => {
      setSelectedId(id);
      writeStoredCustomerId(store, id);
    },
    [store],
  );
  const retry = useCallback(() => void refetch(), [refetch]);

  const value = useMemo<CustomerContextValue>(
    () => ({ customerId: customer?.id ?? null, customer, customers, status, selectCustomer, retry }),
    [customer, customers, status, selectCustomer, retry],
  );
  return <CustomerContext.Provider value={value}>{children}</CustomerContext.Provider>;
}
