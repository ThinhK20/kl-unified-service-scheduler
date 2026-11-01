import type { Appointment } from '../../api/types';
import { Alert } from '../../components/Alert';
import { Button } from '../../components/Button';
import { StatusBadge } from '../../components/StatusBadge';
import { getMonthAndDay, formatTimeRange } from '../../lib/dateTime';
import { formatReference } from '../../lib/formatters';

export interface AppointmentCardProps {
  appointment: Appointment;
  onCancel: (id: number) => void;
  isCancelling: boolean;
  cancelError: string | null;
}

export function AppointmentCard({ appointment: a, onCancel, isCancelling, cancelError }: AppointmentCardProps) {
  const { month, day } = getMonthAndDay(a.start, a.timeZoneId);
  const titleId = `appointment-${a.id}-title`;

  return (
    <article className="card appt" aria-labelledby={titleId}>
      <div className="appt__date" aria-hidden="true">
        <div className="appt__month">{month}</div>
        <div className="appt__day">{day}</div>
      </div>
      <div className="appt__body">
        <div className="appt__head">
          <h2 id={titleId} className="appt__title">
            {a.serviceType.name}
          </h2>
          <StatusBadge status={a.status} />
        </div>
        <p className="appt__line">
          {day} {month} · {formatTimeRange(a.start, a.end, a.timeZoneId)} · {a.vehicle.displayName} · {a.dealership.name}
        </p>
        <p className="appt__meta">
          Technician {a.technician.name} · {a.serviceBay.name} · Ref {formatReference(a.id)}
        </p>
        {cancelError && (
          <Alert title="We could not cancel this appointment">
            <p className="alert__text">{cancelError}</p>
          </Alert>
        )}
      </div>
      {a.status === 'Confirmed' && (
        <Button
          variant="secondary"
          onClick={() => onCancel(a.id)}
          disabled={isCancelling}
          aria-label={`Cancel ${a.serviceType.name} on ${day} ${month}`}
        >
          {isCancelling ? 'Cancelling…' : 'Cancel'}
        </Button>
      )}
    </article>
  );
}
