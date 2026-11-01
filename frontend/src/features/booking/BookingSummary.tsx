export interface BookingSummaryProps {
  vehicle: string | null;
  dealership: string | null;
  service: string | null;
  date: string | null;
  time: string | null;
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
