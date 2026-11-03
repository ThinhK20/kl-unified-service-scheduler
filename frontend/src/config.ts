const DEFAULT_API_BASE_URL = 'http://localhost:5080';

/** Base URL of the Scheduler API, without a trailing slash. */
export const API_BASE_URL: string = (import.meta.env.VITE_API_BASE_URL || DEFAULT_API_BASE_URL).replace(
  /\/+$/,
  '',
);
