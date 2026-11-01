import type { AppointmentStatus } from '../api/types';

export function StatusBadge({ status }: { status: AppointmentStatus }) {
  return <span className={`badge badge--${status.toLowerCase()}`}>{status}</span>;
}
