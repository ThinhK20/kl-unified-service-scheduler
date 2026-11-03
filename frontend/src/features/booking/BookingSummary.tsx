export interface BookingSummaryProps {
  vehicle: string | null;
  dealership: string | null;
  service: string | null;
  date: string | null;
  time: string | null;
  /** Expected technician / bay (from the assignment preview); the rows show only when known. */
  technician?: string | null;
  bay?: string | null;
}

const EMPTY = 'Not chosen yet';

export function BookingSummary(props: BookingSummaryProps) {
  const rows: [string, string | null][] = [
    ['Vehicle', props.vehicle],
    ['Dealership', props.dealership],
    ['Service', props.service],
    ['Date', props.date],
    ['Time', props.time],
  ];
  if (props.technician) rows.push(['Technician', props.technician]);
  if (props.bay) rows.push(['Bay', props.bay]);
  return (
    <dl className="summary" aria-label="Booking summary">
      {rows.map(([label, value]) => (
        <div key={label} className="summary__row">
          <dt>{label}</dt>
          <dd className={value ? undefined : 'summary__empty'}>{value ?? EMPTY}</dd>
        </div>
      ))}
    </dl>
  );
}
