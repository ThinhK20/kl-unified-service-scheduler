import { Link } from 'react-router-dom';
import { isApiError } from '../../api/types';
import { Alert } from '../../components/Alert';
import { Button } from '../../components/Button';
import { Spinner } from '../../components/Spinner';
import { DEMO_CUSTOMER_ID } from '../../config';
import { useNow } from '../../lib/useNow';
import { AppointmentCard } from './AppointmentCard';
import { isCancellable } from './appointmentRules';
import { useAppointments } from './useAppointments';
import { useCancelAppointment } from './useCancelAppointment';

function cancelErrorText(error: Error | null): string {
  if (isApiError(error)) {
    if (error.code === 'NETWORK_ERROR') return "We can't reach the server. Please try again.";
    return error.detail ?? error.title;
  }
  return 'Please try again.';
}

export function AppointmentsPage() {
  const appointments = useAppointments(DEMO_CUSTOMER_ID);
  const cancel = useCancelAppointment();
  const now = useNow();

  return (
    <main className="page page--narrow">
      <div className="page__head">
        <div>
          <h1 className="page__title">My appointments</h1>
          <p className="page__lead page__lead--tight">Newest first. Cancelling frees the time for other customers.</p>
        </div>
        <Link to="/" className="btn btn--primary">
          Book a service
        </Link>
      </div>

      {appointments.isPending && <Spinner label="Loading your appointments…" />}
      {appointments.isError && (
        <Alert title="We could not load your appointments">
          <Button variant="secondary" onClick={() => void appointments.refetch()}>
            Try again
          </Button>
        </Alert>
      )}
      {appointments.data && appointments.data.length === 0 && (
        <p className="muted">You have no appointments yet.</p>
      )}
      {appointments.data && appointments.data.length > 0 && (
        <ul className="appt-list">
          {appointments.data.map((a) => (
            <li key={a.id}>
              <AppointmentCard
                appointment={a}
                canCancel={isCancellable(a, now)}
                now={now}
                onCancel={cancel.cancel}
                isCancelling={cancel.cancellingId === a.id}
                cancelError={cancel.failedId === a.id ? cancelErrorText(cancel.error) : null}
              />
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
