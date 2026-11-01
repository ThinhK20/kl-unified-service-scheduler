import { describe, expect, it } from 'vitest';
import { createSeedState, MockSchedulerBackend, parseWithOffset, type MockState } from './mockBackend';

const DAY = '2030-01-15';
/** Local Ho Chi Minh City time on DAY -> request startTime with offset. */
const at = (hhmm: string, day = DAY) => `${day}T${hhmm}:00+07:00`;
/** Same instant as UTC ISO (what the API returns). */
const utc = (hhmm: string, day = DAY) => new Date(at(hhmm, day)).toISOString().replace('.000Z', 'Z');

const emptyState = (): MockState => ({ version: 1, nextId: 1, appointments: [], idempotency: {} });

function setup(nowIso = '2030-01-01T00:00:00Z') {
  let now = Date.parse(nowIso);
  const backend = new MockSchedulerBackend({ state: emptyState(), now: () => now });
  let keyCounter = 0;
  const call = (method: string, url: string, body?: unknown, key?: string | null) => {
    const headers: Record<string, string> = {};
    if (key !== null) headers['idempotency-key'] = key ?? `key-${++keyCounter}`;
    const response = backend.handle({ method, url, headers, body: body === undefined ? null : JSON.stringify(body) });
    return response as { status: number; headers: Record<string, string>; body: Record<string, unknown> & unknown[] };
  };
  const book = (vehicleId: number, serviceTypeId: number, startTime: string, key?: string, dealershipId?: number) =>
    call(
      'POST',
      '/api/appointments',
      { customerId: 1, vehicleId, dealershipId: dealershipId ?? (serviceTypeId === 5 ? 2 : 1), serviceTypeId, startTime },
      key,
    );
  const availability = (dealershipId: number, serviceTypeId: number, date = DAY) =>
    call('GET', `/api/dealerships/${dealershipId}/availability?serviceTypeId=${serviceTypeId}&date=${date}`);
  const freeStarts = (dealershipId: number, serviceTypeId: number) =>
    (availability(dealershipId, serviceTypeId).body.slots as { startUtc: string }[]).map((s) => s.startUtc);
  return { backend, call, book, availability, freeStarts, setNow: (iso: string) => (now = Date.parse(iso)) };
}

const assigned = (body: Record<string, unknown>) => ({
  technician: (body.technician as { name: string }).name,
  bay: (body.serviceBay as { name: string }).name,
});

describe('availability (same counts as the real API)', () => {
  it('counts free starts on an empty day', () => {
    const { availability, freeStarts } = setup();
    const ev = availability(1, 4);
    expect(ev.status).toBe(200);
    expect(ev.body).toMatchObject({ dealershipId: 1, serviceTypeId: 4, date: DAY, timeZoneId: 'Asia/Ho_Chi_Minh', durationMinutes: 60 });
    expect(freeStarts(1, 4)).toHaveLength(33); // 08:00..16:00
    expect(freeStarts(1, 2)).toHaveLength(31); // Brake inspection, 90 min: 08:00..15:30
    expect(freeStarts(1, 3)).toHaveLength(35); // Tyre rotation, 30 min: 08:00..16:30
    expect(freeStarts(2, 5)).toHaveLength(39); // Thu Duc 07:30-18:00, 60 min: 07:30..17:00
    expect(freeStarts(1, 4)[0]).toBe(utc('08:00'));
    expect(freeStarts(1, 4).at(-1)).toBe(utc('16:00'));
  });

  it('only returns starts in the future', () => {
    const { freeStarts } = setup('2030-01-15T03:05:00Z'); // 10:05 local
    expect(freeStarts(1, 4)[0]).toBe(utc('10:15'));
  });

  it('removes times where the only qualified technician or bay is busy (half-open ranges)', () => {
    const { book, freeStarts } = setup();
    expect(book(1, 4, at('10:00')).status).toBe(201); // Lan + Bay 3, 10:00-11:00
    const starts = freeStarts(1, 4);
    expect(starts).toContain(utc('09:00')); // ends exactly at 10:00: touching is fine
    expect(starts).not.toContain(utc('09:15'));
    expect(starts).not.toContain(utc('10:45'));
    expect(starts).toContain(utc('11:00'));
    expect(starts).toHaveLength(33 - 7);
  });

  it('validates the query like the API', () => {
    const { call, availability } = setup();
    expect(call('GET', '/api/dealerships/1/availability?serviceTypeId=4').body).toMatchObject({
      status: 400,
      code: 'INVALID_REQUEST',
      detail: 'date is required.',
    });
    expect(availability(1, 4, '2030-13-01').body.detail).toBe('date must be in the format YYYY-MM-DD.');
    expect(availability(9, 4).body).toMatchObject({ status: 404, code: 'NOT_FOUND', detail: 'Dealership 9 was not found.' });
    expect(availability(2, 4).body).toMatchObject({
      status: 422,
      code: 'BOOKING_RULE_VIOLATION',
      detail: 'The service type is not offered at this dealership.',
    });
  });
});

describe('booking and allocation (ScarcityAwareStrategy)', () => {
  it('oil change 10:00 goes to Nam, then EV battery check 10:00 goes to Lan', () => {
    const { book } = setup();
    const oil = book(1, 1, at('10:00'));
    expect(oil.status).toBe(201);
    expect(oil.headers.Location).toBe(`/api/appointments/${oil.body.id as number}`);
    expect(oil.body).toMatchObject({ status: 'Confirmed', startUtc: utc('10:00'), endUtc: utc('11:00') });
    expect(assigned(oil.body)).toEqual({ technician: 'Nam', bay: 'Bay 1' });

    const ev = book(2, 4, at('10:00'));
    expect(ev.status).toBe(201);
    expect(assigned(ev.body)).toEqual({ technician: 'Lan', bay: 'Bay 3' });
  });

  it('protects Lan (only EV technician) and prefers Minh for brakes when he is on shift', () => {
    const { book } = setup();
    expect(assigned(book(1, 1, at('13:00')).body).technician).toBe('Minh'); // fewest other services
    expect(assigned(book(2, 2, at('09:00')).body)).toEqual({ technician: 'Nam', bay: 'Bay 2' }); // Minh starts at 12:00
  });

  it('never uses inactive Tuan or inactive Bay 4', () => {
    const { book } = setup();
    expect(assigned(book(1, 2, at('13:00')).body)).toEqual({ technician: 'Minh', bay: 'Bay 2' });
    // Tyre rotation also needs a Lift bay. Bay 2 is busy; Bay 4 is inactive.
    const tyres = book(2, 3, at('13:00'));
    expect(tyres.status).toBe(409);
    expect(tyres.body.code).toBe('NO_AVAILABILITY');
  });

  it('VEHICLE_ALREADY_BOOKED when the same car overlaps, at any dealership, without suggestions', () => {
    const { book } = setup();
    expect(book(1, 1, at('10:00')).status).toBe(201);
    const twin = book(1, 5, at('10:30')); // Thu Duc, same car
    expect(twin.status).toBe(409);
    expect(twin.body).toMatchObject({
      code: 'VEHICLE_ALREADY_BOOKED',
      title: 'Vehicle already booked',
      detail: 'This vehicle already has a booking at that time.',
    });
    expect(twin.body).not.toHaveProperty('suggestedStartTimes');
    expect(book(1, 5, at('11:00')).status).toBe(201); // touching is fine
  });

  it('NO_AVAILABILITY suggests up to 3 nearest free times, earlier wins a tie, ascending', () => {
    const { book } = setup();
    expect(book(1, 4, at('10:00')).status).toBe(201); // Lan busy 10:00-11:00
    const taken = book(2, 4, at('10:15'));
    expect(taken.status).toBe(409);
    expect(taken.body).toMatchObject({ code: 'NO_AVAILABILITY', title: 'No availability' });
    // Distances from 10:15: 11:00=45, 11:15=60, 09:00=75 and 11:30=75 (tie -> earlier 09:00).
    expect(taken.body.suggestedStartTimes).toEqual([utc('09:00'), utc('11:00'), utc('11:15')]);
  });

  it('does not suggest times when the customer car is busy', () => {
    const { book } = setup();
    expect(book(1, 4, at('10:00')).status).toBe(201); // Lan busy 10:00-11:00
    expect(book(2, 1, at('11:30')).status).toBe(201); // car 2 busy 11:30-12:30
    const taken = book(2, 4, at('10:15'));
    expect(taken.body.code).toBe('NO_AVAILABILITY');
    expect(taken.body.suggestedStartTimes).toEqual([utc('08:30'), utc('08:45'), utc('09:00')]);
  });

  it('checks the request like the real API', () => {
    const { book, call } = setup();
    expect(call('POST', '/api/appointments', { customerId: 1, vehicleId: 1, dealershipId: 1, serviceTypeId: 1, startTime: at('10:00') }, null).body)
      .toMatchObject({ status: 400, code: 'INVALID_REQUEST', detail: 'The Idempotency-Key header is required.', errors: { 'Idempotency-Key': ['The Idempotency-Key header is required.'] } });
    expect(book(1, 1, '2030-01-15T10:00:00').body.detail).toMatch(/with an offset/);
    expect(book(1, 1, at('10:10')).body.detail).toMatch(/15-minute grid/);
    expect(book(1, 1, '2030-01-15T10:00:30+07:00').body.detail).toMatch(/15-minute grid/);
    expect(book(1, 1, at('16:30')).body).toMatchObject({
      status: 422,
      code: 'BOOKING_RULE_VIOLATION',
      detail: 'The appointment must be inside the dealership opening hours.',
    });
    expect(book(1, 1, at('10:00', '2029-12-01')).body).toMatchObject({ status: 400, detail: 'startTime must be in the future.' });
    expect(book(9, 1, at('10:00')).body).toMatchObject({ status: 404, detail: 'Vehicle 9 was not found.' });
    expect(book(1, 5, at('10:00'), undefined, 1).body).toMatchObject({ status: 422, detail: 'The service type is not offered at this dealership.' });
    const many = call('POST', '/api/appointments', {});
    expect(many.body.detail).toBe('The request is not valid. See the field errors.');
    expect(Object.keys(many.body.errors as object)).toEqual(['customerId', 'vehicleId', 'dealershipId', 'serviceTypeId', 'startTime']);
    expect(call('POST', '/api/appointments', { customerId: 'x' }).body).toMatchObject({ status: 400, errors: { customerId: ['The value is not valid.'] } });
  });
});

describe('idempotency', () => {
  it('same key + same body returns the same 201 without a second booking', () => {
    const { book, call, setNow } = setup();
    const first = book(1, 1, at('10:00'), 'abc');
    // Same instant written in UTC: same canonical request.
    const again = call('POST', '/api/appointments', { customerId: 1, vehicleId: 1, dealershipId: 1, serviceTypeId: 1, startTime: '2030-01-15T03:00:00Z' }, 'abc');
    expect(again.status).toBe(201);
    expect(again.body).toEqual(first.body);
    // A replay wins over the "in the future" rule.
    setNow('2030-02-01T00:00:00Z');
    expect(book(1, 1, at('10:00'), 'abc').status).toBe(201);
    expect((call('GET', '/api/appointments?customerId=1').body as unknown[]).length).toBe(1);
  });

  it('same key + different body is IDEMPOTENCY_CONFLICT; keys are case-sensitive', () => {
    const { book } = setup();
    book(1, 1, at('10:00'), 'abc');
    expect(book(1, 1, at('10:15'), 'abc').body).toMatchObject({
      status: 409,
      code: 'IDEMPOTENCY_CONFLICT',
      detail: 'This Idempotency-Key was already used with a different request body.',
    });
    expect(book(2, 1, at('13:00'), 'ABC').status).toBe(201);
  });
});

describe('appointments and cancel', () => {
  it('lists newest first and gets one by id', () => {
    const { book, call } = setup();
    book(1, 1, at('09:00'));
    book(2, 1, at('15:00'));
    const list = call('GET', '/api/appointments?customerId=1').body as { startUtc: string; id: number }[];
    expect(list.map((a) => a.startUtc)).toEqual([utc('15:00'), utc('09:00')]);
    expect(call('GET', `/api/appointments/${list[0]!.id}`).body).toMatchObject({ id: list[0]!.id });
    expect(call('GET', '/api/appointments/99').body).toMatchObject({ status: 404, detail: 'Appointment 99 was not found.' });
    expect(call('GET', '/api/appointments?customerId=0').body).toMatchObject({ status: 400, detail: 'customerId is required.' });
    expect(call('GET', '/api/appointments?customerId=7').body).toEqual([]);
  });

  it('cancel frees the slots; only Confirmed and before the start', () => {
    const { book, call, setNow, freeStarts } = setup();
    const ev = book(1, 4, at('10:00'));
    const id = ev.body.id as number;
    expect(freeStarts(1, 4)).not.toContain(utc('10:00'));

    const cancelled = call('POST', `/api/appointments/${id}/cancel`);
    expect(cancelled.status).toBe(200);
    expect(cancelled.body.status).toBe('Cancelled');
    expect(freeStarts(1, 4)).toContain(utc('10:00'));
    expect(book(2, 4, at('10:00')).status).toBe(201);

    expect(call('POST', `/api/appointments/${id}/cancel`).body).toMatchObject({
      status: 409,
      code: 'APPOINTMENT_NOT_CANCELLABLE',
      title: 'Appointment cannot be cancelled',
      detail: 'Only confirmed appointments can be cancelled. This appointment is Cancelled.',
    });

    const later = book(1, 1, at('14:00')).body.id as number;
    setNow('2030-01-15T07:00:00Z'); // 14:00 local: it has started
    expect(call('POST', `/api/appointments/${later}/cancel`).body).toMatchObject({
      status: 409,
      detail: 'This appointment has already started or passed.',
    });
    expect(call('POST', '/api/appointments/99/cancel').body).toMatchObject({ status: 404 });
  });
});

describe('catalog, routes and seed', () => {
  it('serves the seeded catalog', () => {
    const { call } = setup();
    expect((call('GET', '/api/dealerships').body as { name: string }[]).map((d) => d.name)).toEqual([
      'District 1 Service Centre',
      'Thu Duc Service Centre',
    ]);
    expect((call('GET', '/api/service-types?dealershipId=1').body as unknown[]).length).toBe(4);
    expect((call('GET', '/api/service-types').body as unknown[]).length).toBe(5);
    expect((call('GET', '/api/customers/1/vehicles').body as { vin: string }[])[0]!.vin).toBe('JTDBT923X71000001');
    expect(call('GET', '/api/customers/2/vehicles').body).toMatchObject({ status: 404, detail: 'Customer 2 was not found.' });
  });

  it('answers unknown routes and wrong methods with ProblemDetails', () => {
    const { call } = setup();
    expect(call('GET', '/api/nope').body).toMatchObject({ status: 404, code: 'NOT_FOUND', detail: 'No resource at /api/nope.' });
    expect(call('GET', '/api/appointments/1/cancel').body).toMatchObject({ status: 405, code: 'INVALID_REQUEST' });
  });

  it('seeds three example appointments around today', () => {
    const now = Date.parse('2026-11-01T05:00:00Z');
    const backend = new MockSchedulerBackend({ state: createSeedState(now), now: () => now });
    const list = backend.handle({ method: 'GET', url: '/api/appointments?customerId=1', headers: {}, body: null })
      .body as { status: string; startUtc: string; id: number }[];
    expect(list.map((a) => a.status)).toEqual(['Confirmed', 'Cancelled', 'Completed']);
    expect(Date.parse(list[0]!.startUtc)).toBeGreaterThan(now);
    expect(list[0]!.startUtc).toBe('2026-11-04T03:30:00Z'); // 3 days ahead, 10:30 local
    expect(list[2]!.startUtc).toBe('2026-10-02T02:00:00Z'); // last month
    const cancel = backend.handle({ method: 'POST', url: `/api/appointments/${list[0]!.id}/cancel`, headers: {}, body: null });
    expect(cancel.status).toBe(200);
  });
});

describe('parseWithOffset', () => {
  it('requires an offset and handles fractions', () => {
    expect(parseWithOffset('2030-01-15T10:30:00+07:00')).toEqual({ utc: Date.parse('2030-01-15T03:30:00Z'), wholeSeconds: true });
    expect(parseWithOffset('2030-01-15T03:30Z')).toEqual({ utc: Date.parse('2030-01-15T03:30:00Z'), wholeSeconds: true });
    expect(parseWithOffset('2030-01-15T03:30:00.0000001Z')?.wholeSeconds).toBe(false);
    expect(parseWithOffset('2030-01-15T10:30:00')).toBeNull();
    expect(parseWithOffset('2030-02-30T10:30:00Z')).toBeNull();
  });
});
