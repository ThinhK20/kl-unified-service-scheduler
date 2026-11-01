/** The app has no login. Every request is made for this one demo customer (SPEC section 0). */
export const DEMO_CUSTOMER_ID = 1;

const DEFAULT_API_BASE_URL = 'http://localhost:5080';

/** Base URL of the Scheduler API, without a trailing slash. */
export const API_BASE_URL: string = (import.meta.env.VITE_API_BASE_URL || DEFAULT_API_BASE_URL).replace(
  /\/+$/,
  '',
);
