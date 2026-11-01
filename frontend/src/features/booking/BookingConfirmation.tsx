import { useEffect, useRef } from 'react';
import { Link } from 'react-router-dom';
import type { Appointment } from '../../api/types';
import { Button } from '../../components/Button';
import { StatusBadge } from '../../components/StatusBadge';
import { formatLongDate, formatTimeRange } from '../../lib/dateTime';
import { formatReference } from '../../lib/formatters';

export interface BookingConfirmationProps {
  appointment: Appointment;
  onBookAnother: () => void;
}

export function BookingConfirmation({ appointment: a, onBookAnother }: BookingConfirmationProps) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  const when = `${formatLongDate(a.start, a.timeZoneId)} · ${formatTimeRange(a.start, a.end, a.timeZoneId)}`;

  return (
    <section className="card confirm" aria-labelledby="confirm-title">
      <div className="confirm__head">
        <div className="confirm__icon" aria-hidden="true">
          <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="#1F7A4D" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
            <path d="M5 12.5l4.5 4.5L19 7.5" />
          </svg>
        </div>
        <div>
          <h2 id="confirm-title" ref={headingRef} tabIndex={-1} className="confirm__title">
            You're booked
          </h2>
          <p className="muted confirm__when">
            {a.serviceType.name} · {when}
          </p>
        </div>
      </div>

      <dl className="confirm__grid">
        <div>
          <dt>Service</dt>
          <dd>{a.serviceType.name}</dd>
        </div>
        <div>
          <dt>Date and time</dt>
          <dd>{when}</dd>
        </div>
        <div>
          <dt>Vehicle</dt>
          <dd>{a.vehicle.displayName}</dd>
        </div>
        <div>
          <dt>Dealership</dt>
          <dd>{a.dealership.name}</dd>
        </div>
        <div>
          <dt>Technician</dt>
          <dd>{a.technician.name}</dd>
        </div>
        <div>
          <dt>Service bay</dt>
          <dd>{a.serviceBay.name}</dd>
        </div>
        <div>
          <dt>Booking reference</dt>
          <dd>{formatReference(a.id)}</dd>
        </div>
        <div>
          <dt>Status</dt>
          <dd>
            <StatusBadge status={a.status} />
          </dd>
        </div>
      </dl>

      <p className="muted">Times are in the dealership's local time. You can cancel from My appointments.</p>

      <div className="confirm__actions">
        <Link to="/appointments" className="btn btn--primary">
          View my appointments
        </Link>
        <Button variant="secondary" onClick={onBookAnother}>
          Book another service
        </Button>
      </div>
    </section>
  );
}
