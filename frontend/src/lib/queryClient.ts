import { QueryClient } from '@tanstack/react-query';
import { isApiError } from '../api/types';

/** Retry a failed read once, but never for 4xx answers (they will not change on retry). */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: (failureCount, error) => {
          if (isApiError(error) && error.status >= 400 && error.status < 500) return false;
          return failureCount < 1;
        },
      },
      mutations: { retry: false },
    },
  });
}
