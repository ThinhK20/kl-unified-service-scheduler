import { afterEach, describe, expect, it, vi } from 'vitest';
import { bookAppointment, getAppointments, getDealerships } from '../api/appointmentsApi';
import { ApiError } from '../api/types';
import { installMockApi, STORAGE_KEY, type InstalledMockApi } from './mockApi';

function memoryStorage(): Storage {
  const data = new Map<string, string>();
  return {
    get length() {
      return data.size;
    },
    clear: () => data.clear(),
    getItem: (k) => data.get(k) ?? null,
    key: (i) => [...data.keys()][i] ?? null,
    removeItem: (k) => void data.delete(k),
    setItem: (k, v) => void data.set(k, v),
  };
}

const throwingStorage = {
  getItem: () => {
    throw new Error('blocked');
  },
  setItem: () => {
    throw new Error('blocked');
  },
} as unknown as Storage;

const now = () => Date.parse('2030-01-01T00:00:00Z');
const request = { customerId: 1, vehicleId: 1, dealershipId: 1, serviceTypeId: 1, startTime: '2030-01-15T10:00:00+07:00' };

let installed: InstalledMockApi | null = null;
afterEach(() => {
  installed?.uninstall();
  installed = null;
});

describe('installMockApi', () => {
  it('answers the real API layer through fetch, including typed errors', async () => {
    installed = installMockApi({ latencyMs: () => 0, storage: memoryStorage(), now });

    expect((await getDealerships()).map((d) => d.id)).toEqual([1, 2]);
    const booked = await bookAppointment(request, 'k1');
    expect(booked.technician.name).toBe('Nam');
    const conflict = await bookAppointment({ ...request, vehicleId: 2 }, 'k1').catch((e: unknown) => e);
    expect(conflict).toBeInstanceOf(ApiError);
    expect(conflict).toMatchObject({ status: 409, code: 'IDEMPOTENCY_CONFLICT' });
  });

  it('saves to storage and loads it again; reset restores the seed', async () => {
    const storage = memoryStorage();
    installed = installMockApi({ latencyMs: () => 0, storage, now });
    const seeded = (await getAppointments(1)).length;
    await bookAppointment(request, 'k1');
    installed.uninstall();

    installed = installMockApi({ latencyMs: () => 0, storage, now });
    expect((await getAppointments(1)).length).toBe(seeded + 1);
    expect(JSON.parse(storage.getItem(STORAGE_KEY) ?? '{}').nextId).toBe(seeded + 2);

    installed.reset();
    expect((await getAppointments(1)).length).toBe(seeded);
  });

  it('works when storage is blocked, and leaves other requests to the real fetch', async () => {
    const realFetch = vi.fn().mockResolvedValue(new Response('ok'));
    vi.stubGlobal('fetch', realFetch);
    installed = installMockApi({ latencyMs: () => 0, storage: throwingStorage, now });

    expect((await getDealerships()).length).toBe(2);
    await fetch('https://example.com/other');
    expect(realFetch).toHaveBeenCalledTimes(1);
    expect(realFetch.mock.calls[0]?.[0]).toBe('https://example.com/other');
    installed.uninstall();
    installed = null;
    vi.unstubAllGlobals();
  });
});
