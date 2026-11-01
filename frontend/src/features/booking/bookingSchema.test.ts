import { describe, expect, it } from 'vitest';
import { createBookingSchema } from './bookingSchema';

const schema = createBookingSchema(() => '2030-01-15');
const valid = { vehicleId: '1', dealershipId: '1', serviceTypeId: '1', date: '2030-01-15', startTime: '2030-01-15T03:30:00.000Z' };

describe('booking schema', () => {
  it('accepts a complete booking for today or later', () => {
    expect(schema.safeParse(valid).success).toBe(true);
    expect(schema.safeParse({ ...valid, date: '2030-02-01' }).success).toBe(true);
  });

  it('rejects a date before today in the dealership zone', () => {
    const result = schema.safeParse({ ...valid, date: '2030-01-14' });
    expect(result.success).toBe(false);
    expect(result.error?.issues[0]).toMatchObject({ path: ['date'], message: 'Please choose today or a later date' });
  });

  it('rejects missing fields with friendly messages', () => {
    const result = schema.safeParse({ ...valid, vehicleId: '', startTime: undefined });
    expect(result.error?.issues.map((i) => i.message)).toEqual(['Please choose a vehicle', 'Please choose a start time']);
  });
});
