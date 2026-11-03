import { createContext, useContext } from 'react';
import type { Customer } from '../../api/types';

export type CustomerStatus = 'loading' | 'error' | 'ready';

export interface CustomerContextValue {
  /** The customer the app books for. Null until the customer list has loaded (or when it failed). */
  customerId: number | null;
  customer: Customer | null;
  customers: Customer[];
  status: CustomerStatus;
  selectCustomer: (id: number) => void;
  retry: () => void;
}

export const CustomerContext = createContext<CustomerContextValue | null>(null);

/** The selected customer. Must be used under <CustomerProvider>. */
export function useCustomer(): CustomerContextValue {
  const value = useContext(CustomerContext);
  if (!value) throw new Error('useCustomer must be used inside <CustomerProvider>.');
  return value;
}
