import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import type { Appointment } from '../../api/types';
import { Alert } from '../../components/Alert';
import { Button } from '../../components/Button';
import { StatusBadge } from '../../components/StatusBadge';
import { formatTimeRange, getCardDate } from '../../lib/dateTime';
import { formatReference } from '../../lib/formatters';

export interface AppointmentCardProps {
  appointment: Appointment;
  /** Confirmed and not started yet (see appointmentRules.isCancellable). */
  canCancel: boolean;
  onCancel: (id: number) => void;
  isCancelling: boolean;
  cancelError: string | null;
  now: Date;
}

/** idle -> confirm (inline question) -> sent (request running). Back to idle when the request ends. */
type CancelStep = 'idle' | 'confirm' | 'sent';

export function AppointmentCard({ appointment: a, canCancel, onCancel, isCancelling, cancelError, now }: AppointmentCardProps) {
  const { month, day, year } = getCardDate(a.start, a.timeZoneId, now);
  const dateText = year ? `${day} ${month} ${year}` : `${day} ${month}`;
  const titleId = `appointment-${a.id}-title`;
  const promptId = useId();

  const [step, setStep] = useState<CancelStep>('idle');
  const keepRef = useRef<HTMLButtonElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const returnFocus = useRef(false);

  // A finished request ends the "sent" step without an extra state update.
  const visibleStep: CancelStep = step === 'sent' && !isCancelling ? 'idle' : step;
  const showConfirm = canCancel && (visibleStep === 'confirm' || visibleStep === 'sent');

  useEffect(() => {
    if (visibleStep === 'confirm') keepRef.current?.focus();
    if (visibleStep === 'idle' && returnFocus.current) {
      returnFocus.current = false;
      cancelRef.current?.focus();
    }
  }, [visibleStep]);

  const keep = () => {
    returnFocus.current = true;
    setStep('idle');
  };

  const onConfirmKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape' && visibleStep === 'confirm') {
      event.stopPropagation();
      keep();
    }
  };

  return (
    <article className="card appt" aria-labelledby={titleId}>
      <div className="appt__date" aria-hidden="true">
        <div className="appt__month">{month}</div>
        <div className="appt__day">{day}</div>
        {year && <div className="appt__year">{year}</div>}
      </div>
      <div className="appt__body">
        <div className="appt__head">
          <h2 id={titleId} className="appt__title">
            {a.serviceType.name}
          </h2>
          <StatusBadge status={a.status} />
        </div>
        <p className="appt__line">
          {dateText} · {formatTimeRange(a.start, a.end, a.timeZoneId)} · {a.vehicle.displayName} · {a.dealership.name}
        </p>
        <p className="appt__meta">
          Technician {a.technician.name} · {a.serviceBay.name} · Ref {formatReference(a.id)}
        </p>
        {showConfirm && (
          <div className="appt__confirm" role="group" aria-labelledby={promptId} onKeyDown={onConfirmKeyDown}>
            <p id={promptId} className="appt__confirm-text">
              Cancel this appointment?
            </p>
            <div className="alert__actions">
              <Button
                variant="primary"
                disabled={visibleStep === 'sent'}
                onClick={() => {
                  returnFocus.current = true;
                  setStep('sent');
                  onCancel(a.id);
                }}
              >
                {visibleStep === 'sent' ? 'Cancelling…' : 'Yes, cancel'}
              </Button>
              <Button ref={keepRef} variant="secondary" disabled={visibleStep === 'sent'} onClick={keep}>
                Keep it
              </Button>
            </div>
          </div>
        )}
        {cancelError && (
          <Alert title="We could not cancel this appointment">
            <p className="alert__text">{cancelError}</p>
          </Alert>
        )}
      </div>
      {canCancel && !showConfirm && (
        <Button
          ref={cancelRef}
          variant="secondary"
          onClick={() => setStep('confirm')}
          aria-label={`Cancel ${a.serviceType.name} on ${dateText}`}
        >
          Cancel
        </Button>
      )}
    </article>
  );
}
