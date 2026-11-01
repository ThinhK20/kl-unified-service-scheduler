// Installs the in-browser preview API: a window.fetch interceptor that answers requests to API_BASE_URL
// from MockSchedulerBackend. Other requests go to the real fetch. State is kept in memory and saved to
// localStorage when it is available (private windows and blocked storage still work, just without saving).

import { API_BASE_URL } from '../config';
import { MockSchedulerBackend, type MockState } from './mockBackend';

export const STORAGE_KEY = 'scheduler-preview-state-v1';

export interface InstallOptions {
  /** Extra delay per request so loading states are visible. Default 150-400 ms. */
  latencyMs?: () => number;
  storage?: Storage | null;
  now?: () => number;
}

export interface InstalledMockApi {
  backend: MockSchedulerBackend;
  /** Restore the seed data and save it. */
  reset: () => void;
  /** Put the real fetch back. */
  uninstall: () => void;
}

function defaultStorage(): Storage | null {
  try {
    return typeof window !== 'undefined' ? window.localStorage : null;
  } catch {
    return null;
  }
}

function loadState(storage: Storage | null): MockState | undefined {
  if (!storage) return undefined;
  try {
    const raw = storage.getItem(STORAGE_KEY);
    if (!raw) return undefined;
    const parsed = JSON.parse(raw) as Partial<MockState>;
    if (parsed.version !== 1 || !Array.isArray(parsed.appointments) || typeof parsed.nextId !== 'number') return undefined;
    return { version: 1, nextId: parsed.nextId, appointments: parsed.appointments, idempotency: parsed.idempotency ?? {} };
  } catch {
    return undefined;
  }
}

function saveState(storage: Storage | null, state: MockState): void {
  if (!storage) return;
  try {
    storage.setItem(STORAGE_KEY, JSON.stringify(state));
  } catch {
    // Storage full or blocked: keep working in memory.
  }
}

const randomLatency = () => 150 + Math.floor(Math.random() * 251);

function wait(ms: number, signal: AbortSignal | null | undefined): Promise<void> {
  return new Promise((resolve, reject) => {
    const abortError = () => new DOMException('The operation was aborted.', 'AbortError');
    if (signal?.aborted) {
      reject(abortError());
      return;
    }
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort);
      resolve();
    }, ms);
    const onAbort = () => {
      clearTimeout(timer);
      reject(abortError());
    };
    signal?.addEventListener('abort', onAbort, { once: true });
  });
}

function headersToRecord(headers: HeadersInit | undefined): Record<string, string> {
  const result: Record<string, string> = {};
  new Headers(headers).forEach((value, name) => {
    result[name.toLowerCase()] = value;
  });
  return result;
}

export function installMockApi(options: InstallOptions = {}): InstalledMockApi {
  const storage = options.storage === undefined ? defaultStorage() : options.storage;
  const latency = options.latencyMs ?? randomLatency;
  const backendOptions: ConstructorParameters<typeof MockSchedulerBackend>[0] = {
    onChange: (state) => saveState(storage, state),
  };
  const saved = loadState(storage);
  if (saved) backendOptions.state = saved;
  if (options.now) backendOptions.now = options.now;
  const backend = new MockSchedulerBackend(backendOptions);
  if (!saved) saveState(storage, backend.snapshot());

  const realFetch = globalThis.fetch.bind(globalThis);

  const mockFetch: typeof fetch = async (input, init) => {
    const request = input instanceof Request ? input : null;
    const url = request ? request.url : String(input);
    if (!url.startsWith(API_BASE_URL)) return realFetch(input, init);

    const method = init?.method ?? request?.method ?? 'GET';
    const headers = headersToRecord(init?.headers ?? request?.headers);
    const signal = init?.signal ?? request?.signal;
    let body: string | null = null;
    if (typeof init?.body === 'string') body = init.body;
    else if (request && !init?.body) body = await request.clone().text();

    await wait(latency(), signal);
    const response = backend.handle({ method, url: url.slice(API_BASE_URL.length) || '/', headers, body });
    return new Response(response.body === undefined ? null : JSON.stringify(response.body), {
      status: response.status,
      headers: response.headers,
    });
  };

  globalThis.fetch = mockFetch;

  return {
    backend,
    reset: () => backend.reset(),
    uninstall: () => {
      if (globalThis.fetch === mockFetch) globalThis.fetch = realFetch;
    },
  };
}
