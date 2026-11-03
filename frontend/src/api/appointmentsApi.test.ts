import { afterEach, describe, expect, it, vi } from 'vitest';
import { appointmentDto, assignmentPreviewDto, availabilityDaysDto, availabilityDto, customers } from '../test/fixtures';
import {
  bookAppointment,
  getAssignmentPreview,
  getAvailability,
  getAvailabilityDays,
  getCustomers,
  toAssignmentPreview,
  toAvailability,
} from './appointmentsApi';
import { ApiError } from './types';

function jsonResponse(status: number, body: unknown, contentType = 'application/json') {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } });
}

const request = { customerId: 1, vehicleId: 1, dealershipId: 1, serviceTypeId: 1, startTime: '2030-01-15T10:30:00+07:00' };

describe('appointmentsApi + apiClient', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('POSTs the booking with the Idempotency-Key header and adapts the response', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(201, appointmentDto()));
    vi.stubGlobal('fetch', fetchMock);

    const appointment = await bookAppointment(request, 'key-123');

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('http://localhost:5080/api/appointments');
    expect(init.method).toBe('POST');
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe('key-123');
    expect(JSON.parse(init.body as string)).toEqual(request);
    expect(appointment.start).toEqual(new Date('2030-01-15T03:30:00Z'));
    expect(appointment.vehicle.displayName).toBe('Toyota Vios 2021');
    expect(appointment.timeZoneId).toBe('Asia/Ho_Chi_Minh');
  });

  it('turns ProblemDetails into a typed ApiError', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(
          409,
          {
            type: 'about:blank',
            title: 'No availability',
            status: 409,
            detail: 'No technician and bay are free.',
            code: 'NO_AVAILABILITY',
            traceId: 't-1',
            suggestedStartTimes: ['2030-01-15T04:00:00Z'],
          },
          'application/problem+json',
        ),
      ),
    );

    const error = await bookAppointment(request, 'k').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({
      status: 409,
      code: 'NO_AVAILABILITY',
      title: 'No availability',
      detail: 'No technician and bay are free.',
      suggestedStartTimes: ['2030-01-15T04:00:00Z'],
      traceId: 't-1',
    });
  });

  it('keeps field errors from a 400 and falls back to UNKNOWN for a body without a code', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(400, { title: 'Bad', status: 400, code: 'INVALID_REQUEST', errors: { startTime: ['Must be on the 15-minute grid.'] } }))
        .mockResolvedValueOnce(new Response('oops', { status: 502 })),
    );

    await expect(bookAppointment(request, 'k')).rejects.toMatchObject({
      code: 'INVALID_REQUEST',
      errors: { startTime: ['Must be on the 15-minute grid.'] },
    });
    await expect(bookAppointment(request, 'k')).rejects.toMatchObject({ status: 502, code: 'UNKNOWN' });
  });

  it('reports a network failure as NETWORK_ERROR with status 0', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));
    await expect(bookAppointment(request, 'k')).rejects.toMatchObject({ status: 0, code: 'NETWORK_ERROR' });
  });

  it('requests availability for the local date and converts slots to Date objects', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, availabilityDto));
    vi.stubGlobal('fetch', fetchMock);

    const availability = await getAvailability({ dealershipId: 1, serviceTypeId: 1, date: '2030-01-15' });

    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      'http://localhost:5080/api/dealerships/1/availability?serviceTypeId=1&date=2030-01-15',
    );
    expect(availability.slots[0]).toEqual({
      key: '2030-01-15T03:00:00.000Z',
      start: new Date('2030-01-15T03:00:00Z'),
      end: new Date('2030-01-15T04:00:00Z'),
    });
  });

  it('sends vehicleId when given and keeps the status of every time', async () => {
    const dto = {
      ...availabilityDto,
      times: [
        { startUtc: '2030-01-15T02:00:00Z', endUtc: '2030-01-15T03:00:00Z', status: 'VehicleBooked' as const },
        { startUtc: '2030-01-15T03:00:00Z', endUtc: '2030-01-15T04:00:00Z', status: 'Available' as const },
      ],
    };
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, dto));
    vi.stubGlobal('fetch', fetchMock);

    const availability = await getAvailability({ dealershipId: 1, serviceTypeId: 1, date: '2030-01-15', vehicleId: 2 });

    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      'http://localhost:5080/api/dealerships/1/availability?serviceTypeId=1&date=2030-01-15&vehicleId=2',
    );
    expect(availability.times.map((t) => [t.key, t.status])).toEqual([
      ['2030-01-15T02:00:00.000Z', 'VehicleBooked'],
      ['2030-01-15T03:00:00.000Z', 'Available'],
    ]);
  });

  it('treats an unknown time status as not bookable, and an API without times as only free starts', () => {
    const odd = toAvailability({
      ...availabilityDto,
      times: [{ startUtc: '2030-01-15T03:00:00Z', endUtc: '2030-01-15T04:00:00Z', status: 'Held' as never }],
    });
    expect(odd.times[0]?.status).toBe('Booked');
    const old = toAvailability({ ...availabilityDto, times: undefined });
    expect(old.times.map((t) => t.status)).toEqual(['Available', 'Available', 'Available']);
  });

  it('requests the days of a range', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, availabilityDaysDto('2030-01-09', '2030-01-11')));
    vi.stubGlobal('fetch', fetchMock);

    const result = await getAvailabilityDays({ dealershipId: 1, serviceTypeId: 4, from: '2030-01-09', to: '2030-01-11', vehicleId: null });

    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      'http://localhost:5080/api/dealerships/1/availability/days?serviceTypeId=4&from=2030-01-09&to=2030-01-11',
    );
    expect(result.days).toEqual([
      { date: '2030-01-09', availableCount: 0, status: 'Past' },
      { date: '2030-01-10', availableCount: 3, status: 'Available' },
      { date: '2030-01-11', availableCount: 3, status: 'Available' },
    ]);
  });

  it('gets the customers', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, customers));
    vi.stubGlobal('fetch', fetchMock);
    expect((await getCustomers()).map((c) => c.name)).toEqual(['An Nguyen', 'Binh Tran', 'Chi Le']);
    expect(fetchMock.mock.calls[0]?.[0]).toBe('http://localhost:5080/api/customers');
  });

  it('asks for the assignment preview with the offset start time (encoded) and the car', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, assignmentPreviewDto()));
    vi.stubGlobal('fetch', fetchMock);

    const preview = await getAssignmentPreview({ dealershipId: 1, serviceTypeId: 1, startTime: '2030-01-15T10:30:00+07:00', vehicleId: 2 });

    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      'http://localhost:5080/api/dealerships/1/assignment-preview?serviceTypeId=1&startTime=2030-01-15T10%3A30%3A00%2B07%3A00&vehicleId=2',
    );
    expect(preview.start).toEqual(new Date('2030-01-15T03:30:00Z'));
    expect(preview.technician).toEqual({ id: 3, name: 'Minh' });
  });

  it('never promises a pick it does not have, and treats unknown states as busy', () => {
    const noPick = toAssignmentPreview(assignmentPreviewDto({ technician: null }));
    expect(noPick).toMatchObject({ status: 'NoAvailability', technician: null, serviceBay: null });
    const odd = toAssignmentPreview(
      assignmentPreviewDto({ status: 'Held' as never, technicians: [{ id: 1, name: 'Nam', state: 'OnBreak' as never }] }),
    );
    expect(odd.status).toBe('NoAvailability');
    expect(odd.technician).toBeNull();
    expect(odd.technicians[0]?.state).toBe('Busy');
  });
});
