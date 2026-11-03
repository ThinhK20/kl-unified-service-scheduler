import { useIsMutating } from '@tanstack/react-query';
import { useId } from 'react';
import { mutationKeys } from '../../lib/queryKeys';
import { useCustomer } from './customerContext';

/**
 * "Booking as" select in the header. Switching starts a fresh booking for that customer, so it is locked
 * while a booking is being saved (the confirmation would be lost).
 */
export function CustomerSwitcher() {
  const { customerId, customers, status, selectCustomer } = useCustomer();
  const saving = useIsMutating({ mutationKey: mutationKeys.bookAppointment }) > 0;
  const id = useId();
  const hintId = useId();
  return (
    <div className="customer-switch">
      <label htmlFor={id} className="customer-switch__label">
        Booking as
      </label>
      <select
        id={id}
        className="customer-switch__control"
        value={customerId ?? ''}
        disabled={status !== 'ready' || saving}
        aria-describedby={saving ? hintId : undefined}
        onChange={(e) => selectCustomer(Number(e.target.value))}
      >
        {status === 'loading' && <option value="">Loading…</option>}
        {status === 'error' && <option value="">Unavailable</option>}
        {customers.map((c) => (
          <option key={c.id} value={c.id}>
            {c.name}
          </option>
        ))}
      </select>
      {saving && (
        <span id={hintId} className="customer-switch__hint">
          Saving your booking…
        </span>
      )}
    </div>
  );
}
