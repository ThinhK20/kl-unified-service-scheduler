import { useId, useMemo } from 'react';
import { DayButton, DayPicker, type DayButtonProps, type Modifiers } from 'react-day-picker';
import {
  calendarDateToIsoDate,
  formatIsoDate,
  isoDateToCalendarDate,
  startOfMonthIso,
} from '../lib/dateTime';
import { Alert } from './Alert';
import { Button } from './Button';
import { Spinner } from './Spinner';

export type CalendarDayStatus = 'Available' | 'FullyBooked' | 'Past';
export type CalendarState = 'idle' | 'loading' | 'error' | 'ready';

export interface CalendarProps {
  label: string;
  /** Selected calendar date ("YYYY-MM-DD") or ''. */
  value: string;
  onChange: (isoDate: string) => void;
  /** Any date in the month on screen ("YYYY-MM-DD"). */
  month: string;
  /** Called with the first day of the new month ("YYYY-MM-01"). */
  onMonthChange: (isoDate: string) => void;
  /** Today in the place the dates belong to (the dealership). Days before it are past; no paging before its month. */
  today: string;
  /** Status per date for the month on screen; dates not in the map are unknown and can be picked. */
  dayStatuses?: ReadonlyMap<string, CalendarDayStatus> | undefined;
  /** Loading state of `dayStatuses`. */
  state: CalendarState;
  onRetry: () => void;
  disabled?: boolean;
  hint?: string | undefined;
  error?: string | undefined;
}

// Our own class names (BEM, styles.css) instead of the library stylesheet.
const classNames = {
  root: 'cal',
  months: 'cal__months',
  month: 'cal__month',
  month_caption: 'cal__caption',
  caption_label: 'cal__caption-label',
  nav: 'cal__nav',
  button_previous: 'cal__nav-button cal__nav-button--previous',
  button_next: 'cal__nav-button cal__nav-button--next',
  chevron: 'cal__chevron',
  month_grid: 'cal__grid',
  weekdays: 'cal__weekdays',
  weekday: 'cal__weekday',
  weeks: 'cal__weeks',
  week: 'cal__week',
  day: 'cal__day',
  day_button: 'cal__day-button',
  selected: 'cal__day--selected',
  today: 'cal__day--today',
  disabled: 'cal__day--disabled',
  outside: 'cal__day--outside',
  hidden: 'cal__day--hidden',
  focused: 'cal__day--focused',
};

const modifiersClassNames = { past: 'cal__day--past', fullyBooked: 'cal__day--full' };

/** Past and fully booked days stay focusable (so the reason is announced) but cannot be picked. */
const isUnavailable = (modifiers: Modifiers) => Boolean(modifiers.past || modifiers.fullyBooked);

function CalendarDayButton(props: DayButtonProps) {
  return <DayButton {...props} aria-disabled={isUnavailable(props.modifiers) || props['aria-disabled'] || undefined} />;
}

/** "Tue 10 Nov 2026, fully booked" — the reason is part of the name screen readers announce. */
function dayLabel(date: Date, modifiers: Modifiers): string {
  let label = formatIsoDate(calendarDateToIsoDate(date));
  if (modifiers.today) label = `Today, ${label}`;
  if (modifiers.past) label += ', past date';
  else if (modifiers.fullyBooked) label += ', fully booked';
  if (modifiers.selected) label += ', selected';
  return label;
}

const legend: { key: string; label: string }[] = [
  { key: 'available', label: 'Free times' },
  { key: 'full', label: 'Fully booked' },
  { key: 'past', label: 'Past' },
  { key: 'selected', label: 'Selected' },
];

/**
 * Month calendar for picking one calendar date. Dates are plain "YYYY-MM-DD" strings in the dealership's
 * calendar; the browser-local Date objects the library needs never leave this component.
 */
export function Calendar({
  label,
  value,
  onChange,
  month,
  onMonthChange,
  today,
  dayStatuses,
  state,
  onRetry,
  disabled = false,
  hint,
  error,
}: CalendarProps) {
  const labelId = useId();
  const hintId = useId();
  const errorId = useId();
  const describedBy = [hint ? hintId : '', error ? errorId : ''].filter(Boolean).join(' ') || undefined;

  const modifiers = useMemo(
    () => ({
      past: (date: Date) => calendarDateToIsoDate(date) < today,
      fullyBooked: (date: Date) => {
        const iso = calendarDateToIsoDate(date);
        return iso >= today && dayStatuses?.get(iso) === 'FullyBooked';
      },
    }),
    [today, dayStatuses],
  );

  const showStatus = !disabled;

  return (
    <div className={['field', 'cal-field', disabled ? 'cal-field--disabled' : ''].filter(Boolean).join(' ')}>
      <p id={labelId} className="field__label">
        {label}
      </p>
      <div role="group" aria-labelledby={labelId} aria-describedby={describedBy} className="cal-frame">
        <DayPicker
          mode="single"
          required
          weekStartsOn={1}
          classNames={classNames}
          modifiers={showStatus ? modifiers : {}}
          modifiersClassNames={modifiersClassNames}
          components={{ DayButton: CalendarDayButton }}
          labels={{ labelDayButton: dayLabel }}
          today={isoDateToCalendarDate(today)}
          startMonth={isoDateToCalendarDate(startOfMonthIso(today))}
          month={isoDateToCalendarDate(startOfMonthIso(month))}
          onMonthChange={(date) => onMonthChange(calendarDateToIsoDate(date))}
          selected={value ? isoDateToCalendarDate(value) : undefined}
          onSelect={(_selected, triggerDate, dayModifiers) => {
            const isoDate = calendarDateToIsoDate(triggerDate);
            if (disabled || isUnavailable(dayModifiers) || isoDate === value) return;
            onChange(isoDate);
          }}
          disabled={disabled}
          disableNavigation={disabled}
        />
      </div>

      {showStatus && state === 'loading' && <Spinner label="Checking which days are free…" />}
      {showStatus && state === 'error' && (
        <Alert title="We could not check which days are free" tone="neutral">
          <p className="alert__text">You can still pick a date; its times are checked when you do.</p>
          <Button variant="secondary" onClick={onRetry}>
            Try again
          </Button>
        </Alert>
      )}

      <ul className="cal-legend" aria-label="Calendar legend">
        {legend.map((item) => (
          <li key={item.key} className="cal-legend__item">
            <span className={`cal-legend__swatch cal-legend__swatch--${item.key}`} aria-hidden="true" />
            {item.label}
          </li>
        ))}
      </ul>

      {hint && (
        <p id={hintId} className="field__hint">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="field__error">
          {error}
        </p>
      )}
    </div>
  );
}
