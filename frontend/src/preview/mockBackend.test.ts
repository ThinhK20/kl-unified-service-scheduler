import { describe, expect, it } from 'vitest';
import { compareOrdinalIgnoreCase, createSeedState, MockSchedulerBackend, parseWithOffset, type MockState } from './mockBackend';
import { bays, dealerships, serviceTypes, technicians } from './seed';

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

type Time = { startUtc: string; endUtc: string; status: string };
type Day = { date: string; availableCount: number; status: string };

describe('availability v2: times with a status (contract section 1)', () => {
  const times = (body: unknown) => (body as { times: Time[] }).times;
  const statusAt = (list: Time[], hhmm: string) => list.find((t) => t.startUtc === utc(hhmm))?.status;
  const day = (dealershipId: number, serviceTypeId: number, vehicleId?: number | string, date = DAY) =>
    `/api/dealerships/${dealershipId}/availability?serviceTypeId=${serviceTypeId}&date=${date}` +
    (vehicleId === undefined ? '' : `&vehicleId=${vehicleId}`);

  it('lists every grid start; slots are exactly the Available times', () => {
    const { call, book } = setup();
    book(1, 4, at('10:00')); // Lan + EV bay busy 10:00-11:00
    const body = call('GET', day(1, 4)).body as unknown as { slots: { startUtc: string }[]; times: Time[] };
    expect(body.times).toHaveLength(33); // 08:00..16:00
    expect(body.times.map((t) => t.startUtc)).toEqual([...body.times.map((t) => t.startUtc)].sort());
    expect(body.times[0]).toEqual({ startUtc: utc('08:00'), endUtc: utc('09:00'), status: 'Available' });
    expect(statusAt(body.times, '09:15')).toBe('Booked');
    expect(statusAt(body.times, '10:45')).toBe('Booked');
    expect(statusAt(body.times, '11:00')).toBe('Available');
    expect(body.slots).toEqual(body.times.filter((t) => t.status === 'Available').map(({ startUtc, endUtc }) => ({ startUtc, endUtc })));
  });

  it('without vehicleId the slots are unchanged from v1; with it they are car-aware', () => {
    const { call, book } = setup();
    book(1, 1, at('13:00')); // the Toyota has an oil change 13:00-14:00 (Minh + Bay 1)
    const v1 = call('GET', day(1, 4)).body as unknown as { slots: unknown[]; times: Time[] };
    expect(v1.slots).toHaveLength(33);
    const toyota = times(call('GET', day(1, 4, 1)).body);
    expect(statusAt(toyota, '12:15')).toBe('VehicleBooked');
    expect(statusAt(toyota, '13:45')).toBe('VehicleBooked');
    expect(statusAt(toyota, '12:00')).toBe('Available'); // ends exactly at 13:00
    expect(statusAt(toyota, '14:00')).toBe('Available');
    expect((call('GET', day(1, 4, 1)).body as unknown as { slots: unknown[] }).slots).toHaveLength(33 - 7);
    expect(times(call('GET', day(1, 4, 2)).body).every((t) => t.status === 'Available')).toBe(true);
  });

  it('first match wins: Past over VehicleBooked over Booked', () => {
    const { call, book, setNow } = setup();
    book(1, 4, at('10:00')); // Toyota holds Lan + EV bay 10:00-11:00: Booked AND VehicleBooked for the Toyota
    expect(statusAt(times(call('GET', day(1, 4, 1)).body), '10:00')).toBe('VehicleBooked');
    expect(statusAt(times(call('GET', day(1, 4, 2)).body), '10:00')).toBe('Booked');
    setNow(`${utc('10:00')}`); // start <= now is Past
    const later = times(call('GET', day(1, 4, 1)).body);
    expect(statusAt(later, '10:00')).toBe('Past');
    expect(statusAt(later, '10:15')).toBe('VehicleBooked');
    expect(later.filter((t) => t.status === 'Past')).toHaveLength(9); // 08:00..10:00
  });

  it('every Available time books with 201 for that vehicle; Past, VehicleBooked and Booked do not', () => {
    const nowIso = '2030-01-15T02:40:00Z'; // 09:40 local: 08:00..09:30 have passed
    const { backend, call, book } = setup(nowIso);
    expect(book(1, 1, at('13:00')).status).toBe(201); // Toyota busy 13:00-14:00
    expect(book(2, 4, at('11:00')).status).toBe(201); // Lan + EV bay busy 11:00-12:00
    const snapshot = backend.snapshot();
    const list = times(call('GET', day(1, 4, 1)).body);
    expect(new Set(list.map((t) => t.status))).toEqual(new Set(['Past', 'Available', 'Booked', 'VehicleBooked']));
    expect(list.filter((t) => t.status === 'Past')).toHaveLength(7);
    for (const t of list) {
      const fresh = new MockSchedulerBackend({ state: snapshot, now: () => Date.parse(nowIso) });
      const response = fresh.handle({
        method: 'POST',
        url: '/api/appointments',
        headers: { 'idempotency-key': `k-${t.startUtc}` },
        body: JSON.stringify({ customerId: 1, vehicleId: 1, dealershipId: 1, serviceTypeId: 4, startTime: t.startUtc }),
      });
      const expected = { Past: 400, Available: 201, Booked: 409, VehicleBooked: 409 }[t.status];
      expect([t.startUtc, t.status, response.status]).toEqual([t.startUtc, t.status, expected]);
      if (t.status === 'Past') expect((response.body as { detail: string }).detail).toBe('startTime must be in the future.');
      if (t.status === 'VehicleBooked') expect((response.body as { code: string }).code).toBe('VEHICLE_ALREADY_BOOKED');
      if (t.status === 'Booked') expect((response.body as { code: string }).code).toBe('NO_AVAILABILITY');
    }
  });

  it('validates vehicleId: positive integer, known vehicle', () => {
    const { call } = setup();
    for (const bad of ['0', '-1', 'abc', '1.5']) {
      expect(call('GET', day(1, 4, bad)).body).toMatchObject({
        status: 400,
        code: 'INVALID_REQUEST',
        detail: 'vehicleId must be a positive number.',
        errors: { vehicleId: ['vehicleId must be a positive number.'] },
      });
    }
    expect(call('GET', day(1, 4, 99)).body).toMatchObject({ status: 404, code: 'NOT_FOUND', detail: 'Vehicle 99 was not found.' });
    expect(call('GET', day(9, 4, 99)).body).toMatchObject({ status: 404, detail: 'Dealership 9 was not found.' });
    expect(call('GET', day(1, 4, '')).status).toBe(400); // present but empty = 400, like the real API
    expect(call('GET', day(1, 4, '2147483648')).status).toBe(400); // larger than an int
  });
});

describe('availability v2: days (contract section 2)', () => {
  const daysUrl = (from: string, to: string, extra = '', serviceTypeId = 4) =>
    `/api/dealerships/1/availability/days?serviceTypeId=${serviceTypeId}&from=${from}&to=${to}${extra}`;
  const days = (body: unknown) => (body as { days: Day[] }).days;

  it('returns one entry per date, Past before today, FullyBooked when nothing is Available', () => {
    const { call, book, setNow } = setup();
    // Fill the whole of 2030-01-20 for the EV service (one technician, one bay): 08:00, 09:00, ... 16:00.
    for (let h = 8; h <= 16; h++) expect(book(h % 2 ? 1 : 2, 4, at(`${String(h).padStart(2, '0')}:00`, '2030-01-20')).status).toBe(201);
    setNow('2030-01-15T09:30:00Z'); // 16:30 local on the 15th: every start that day has passed
    const body = call('GET', daysUrl('2030-01-13', '2030-01-21')).body as unknown as Record<string, unknown>;
    expect(body).toMatchObject({ dealershipId: 1, serviceTypeId: 4, from: '2030-01-13', to: '2030-01-21', timeZoneId: 'Asia/Ho_Chi_Minh', durationMinutes: 60 });
    expect(days(body).map((d) => [d.date, d.status, d.availableCount])).toEqual([
      ['2030-01-13', 'Past', 0],
      ['2030-01-14', 'Past', 0],
      ['2030-01-15', 'FullyBooked', 0], // today, all passed
      ['2030-01-16', 'Available', 33],
      ['2030-01-17', 'Available', 33],
      ['2030-01-18', 'Available', 33],
      ['2030-01-19', 'Available', 33],
      ['2030-01-20', 'FullyBooked', 0],
      ['2030-01-21', 'Available', 33],
    ]);
  });

  it('gives the same answer as the day endpoint for each date, with and without a vehicle', () => {
    const { call, book, setNow } = setup();
    book(1, 4, at('10:00', '2030-01-16'));
    book(2, 1, at('14:00', '2030-01-17'));
    book(1, 2, at('09:00', '2030-01-31'));
    setNow('2030-01-16T02:20:00Z'); // 09:20 local on the 16th
    for (const extra of ['', '&vehicleId=1', '&vehicleId=2']) {
      for (const service of [1, 2, 3, 4]) {
        const list = days(call('GET', daysUrl('2030-01-01', '2030-02-11', extra, service)).body);
        expect(list).toHaveLength(42);
        for (const d of list) {
          const one = call('GET', `/api/dealerships/1/availability?serviceTypeId=${service}&date=${d.date}${extra}`).body as unknown as {
            times: Time[];
          };
          const count = one.times.filter((t) => t.status === 'Available').length;
          expect([d.date, d.availableCount]).toEqual([d.date, d.status === 'Past' ? 0 : count]);
          if (d.status === 'Past') expect(d.date < '2030-01-16').toBe(true);
          else expect(d.status).toBe(count === 0 ? 'FullyBooked' : 'Available');
        }
      }
    }
  });

  it('validates the range with the contract messages', () => {
    const { call } = setup();
    const err = (url: string) => call('GET', url).body;
    expect(err(daysUrl('2030-01-10', '2030-01-09'))).toMatchObject({
      status: 400,
      code: 'INVALID_REQUEST',
      detail: 'to must be on or after from.',
      errors: { to: ['to must be on or after from.'] },
    });
    expect(err(daysUrl('2030-01-01', '2030-02-12')).detail).toBe('The range can be at most 42 days.');
    expect(call('GET', daysUrl('2030-01-01', '2030-02-11')).status).toBe(200); // exactly 42
    expect(call('GET', daysUrl('2030-01-10', '2030-01-10')).status).toBe(200);
    expect(err('/api/dealerships/1/availability/days?serviceTypeId=4&to=2030-01-10').detail).toBe('from is required.');
    expect(err(daysUrl('2030-1-01', '2030-01-10')).detail).toBe('from must be in the format YYYY-MM-DD.');
    expect(err(daysUrl('2030-01-01', '1999-12-31')).detail).toBe('to must be between the years 2000 and 9998.');
    expect(err('/api/dealerships/1/availability/days?serviceTypeId=0&from=2030-01-01&to=2030-01-02')).toMatchObject({
      detail: 'serviceTypeId is required.',
    });
    const many = err('/api/dealerships/1/availability/days?vehicleId=0');
    expect(many.detail).toBe('The request is not valid. See the field errors.');
    expect(Object.keys(many.errors as object)).toEqual(['serviceTypeId', 'from', 'to', 'vehicleId']);
    expect(err(daysUrl('2030-01-01', '2030-01-02', '&vehicleId=0')).detail).toBe('vehicleId must be a positive number.');
    expect(err(daysUrl('2030-01-01', '2030-01-02', '&vehicleId=7'))).toMatchObject({ status: 404, detail: 'Vehicle 7 was not found.' });
    expect(err('/api/dealerships/9/availability/days?serviceTypeId=4&from=2030-01-01&to=2030-01-02')).toMatchObject({ status: 404, detail: 'Dealership 9 was not found.' });
    expect(err('/api/dealerships/2/availability/days?serviceTypeId=4&from=2030-01-01&to=2030-01-02')).toMatchObject({
      status: 422,
      detail: 'The service type is not offered at this dealership.',
    });
  });
});

type Person = { id: number; name: string; state: string; bayType?: string };
type Preview = {
  status: string;
  technician: { id: number; name: string } | null;
  serviceBay: { id: number; name: string; bayType: string } | null;
  technicians: Person[];
  serviceBays: Person[];
} & Record<string, unknown>;

describe('assignment preview (customers contract section 3)', () => {
  const previewUrl = (serviceTypeId: number | string, startTime: string, extra = '', dealershipId = 1) =>
    `/api/dealerships/${dealershipId}/assignment-preview?serviceTypeId=${serviceTypeId}&startTime=${encodeURIComponent(startTime)}${extra}`;
  const states = (rows: Person[]) => rows.map((r) => `${r.name}:${r.state}`);

  it('EV battery check 10:00 on an empty day: Lan and Bay 3 will serve; every technician and bay with a state, in order', () => {
    const { call } = setup();
    const response = call('GET', previewUrl(4, at('10:00')));
    expect(response.status).toBe(200);
    const body = response.body as unknown as Preview;
    expect(body).toMatchObject({
      dealershipId: 1,
      serviceTypeId: 4,
      timeZoneId: 'Asia/Ho_Chi_Minh',
      startUtc: utc('10:00'),
      endUtc: utc('11:00'),
      status: 'Assignable',
      technician: { id: 2, name: 'Lan' },
      serviceBay: { id: 3, name: 'Bay 3', bayType: 'EV' },
    });
    expect(states(body.technicians)).toEqual(['Lan:WillServe', 'Minh:NotQualified', 'Nam:NotQualified', 'Tuan:Inactive']);
    expect(states(body.serviceBays)).toEqual(['Bay 3:WillServe', 'Bay 1:WrongType', 'Bay 2:WrongType', 'Bay 4:Inactive']);
    expect(body.serviceBays[0]).toEqual({ id: 3, name: 'Bay 3', bayType: 'EV', state: 'WillServe' });
  });

  it('follows the scarcity strategy: an oil change at 10:00 previews Nam (Minh is off shift), at 13:00 Minh', () => {
    const { call } = setup();
    const morning = call('GET', previewUrl(1, at('10:00'))).body as unknown as Preview;
    expect(morning.technician).toEqual({ id: 1, name: 'Nam' });
    expect(states(morning.technicians)).toEqual(['Nam:WillServe', 'Lan:Free', 'Minh:OffShift', 'Tuan:Inactive']);
    expect(states(morning.serviceBays)).toEqual(['Bay 1:WillServe', 'Bay 2:WrongType', 'Bay 3:WrongType', 'Bay 4:Inactive']);
    expect((call('GET', previewUrl(1, at('13:00'))).body as unknown as Preview).technician?.name).toBe('Minh');
  });

  it('shows Busy, then NoAvailability without a pick; VehicleBooked and Past keep the other states', () => {
    const { call, book, setNow } = setup();
    expect(book(1, 4, at('10:00')).status).toBe(201); // Toyota: Lan + Bay 3, 10:00-11:00
    const taken = call('GET', previewUrl(4, at('10:30'), '&vehicleId=2')).body as unknown as Preview;
    expect(taken).toMatchObject({ status: 'NoAvailability', technician: null, serviceBay: null });
    expect(states(taken.technicians)).toEqual(['Lan:Busy', 'Minh:NotQualified', 'Nam:NotQualified', 'Tuan:Inactive']);
    expect(states(taken.serviceBays)[0]).toBe('Bay 3:Busy');
    expect(taken.technicians.some((t) => t.state === 'WillServe')).toBe(false);

    const car = call('GET', previewUrl(1, at('10:30'), '&vehicleId=1')).body as unknown as Preview;
    expect(car).toMatchObject({ status: 'VehicleBooked', technician: null, serviceBay: null });
    expect(states(car.technicians)).toEqual(['Nam:Free', 'Lan:Busy', 'Minh:OffShift', 'Tuan:Inactive']);

    setNow(utc('10:30'));
    const past = call('GET', previewUrl(1, at('10:30'), '&vehicleId=1')).body as unknown as Preview;
    expect(past).toMatchObject({ status: 'Past', technician: null });
    expect(states(past.technicians)).toEqual(['Nam:Free', 'Lan:Busy', 'Minh:OffShift', 'Tuan:Inactive']);
  });

  it('invariant: an Assignable preview books right away with 201 and exactly that technician and bay', () => {
    const { backend, call, book } = setup();
    book(1, 1, at('10:00'));
    book(2, 2, at('13:00'));
    book(1, 4, at('15:00'));
    const snapshot = backend.snapshot();
    let checked = 0;
    for (const service of [1, 2, 3, 4]) {
      for (const vehicleId of [1, 2, 4]) {
        for (let minutes = 8 * 60; minutes <= 16 * 60; minutes += 45) {
          const hhmm = `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
          const preview = call('GET', previewUrl(service, at(hhmm), `&vehicleId=${vehicleId}`));
          if (preview.status !== 200) continue; // outside opening hours for the longer services
          const body = preview.body as unknown as Preview;
          const fresh = new MockSchedulerBackend({ state: snapshot, now: () => Date.parse('2030-01-01T00:00:00Z') });
          const customerId = vehicleId === 4 ? 3 : 1;
          const booked = fresh.handle({
            method: 'POST',
            url: '/api/appointments',
            headers: { 'idempotency-key': `k-${service}-${vehicleId}-${hhmm}` },
            body: JSON.stringify({ customerId, vehicleId, dealershipId: 1, serviceTypeId: service, startTime: at(hhmm) }),
          });
          const result = booked.body as { technician?: { id: number }; serviceBay?: { id: number }; code?: string };
          if (body.status === 'Assignable') {
            expect([service, vehicleId, hhmm, booked.status, result.technician?.id, result.serviceBay?.id]).toEqual([
              service, vehicleId, hhmm, 201, body.technician?.id, body.serviceBay?.id,
            ]);
            checked++;
          } else {
            expect(booked.status).toBe(409);
            expect(result.code).toBe(body.status === 'VehicleBooked' ? 'VEHICLE_ALREADY_BOOKED' : 'NO_AVAILABILITY');
          }
        }
      }
    }
    expect(checked).toBeGreaterThan(60);
  });

  it('validates like the booking request and the availability endpoints', () => {
    const { call } = setup();
    const err = (url: string) => call('GET', url).body;
    expect(err('/api/dealerships/1/assignment-preview?serviceTypeId=4')).toMatchObject({ status: 400, detail: 'startTime is required.' });
    expect(err(previewUrl(4, '2030-01-15T10:00:00')).detail).toMatch(/with an offset, for example 2026-11-10T10:30:00\+07:00\.$/);
    expect(err(previewUrl(4, at('10:10'))).detail).toBe('startTime must be on the 15-minute grid (:00, :15, :30 or :45, with zero seconds).');
    expect(err(previewUrl(4, '1999-01-15T10:00:00+07:00')).detail).toBe('startTime must be between the years 2000 and 9998.');
    expect(err(previewUrl(0, at('10:00'))).detail).toBe('serviceTypeId is required.');
    expect(err(previewUrl('x', at('10:00'))).errors).toEqual({ serviceTypeId: ['The value is not valid.'] });
    for (const bad of ['', '0', '-3', 'abc', '2147483648']) {
      expect(err(previewUrl(4, at('10:00'), `&vehicleId=${bad}`))).toMatchObject({ status: 400, detail: 'vehicleId must be a positive number.' });
    }
    const many = err('/api/dealerships/1/assignment-preview?vehicleId=0');
    expect(Object.keys(many.errors as object)).toEqual(['serviceTypeId', 'startTime', 'vehicleId']);
    // A UTC "Z" time is an offset too.
    expect(call('GET', previewUrl(4, utc('10:00'))).status).toBe(200);
  });

  it('404 in the order dealership, service type, vehicle; then 422 offer rules and opening hours', () => {
    const { call } = setup();
    const err = (url: string) => call('GET', url).body;
    expect(err(previewUrl(99, at('10:00'), '&vehicleId=99', 9)).detail).toBe('Dealership 9 was not found.');
    expect(err(previewUrl(99, at('10:00'), '&vehicleId=99')).detail).toBe('Service type 99 was not found.');
    expect(err(previewUrl(4, at('10:00'), '&vehicleId=99'))).toMatchObject({ status: 404, detail: 'Vehicle 99 was not found.' });
    expect(err(previewUrl(5, at('10:00')))).toMatchObject({ status: 422, detail: 'The service type is not offered at this dealership.' });
    expect(err(previewUrl(4, at('16:15')))).toMatchObject({
      status: 422,
      code: 'BOOKING_RULE_VIOLATION',
      detail: 'The appointment must be inside the dealership opening hours.',
    });
    expect(call('GET', previewUrl(4, at('16:00'))).status).toBe(200);
    // A past time is not an error, even outside opening hours (Past is checked first).
    expect((call('GET', previewUrl(4, at('10:00', '2029-12-01'))).body as unknown as Preview).status).toBe('Past');
    const pastAfterHours = call('GET', previewUrl(4, at('16:30', '2029-12-01')));
    expect(pastAfterHours.status).toBe(200);
    expect((pastAfterHours.body as unknown as Preview).status).toBe('Past');
    expect(call('GET', previewUrl(4, at('07:00', '2029-12-01'))).status).toBe(200);
  });

  /** Runs `body` with seed rows switched off, then restores them (the seed arrays are module state). */
  function withInactive(rows: { isActive: boolean }[], body: () => void) {
    const before = rows.map((r) => r.isActive);
    rows.forEach((r) => (r.isActive = false));
    try {
      body();
    } finally {
      rows.forEach((r, i) => (r.isActive = before[i]!));
    }
  }

  it('NoAvailability when no bay of the required type is active, while qualified technicians are Free and Busy', () => {
    withInactive([bays.find((b) => b.name === 'Bay 2')!], () => {
      const { call, book } = setup();
      expect(assigned(book(1, 1, at('13:00')).body).technician).toBe('Minh'); // Minh busy 13:00-14:00
      const brakes = call('GET', previewUrl(2, at('13:00'))).body as unknown as Preview;
      expect(brakes).toMatchObject({ status: 'NoAvailability', technician: null, serviceBay: null });
      expect(states(brakes.technicians)).toEqual(['Nam:Free', 'Minh:Busy', 'Lan:NotQualified', 'Tuan:Inactive']);
      expect(states(brakes.serviceBays)).toEqual(['Bay 1:WrongType', 'Bay 3:WrongType', 'Bay 2:Inactive', 'Bay 4:Inactive']);
      // Booking agrees.
      expect(book(2, 2, at('13:00')).body.code).toBe('NO_AVAILABILITY');
    });
  });

  it('NoAvailability when no qualified technician is active, while the right bays are Free and Busy', () => {
    withInactive([technicians.find((t) => t.name === 'Nam')!, technicians.find((t) => t.name === 'Minh')!], () => {
      const { call, book } = setup();
      // Before switching Nam and Minh off, nobody else could have taken Bay 2; use tyres (Lan) to make it busy.
      expect(assigned(book(1, 3, at('10:00')).body)).toEqual({ technician: 'Lan', bay: 'Bay 2' });
      const brakes = call('GET', previewUrl(2, at('10:00'))).body as unknown as Preview;
      expect(brakes).toMatchObject({ status: 'NoAvailability', technician: null });
      expect(states(brakes.technicians)).toEqual(['Lan:NotQualified', 'Minh:Inactive', 'Nam:Inactive', 'Tuan:Inactive']);
      expect(states(brakes.serviceBays)).toEqual(['Bay 2:Busy', 'Bay 1:WrongType', 'Bay 3:WrongType', 'Bay 4:Inactive']);
      const later = call('GET', previewUrl(2, at('13:00'))).body as unknown as Preview;
      expect(states(later.serviceBays)[0]).toBe('Bay 2:Free');
      expect(later.status).toBe('NoAvailability');
    });
  });

  it('sorts names like OrdinalIgnoreCase', () => {
    expect(['bay 10', 'Bay 2', 'BAY 1', 'Bay 1b'].sort(compareOrdinalIgnoreCase)).toEqual(['BAY 1', 'bay 10', 'Bay 1b', 'Bay 2']);
    expect(compareOrdinalIgnoreCase('lan', 'LAN')).toBe(0);
    // Upper case, not lower case: "_" (95) sorts after "A" (65), unlike a lower-case comparison with "a" (97).
    expect(compareOrdinalIgnoreCase('_a', 'a')).toBeGreaterThan(0);
    // Length-preserving: "ß" is not expanded to "SS".
    expect(compareOrdinalIgnoreCase('straße', 'STRASSE')).not.toBe(0);
  });
});

describe('dealership setup (setup contract section 1)', () => {
  type Setup = {
    dealership: Record<string, unknown>;
    serviceTypes: { id: number; name: string; isActive: boolean; qualifiedTechnicianIds: number[]; matchingBayIds: number[] }[];
    technicians: { id: number; name: string; isActive: boolean; shiftStart: string; skills: { skill: string; tier: string }[] }[];
    serviceBays: { id: number; name: string; bayType: string; isActive: boolean }[];
  };
  const setupOf = (id: number) => setup().call('GET', `/api/dealerships/${id}/setup`);

  it('lists every service, technician and bay of the dealership with the booking rules applied', () => {
    const response = setupOf(1);
    expect(response.status).toBe(200);
    const body = response.body as unknown as Setup;
    expect(body.dealership).toEqual({
      id: 1, name: 'District 1 Service Centre', timeZoneId: 'Asia/Ho_Chi_Minh', openTime: '08:00', closeTime: '17:00', isActive: true,
    });
    expect(body.serviceTypes.map((s) => [s.name, s.qualifiedTechnicianIds, s.matchingBayIds])).toEqual([
      ['Oil change', [1, 2, 3], [1]],
      ['Brake inspection', [1, 3], [2]], // Bay 4 (Lift) is inactive; Tuan (Master brakes) is inactive
      ['Tyre rotation', [1, 2], [2]],
      ['EV battery check', [2], [3]], // Lan is the only active EV technician
    ]);
    expect(body.serviceTypes[3]).toMatchObject({
      id: 4, durationMinutes: 60, requiredSkill: 'EV', minimumTier: 'Senior', requiredBayType: 'EV', isActive: true,
    });
    // Active first, then name; inactive Tuan last.
    expect(body.technicians.map((t) => [t.name, t.isActive])).toEqual([
      ['Lan', true], ['Minh', true], ['Nam', true], ['Tuan', false],
    ]);
    expect(body.technicians[0]).toEqual({
      id: 2, name: 'Lan', shiftStart: '08:00', shiftEnd: '17:00', isActive: true,
      skills: [{ skill: 'General', tier: 'Qualified' }, { skill: 'Tyres', tier: 'Qualified' }, { skill: 'EV', tier: 'Senior' }],
    });
    // Skills in enum order (General, Brakes, Tyres, EV, Diagnostics), not seed order.
    expect(body.technicians[3]!.skills.map((s) => s.skill)).toEqual(['General', 'Brakes', 'EV']);
    expect(body.serviceBays.map((b) => [b.name, b.bayType, b.isActive])).toEqual([
      ['Bay 1', 'Standard', true], ['Bay 2', 'Lift', true], ['Bay 3', 'EV', true], ['Bay 4', 'Lift', false],
    ]);
  });

  it('shows only the dealership\'s own rows, inactive services too, and still answers for an inactive dealership', () => {
    const thuDuc = setupOf(2).body as unknown as Setup;
    expect(thuDuc.serviceTypes.map((s) => [s.id, s.qualifiedTechnicianIds, s.matchingBayIds])).toEqual([[5, [4], [4]]]);
    expect(thuDuc.technicians.map((t) => t.name)).toEqual(['Hoa']);
    expect(thuDuc.serviceBays.map((b) => b.name)).toEqual(['Bay A']);

    const service = serviceTypes.find((s) => s.id === 3)!;
    const dealership = dealerships.find((d) => d.id === 1)!;
    service.isActive = false;
    dealership.isActive = false;
    try {
      const body = setupOf(1);
      expect(body.status).toBe(200);
      expect((body.body as unknown as Setup).dealership.isActive).toBe(false);
      expect((body.body as unknown as Setup).serviceTypes.find((s) => s.id === 3)).toMatchObject({ isActive: false, qualifiedTechnicianIds: [1, 2] });
    } finally {
      service.isActive = true;
      dealership.isActive = true;
    }
  });

  it('404 for an unknown dealership', () => {
    expect(setupOf(9).body).toMatchObject({ status: 404, code: 'NOT_FOUND', detail: 'Dealership 9 was not found.' });
    expect(setup().call('POST', '/api/dealerships/1/setup').status).toBe(405);
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
    expect((call('GET', '/api/customers/2/vehicles').body as { make: string }[]).map((v) => v.make)).toEqual(['VinFast']);
    expect((call('GET', '/api/customers/3/vehicles').body as { id: number }[]).map((v) => v.id)).toEqual([4, 5]);
    expect(call('GET', '/api/customers/7/vehicles').body).toMatchObject({ status: 404, detail: 'Customer 7 was not found.' });
    expect(call('GET', '/api/customers').body).toEqual([
      { id: 1, name: 'An Nguyen', email: 'an.nguyen@example.com', phone: '+84 900 000 001' },
      { id: 2, name: 'Binh Tran', email: 'binh.tran@example.com', phone: '+84 900 000 002' },
      { id: 3, name: 'Chi Le', email: 'chi.le@example.com', phone: '+84 900 000 003' },
    ]);
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

describe('guided tour helper', () => {
  it("another customer's booking takes the slot, stays out of the demo customer's list, and leads to 409 with suggestions", () => {
    const { backend, book, call } = setup();
    expect(backend.simulateOtherCustomerBooking(1, 4, Date.parse(at('11:00')))).toBe(true);
    expect(backend.simulateOtherCustomerBooking(1, 4, Date.parse(at('11:00')))).toBe(false); // Lan and the EV bay are taken
    const taken = book(2, 4, at('11:00'));
    expect(taken.status).toBe(409);
    expect(taken.body).toMatchObject({ code: 'NO_AVAILABILITY' });
    expect((taken.body.suggestedStartTimes as string[]).length).toBeGreaterThan(0);
    expect(call('GET', '/api/appointments?customerId=1').body).toEqual([]);
  });
});
