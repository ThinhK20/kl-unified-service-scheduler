import { useId } from 'react';
import { Button } from '../../components/Button';
import { Spinner } from '../../components/Spinner';
import { Alert } from '../../components/Alert';
import type { SlotOption, SlotsState } from './useBookingForm';

export interface TimeSlotPickerProps {
  state: SlotsState;
  slots: SlotOption[];
  selectedKey: string;
  onSelect: (key: string) => void;
  onRetry: () => void;
  error?: string | undefined;
}

export function TimeSlotPicker({ state, slots, selectedKey, onSelect, onRetry, error }: TimeSlotPickerProps) {
  const headingId = useId();
  const errorId = useId();

  return (
    <div className="slots">
      <p id={headingId} className="field__label">
        Free start times
      </p>

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
      {state === 'ready' && (
        <>
          <div
            role="group"
            aria-labelledby={headingId}
            aria-describedby={error ? errorId : undefined}
            className="slots__grid"
          >
            {slots.map((slot) => {
              const selected = slot.key === selectedKey;
              return (
                <button
                  key={slot.key}
                  type="button"
                  className={`slot${selected ? ' slot--selected' : ''}`}
                  aria-pressed={selected}
                  onClick={() => onSelect(slot.key)}
                >
                  {slot.label}
                </button>
              );
            })}
          </div>
          <p className="field__hint">Times are in the dealership's local time.</p>
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
