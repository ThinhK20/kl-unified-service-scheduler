// Date and time-zone helpers. Every function takes the IANA zone explicitly so the UI
// always shows dealership time, never the browser's time zone.

interface ZonedParts {
  year: number;
  month: number;
  day: number;
  hour: number;
  minute: number;
  second: number;
}

const partsFormatters = new Map<string, Intl.DateTimeFormat>();

function getPartsFormatter(timeZone: string): Intl.DateTimeFormat {
  let formatter = partsFormatters.get(timeZone);
  if (!formatter) {
    formatter = new Intl.DateTimeFormat('en-US', {
      timeZone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
    partsFormatters.set(timeZone, formatter);
  }
  return formatter;
}

/** Wall-clock parts of an instant in the given zone. */
export function getZonedParts(date: Date, timeZone: string): ZonedParts {
  const parts: Partial<Record<Intl.DateTimeFormatPartTypes, number>> = {};
  for (const part of getPartsFormatter(timeZone).formatToParts(date)) {
    if (part.type !== 'literal') parts[part.type] = Number(part.value);
  }
  return {
    year: parts.year ?? 0,
    month: parts.month ?? 0,
    day: parts.day ?? 0,
    // Some engines print midnight as 24 even with h23; normalise it.
    hour: (parts.hour ?? 0) % 24,
    minute: parts.minute ?? 0,
    second: parts.second ?? 0,
  };
}

/** Offset of the zone from UTC at that instant, in minutes (e.g. +420 for UTC+07:00). */
export function getTimeZoneOffsetMinutes(date: Date, timeZone: string): number {
  const p = getZonedParts(date, timeZone);
  const wallClockAsUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second);
  const instantWithoutMs = Math.floor(date.getTime() / 1000) * 1000;
  return Math.round((wallClockAsUtc - instantWithoutMs) / 60_000);
}

const pad = (n: number, width = 2): string => String(Math.abs(n)).padStart(width, '0');

/**
 * ISO-8601 string of the instant in the zone's local time, with the zone's offset.
 * Example: 2026-11-10T03:30:00Z in Asia/Ho_Chi_Minh -> "2026-11-10T10:30:00+07:00".
 * This is the format the booking request needs for `startTime`.
 */
export function toZonedIsoString(date: Date, timeZone: string): string {
  const p = getZonedParts(date, timeZone);
  const offset = getTimeZoneOffsetMinutes(date, timeZone);
  const sign = offset < 0 ? '-' : '+';
  const offsetText = `${sign}${pad(Math.trunc(Math.abs(offset) / 60))}:${pad(Math.abs(offset) % 60)}`;
  return `${pad(p.year, 4)}-${pad(p.month)}-${pad(p.day)}T${pad(p.hour)}:${pad(p.minute)}:${pad(p.second)}${offsetText}`;
}

/** Local calendar date ("YYYY-MM-DD") of the instant in the zone. */
export function toIsoDateInZone(date: Date, timeZone: string): string {
  const p = getZonedParts(date, timeZone);
  return `${pad(p.year, 4)}-${pad(p.month)}-${pad(p.day)}`;
}

/** Today's calendar date ("YYYY-MM-DD") in the zone. */
export function todayInZone(timeZone: string, now: Date = new Date()): string {
  return toIsoDateInZone(now, timeZone);
}

/** Today's calendar date in the browser zone. Only used before a dealership is chosen. */
export function todayInBrowserZone(now: Date = new Date()): string {
  return `${pad(now.getFullYear(), 4)}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** "10:30" (24-hour clock) in the zone. */
export function formatTime(date: Date, timeZone: string): string {
  const p = getZonedParts(date, timeZone);
  return `${pad(p.hour)}:${pad(p.minute)}`;
}

/** "10:30 – 11:30" in the zone. */
export function formatTimeRange(start: Date, end: Date, timeZone: string): string {
  return `${formatTime(start, timeZone)} – ${formatTime(end, timeZone)}`;
}

const longDateFormatters = new Map<string, Intl.DateTimeFormat>();

/** "Tue 10 Nov 2026" in the zone. */
export function formatLongDate(date: Date, timeZone: string): string {
  let formatter = longDateFormatters.get(timeZone);
  if (!formatter) {
    formatter = new Intl.DateTimeFormat('en-GB', {
      timeZone,
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
    longDateFormatters.set(timeZone, formatter);
  }
  const parts = Object.fromEntries(
    formatter
      .formatToParts(date)
      .filter((p) => p.type !== 'literal')
      .map((p) => [p.type, p.value]),
  );
  return `${parts.weekday} ${parts.day} ${parts.month} ${parts.year}`;
}

/** "Tue 10 Nov 2026" for a calendar date string ("YYYY-MM-DD"). No zone maths involved. */
export function formatIsoDate(isoDate: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(isoDate);
  if (!match) return isoDate;
  const [, y, m, d] = match;
  return formatLongDate(new Date(Date.UTC(Number(y), Number(m) - 1, Number(d), 12)), 'UTC');
}

/** Short month ("Nov") and day ("10") of the instant in the zone, for date badges. */
export function getMonthAndDay(date: Date, timeZone: string): { month: string; day: string } {
  const month = new Intl.DateTimeFormat('en-GB', { timeZone, month: 'short' }).format(date);
  return { month, day: String(getZonedParts(date, timeZone).day) };
}

/**
 * Date badge parts for a card. `year` is set only when the instant is not in the current
 * year (both measured in the zone), e.g. { month: 'Nov', day: '10', year: '2025' }.
 */
export function getCardDate(
  date: Date,
  timeZone: string,
  now: Date = new Date(),
): { month: string; day: string; year: string | null } {
  const year = getZonedParts(date, timeZone).year;
  const currentYear = getZonedParts(now, timeZone).year;
  return { ...getMonthAndDay(date, timeZone), year: year === currentYear ? null : String(year) };
}

/** Adds minutes to an instant. */
export function addMinutes(date: Date, minutes: number): Date {
  return new Date(date.getTime() + minutes * 60_000);
}

/** True when the string is a real calendar date in "YYYY-MM-DD" form. */
export function isIsoDate(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return false;
  const [, y, m, d] = match.map(Number) as [number, number, number, number];
  const date = new Date(Date.UTC(y, m - 1, d));
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d;
}

// ---------------------------------------------------------------------------------------------
// Calendar dates ("YYYY-MM-DD"). Pure calendar maths in UTC, so no zone or daylight saving time is involved.
// ---------------------------------------------------------------------------------------------

function parseIsoDate(isoDate: string): { y: number; m: number; d: number } {
  const [y, m, d] = isoDate.split('-').map(Number) as [number, number, number];
  return { y, m, d };
}

const isoFromUtc = (ms: number): string => new Date(ms).toISOString().slice(0, 10);

/** "2026-11-30" + 1 -> "2026-12-01". */
export function addDaysToIsoDate(isoDate: string, days: number): string {
  const { y, m, d } = parseIsoDate(isoDate);
  return isoFromUtc(Date.UTC(y, m - 1, d + days));
}

/** First day of the month: "2026-11-10" -> "2026-11-01". */
export function startOfMonthIso(isoDate: string): string {
  return `${isoDate.slice(0, 7)}-01`;
}

/** Last day of the month: "2026-02-10" -> "2026-02-28". */
export function endOfMonthIso(isoDate: string): string {
  const { y, m } = parseIsoDate(isoDate);
  return isoFromUtc(Date.UTC(y, m, 0));
}

/** First day of the month `months` away: "2026-11-10" + 2 -> "2027-01-01". */
export function addMonthsIso(isoDate: string, months: number): string {
  const { y, m } = parseIsoDate(isoDate);
  return isoFromUtc(Date.UTC(y, m - 1 + months, 1));
}

/**
 * A Date at local midnight of the browser for a calendar date. Only for date-picker widgets that work with
 * browser-local Date objects; never use it as an instant.
 */
export function isoDateToCalendarDate(isoDate: string): Date {
  const { y, m, d } = parseIsoDate(isoDate);
  return new Date(y, m - 1, d);
}

/** The calendar date ("YYYY-MM-DD") a date-picker Date stands for (its browser-local parts). */
export function calendarDateToIsoDate(date: Date): string {
  return todayInBrowserZone(date);
}
