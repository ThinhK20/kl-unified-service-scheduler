import { useId } from 'react';
import type { AssignmentPreview, AssignmentStatus, BayState, TechnicianState } from '../../api/types';
import { Alert } from '../../components/Alert';
import { Button } from '../../components/Button';
import { Spinner } from '../../components/Spinner';
import { formatTime } from '../../lib/dateTime';
import type { AssignmentState } from './useBookingForm';

export interface AssignmentPanelProps {
  state: AssignmentState;
  preview: AssignmentPreview | null;
  onRetry: () => void;
}

type RowState = TechnicianState | BayState;

function stateLabel(state: RowState, startLocal: string): string {
  switch (state) {
    case 'WillServe':
      return 'Will serve you';
    case 'Free':
      return 'Free';
    case 'Busy':
      return 'Busy at this time';
    case 'OffShift':
      return `Off shift at ${startLocal}`;
    case 'NotQualified':
      return 'Not qualified';
    case 'WrongType':
      return 'Wrong bay type';
    case 'Inactive':
      return 'Inactive';
  }
}

const STATUS_TEXT: Record<Exclude<AssignmentStatus, 'Assignable'>, string> = {
  Past: 'This time has passed. Please pick another time.',
  VehicleBooked: 'Your car is already booked at this time. Please pick another time.',
  NoAvailability: 'Nobody is free for the whole time any more. Please pick another time.',
};

/** Spoken with the note (visually hidden): "Minh and Bay 1." */
function pickSummary(preview: AssignmentPreview): string {
  return preview.technician && preview.serviceBay ? `${preview.technician.name} and ${preview.serviceBay.name}.` : '';
}

interface Row {
  id: number;
  name: string;
  detail?: string;
  state: RowState;
}

function AssignmentList({ title, rows, startLocal }: { title: string; rows: Row[]; startLocal: string }) {
  const headingId = useId();
  return (
    <div className="assign__group">
      <p id={headingId} className="assign__heading">
        {title}
      </p>
      <ul className="assign__list" aria-labelledby={headingId}>
        {rows.map((row) => (
          <li key={row.id} className={`assign__row assign__row--${row.state === 'WillServe' ? 'pick' : row.state.toLowerCase()}`}>
            <span className="assign__name">
              {row.name}
              {row.detail && <span className="assign__detail"> · {row.detail}</span>}
            </span>
            <span className="sr-only">: </span>
            <span className="assign__state">{stateLabel(row.state, startLocal)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * "Who will serve you": every technician and bay of the dealership with its state for the chosen time,
 * the expected pick highlighted. The system still assigns them when the booking is made.
 */
export function AssignmentPanel({ state, preview, onRetry }: AssignmentPanelProps) {
  const titleId = useId();
  if (state === 'hidden') return null;

  const startLocal = preview ? formatTime(preview.start, preview.timeZoneId) : '';
  return (
    <section className="assign" aria-labelledby={titleId}>
      <h3 id={titleId} className="assign__title">
        Who will serve you
      </h3>
      {state === 'loading' && <Spinner label="Checking who is free…" />}
      {state === 'error' && (
        <Alert title="We could not check who will serve you" tone="neutral">
          <p className="alert__text">You can still book: a technician and a bay are assigned when you do.</p>
          <Button variant="secondary" onClick={onRetry}>
            Try again
          </Button>
        </Alert>
      )}
      {/* Always rendered while the panel is, so screen readers hear each new result once (text changes only). */}
      <div role="status" className="assign__status">
        {state === 'ready' && preview && (
          <>
            <p className={preview.status === 'Assignable' ? 'assign__note' : 'assign__note assign__note--warning'}>
              {preview.status === 'Assignable' ? 'Expected pick — confirmed when you book.' : STATUS_TEXT[preview.status]}
            </p>
            {preview.status === 'Assignable' && <span className="sr-only">{pickSummary(preview)}</span>}
          </>
        )}
      </div>
      {state === 'ready' && preview && (
        <>
          <div className="assign__lists">
            <AssignmentList title="Technician" rows={preview.technicians} startLocal={startLocal} />
            <AssignmentList
              title="Service bay"
              rows={preview.serviceBays.map((b) => ({ ...b, detail: b.bayType }))}
              startLocal={startLocal}
            />
          </div>
        </>
      )}
    </section>
  );
}
