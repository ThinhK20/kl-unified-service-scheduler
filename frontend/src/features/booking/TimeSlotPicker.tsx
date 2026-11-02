import { useId } from 'react';
import { Button } from '../../components/Button';
import { Spinner } from '../../components/Spinner';
import { Alert } from '../../components/Alert';
import type { SlotsState, TimeOption } from './useBookingForm';

export interface TimeSlotPickerProps {
  state: SlotsState;
  /** Every start time of the day that has not passed; only "Available" ones can be picked. */
  slots: TimeOption[];
  selectedKey: string;
  onSelect: (key: string) => void;
  onRetry: () => void;
  /** Why the app cleared the user's pick, e.g. after the car changed. */
  note?: string | undefined;
  error?: string | undefined;
}

const REASON: Record<Exclude<TimeOption['status'], 'Available'>, { visible: string; spoken: string }> = {
  Booked: { visible: 'Booked', spoken: 'booked' },
  VehicleBooked: { visible: 'Your car is booked', spoken: 'your car is booked' },
};

function TimeButton({ slot, selected, onSelect }: { slot: TimeOption; selected: boolean; onSelect: (key: string) => void }) {
  if (slot.status === 'Available') {
    return (
      <button
        type="button"
        className={`slot${selected ? ' slot--selected' : ''}`}
        aria-pressed={selected}
        onClick={() => onSelect(slot.key)}
      >
        {slot.label}
      </button>
    );
  }
  const reason = REASON[slot.status];
  const modifier = slot.status === 'Booked' ? 'slot--booked' : 'slot--car-booked';
  // Focusable (like unavailable calendar days) so keyboard users hear the reason; clicking does nothing.
  return (
    <button
      type="button"
      className={`slot slot--unavailable ${modifier}`}
      aria-disabled="true"
      aria-label={`${slot.label}, ${reason.spoken}`}
    >
      <span className="slot__time">{slot.label}</span>
      <span className="slot__reason">{reason.visible}</span>
    </button>
  );
}

export function TimeSlotPicker({ state, slots, selectedKey, onSelect, onRetry, note, error }: TimeSlotPickerProps) {
  const headingId = useId();
  const errorId = useId();

  return (
    <div className="slots">
      <p id={headingId} className="field__label">
        Start times
      </p>

      <div role="status" className="slots__note-region">
        {note && <p className="slots__note">{note}</p>}
      </div>

      {state === 'idle' && <p className="muted">Choose a dealership, a service and a date to see free times.</p>}
      {state === 'loading' && <Spinner label="Loading free times…" />}
      {state === 'error' && (
        <Alert title="We could not load free times">
          <Button variant="secondary" onClick={onRetry}>
            Try again
          </Button>
        </Alert>
      )}
      {state === 'empty' && <p className="muted">No free times on this day. Please pick another day.</p>}
      {(state === 'ready' || (state === 'empty' && slots.length > 0)) && (
        <>
          <div
            role="group"
            aria-labelledby={headingId}
            aria-describedby={error ? errorId : undefined}
            className="slots__grid"
          >
            {slots.map((slot) => (
              <TimeButton key={slot.key} slot={slot} selected={slot.key === selectedKey} onSelect={onSelect} />
            ))}
          </div>
          <p className="field__hint">Times are in the dealership's local time. Crossed-out times are taken.</p>
        </>
      )}
      {error && (
        <p id={errorId} className="field__error">
          {error}
        </p>
      )}
    </div>
  );
}
