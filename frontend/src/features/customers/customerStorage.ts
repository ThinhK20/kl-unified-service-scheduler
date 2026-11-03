// The selected demo customer, kept in localStorage. Every access is wrapped: private windows and blocked
// storage still work, the selection just is not remembered.

export const CUSTOMER_STORAGE_KEY = 'scheduler-customer-id';

export function defaultCustomerStorage(): Storage | null {
  try {
    return typeof window !== 'undefined' ? window.localStorage : null;
  } catch {
    return null;
  }
}

export function readStoredCustomerId(storage: Storage | null): number | null {
  if (!storage) return null;
  try {
    const raw = storage.getItem(CUSTOMER_STORAGE_KEY);
    const id = raw === null ? NaN : Number(raw);
    return Number.isInteger(id) && id > 0 ? id : null;
  } catch {
    return null;
  }
}

export function writeStoredCustomerId(storage: Storage | null, id: number): void {
  if (!storage) return;
  try {
    storage.setItem(CUSTOMER_STORAGE_KEY, String(id));
  } catch {
    // Storage full or blocked: keep the selection in memory only.
  }
}

/** Forget the selection, so the app starts with the first customer again (the preview's "Reset demo data"). */
export function clearStoredCustomerId(storage: Storage | null = defaultCustomerStorage()): void {
  if (!storage) return;
  try {
    storage.removeItem(CUSTOMER_STORAGE_KEY);
  } catch {
    // Nothing to do.
  }
}
