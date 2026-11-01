import { Alert } from '../../components/Alert';
import { Button } from '../../components/Button';
import type { BookingErrorView } from './bookingErrors';
import type { SlotOption } from './useBookingForm';

export interface BookingErrorBoxProps {
  error: BookingErrorView;
  suggestions: SlotOption[];
  onPickSuggestion: (key: string) => void;
  onRetry: () => void;
  isSaving: boolean;
}

export function BookingErrorBox({ error, suggestions, onPickSuggestion, onRetry, isSaving }: BookingErrorBoxProps) {
  const tone = error.kind === 'busy' || error.kind === 'network' ? 'neutral' : 'error';
  return (
    <Alert title={error.title} tone={tone}>
      <p className="alert__text">{error.message}</p>
      {error.details.length > 0 && (
        <ul className="alert__list">
          {error.details.map((d) => (
            <li key={d}>{d}</li>
          ))}
        </ul>
      )}
      {suggestions.length > 0 && (
        <div className="alert__actions" role="group" aria-label="Suggested start times">
          {suggestions.map((s) => (
            <Button key={s.key} variant="accent-outline" onClick={() => onPickSuggestion(s.key)}>
              {s.label}
            </Button>
          ))}
        </div>
      )}
      {error.canRetry && (
        <div className="alert__actions">
          <Button variant="secondary" onClick={onRetry} disabled={isSaving}>
            {isSaving ? 'Trying again…' : 'Try again'}
          </Button>
        </div>
      )}
    </Alert>
  );
}
