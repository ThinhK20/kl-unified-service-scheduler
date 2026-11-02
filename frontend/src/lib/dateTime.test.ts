import { describe, expect, it } from 'vitest';
import {
  addDaysToIsoDate,
  addMonthsIso,
  calendarDateToIsoDate,
  endOfMonthIso,
  formatIsoDate,
  formatLongDate,
  formatTime,
  formatTimeRange,
  getCardDate,
  getMonthAndDay,
  getTimeZoneOffsetMinutes,
  isIsoDate,
  isoDateToCalendarDate,
  startOfMonthIso,
  todayInZone,
  toIsoDateInZone,
  toZonedIsoString,
} from './dateTime';

const HCM = 'Asia/Ho_Chi_Minh';

describe('test environment', () => {
  it('runs in a browser zone that is NOT the dealership zone', () => {
    // vite.config.ts sets TZ=America/New_York; 03:30Z is 22:30 the day before there.
    expect(new Date('2026-11-10T03:30:00Z').getHours()).toBe(22);
  });
});

describe('toZonedIsoString', () => {
  it('builds the booking startTime with the dealership offset (SPEC example)', () => {
    expect(toZonedIsoString(new Date('2026-11-10T03:30:00Z'), HCM)).toBe('2026-11-10T10:30:00+07:00');
  });

  it('moves to the next local day when the zone is ahead of UTC', () => {
    expect(toZonedIsoString(new Date('2026-11-09T18:00:00Z'), HCM)).toBe('2026-11-10T01:00:00+07:00');
  });

  it('handles negative offsets and daylight saving time', () => {
    expect(toZonedIsoString(new Date('2026-01-15T15:00:00Z'), 'America/New_York')).toBe('2026-01-15T10:00:00-05:00');
    expect(toZonedIsoString(new Date('2026-07-15T14:00:00Z'), 'America/New_York')).toBe('2026-07-15T10:00:00-04:00');
  });

  it('handles half-hour offsets and UTC', () => {
    expect(toZonedIsoString(new Date('2026-11-10T05:00:00Z'), 'Asia/Kolkata')).toBe('2026-11-10T10:30:00+05:30');
    expect(toZonedIsoString(new Date('2026-11-10T05:00:00Z'), 'UTC')).toBe('2026-11-10T05:00:00+00:00');
  });

  it('round-trips to the same instant', () => {
    const instant = new Date('2026-11-10T09:45:00Z');
    expect(new Date(toZonedIsoString(instant, HCM)).getTime()).toBe(instant.getTime());
  });
});

describe('getTimeZoneOffsetMinutes', () => {
  it('returns +420 for Ho Chi Minh City', () => {
    expect(getTimeZoneOffsetMinutes(new Date('2026-11-10T03:30:00Z'), HCM)).toBe(420);
  });
});

describe('display helpers use the dealership zone, not the browser zone', () => {
  const start = new Date('2026-11-10T03:30:00Z');
  const end = new Date('2026-11-10T04:30:00Z');

  it('formats times on a 24-hour clock', () => {
    expect(formatTime(start, HCM)).toBe('10:30');
    expect(formatTime(new Date('2026-11-09T17:00:00Z'), HCM)).toBe('00:00');
    expect(formatTimeRange(start, end, HCM)).toBe('10:30 – 11:30');
  });

  it('formats the local date', () => {
    expect(formatLongDate(start, HCM)).toBe('Tue 10 Nov 2026');
    expect(getMonthAndDay(new Date('2026-11-09T18:00:00Z'), HCM)).toEqual({ month: 'Nov', day: '10' });
  });

  it('gives the local calendar date and today in the zone', () => {
    const lateEveningUtc = new Date('2026-11-09T20:00:00Z');
    expect(toIsoDateInZone(lateEveningUtc, HCM)).toBe('2026-11-10');
    expect(todayInZone(HCM, lateEveningUtc)).toBe('2026-11-10');
    expect(todayInZone('America/New_York', lateEveningUtc)).toBe('2026-11-09');
  });

  it('formats a plain calendar date without shifting it', () => {
    expect(formatIsoDate('2026-11-10')).toBe('Tue 10 Nov 2026');
  });
});

describe('isIsoDate', () => {
  it('accepts real dates only', () => {
    expect(isIsoDate('2026-11-10')).toBe(true);
    expect(isIsoDate('2026-02-30')).toBe(false);
    expect(isIsoDate('2026-1-5')).toBe(false);
    expect(isIsoDate('')).toBe(false);
  });
});

describe('getCardDate', () => {
  const now = new Date('2026-11-01T05:00:00Z');

  it('hides the year for the current year and shows it otherwise', () => {
    expect(getCardDate(new Date('2026-11-10T03:30:00Z'), HCM, now)).toEqual({ month: 'Nov', day: '10', year: null });
    expect(getCardDate(new Date('2025-10-02T01:15:00Z'), HCM, now)).toEqual({ month: 'Oct', day: '2', year: '2025' });
  });

  it('decides the year in the dealership zone', () => {
    // 2026-12-31T18:00Z is already 1 Jan 2027 in Ho Chi Minh City.
    expect(getCardDate(new Date('2026-12-31T18:00:00Z'), HCM, now).year).toBe('2027');
  });
});

describe('calendar date helpers', () => {
  it('adds days and months across month and year ends', () => {
    expect(addDaysToIsoDate('2026-11-30', 1)).toBe('2026-12-01');
    expect(addDaysToIsoDate('2026-03-01', -1)).toBe('2026-02-28');
    expect(addMonthsIso('2026-11-10', 2)).toBe('2027-01-01');
    expect(addMonthsIso('2026-01-31', -1)).toBe('2025-12-01');
    expect(startOfMonthIso('2026-11-10')).toBe('2026-11-01');
    expect(endOfMonthIso('2028-02-10')).toBe('2028-02-29');
    expect(endOfMonthIso('2026-12-05')).toBe('2026-12-31');
  });

  it('round-trips a calendar date through a browser-local Date (New York has DST in November)', () => {
    for (const iso of ['2026-11-01', '2026-03-08', '2030-01-15']) {
      expect(calendarDateToIsoDate(isoDateToCalendarDate(iso))).toBe(iso);
    }
  });
});
