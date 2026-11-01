import { isApiError } from '../../api/types';

export type BookingErrorKind =
  | 'taken'
  | 'idempotency-conflict'
  | 'rule'
  | 'invalid'
  | 'not-found'
  | 'busy'
  | 'network'
  | 'unexpected';

export interface BookingErrorView {
  kind: BookingErrorKind;
  title: string;
  message: string;
  /** Extra lines, e.g. field errors from a 400. */
  details: string[];
  /** Show a "Try again" button that resends the same request with the same key. */
  canRetry: boolean;
  /** Suggested start instants (409 NO_AVAILABILITY only). */
  suggestedStarts: Date[];
}

/** Turns any booking error into the text and actions the UI shows. Pure, so it is easy to test. */
export function describeBookingError(error: unknown): BookingErrorView {
  const base = { details: [] as string[], canRetry: false, suggestedStarts: [] as Date[] };

  if (!isApiError(error)) {
    return { ...base, kind: 'unexpected', title: 'Something went wrong', message: 'Please try again.' };
  }

  switch (error.code) {
    case 'NO_AVAILABILITY': {
      const suggestedStarts = error.suggestedStartTimes
        .map((s) => new Date(s))
        .filter((d) => !Number.isNaN(d.getTime()));
      return {
        ...base,
        kind: 'taken',
        title: 'This time was just taken',
        message:
          suggestedStarts.length > 0
            ? 'Someone booked it a moment ago. Try one of these:'
            : 'Someone booked it a moment ago. There are no other free times close to it. Please pick another time or day.',
        suggestedStarts,
      };
    }
    case 'IDEMPOTENCY_CONFLICT':
      return {
        ...base,
        kind: 'idempotency-conflict',
        title: 'This booking request was already used',
        message: error.detail ?? 'The same request was sent before with different details. Please press Book appointment again.',
      };
    case 'BOOKING_RULE_VIOLATION':
      return {
        ...base,
        kind: 'rule',
        title: "This booking isn't allowed",
        message: error.detail ?? error.title,
      };
    case 'INVALID_REQUEST':
      return {
        ...base,
        kind: 'invalid',
        title: 'Please check your booking',
        message: error.detail ?? error.title,
        details: Object.values(error.errors).flat(),
      };
    case 'NOT_FOUND':
      return {
        ...base,
        kind: 'not-found',
        title: 'Something in this booking was not found',
        message: error.detail ?? error.title,
      };
    case 'DATABASE_BUSY':
      return {
        ...base,
        kind: 'busy',
        title: "We're busy right now",
        message: 'Your booking was not saved. Please try again in a moment.',
        canRetry: true,
      };
    case 'NETWORK_ERROR':
      return {
        ...base,
        kind: 'network',
        title: "We can't reach the server",
        message: 'Check your connection and try again.',
        canRetry: true,
      };
    default:
      // Fall back on the HTTP status for codes we do not know.
      if (error.status === 503) {
        return { ...base, kind: 'busy', title: "We're busy right now", message: 'Your booking was not saved. Please try again in a moment.', canRetry: true };
      }
      if (error.status === 422) {
        return { ...base, kind: 'rule', title: "This booking isn't allowed", message: error.detail ?? error.title };
      }
      if (error.status === 400) {
        return { ...base, kind: 'invalid', title: 'Please check your booking', message: error.detail ?? error.title, details: Object.values(error.errors).flat() };
      }
      if (error.status === 404) {
        return { ...base, kind: 'not-found', title: 'Something in this booking was not found', message: error.detail ?? error.title };
      }
      return {
        ...base,
        kind: 'unexpected',
        title: 'Something went wrong',
        message: error.detail ?? 'Please try again later.',
      };
  }
}
