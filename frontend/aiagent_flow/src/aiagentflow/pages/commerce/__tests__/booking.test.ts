import { it, expect, describe } from 'vitest';

import { changeMode, bookingToApi, EMPTY_BOOKING, bookingFromApi, validateBooking } from '../booking';

describe('booking settings of a catalog item', () => {
  it('sends a doctor appointment with its duration only', () => {
    const api = bookingToApi({
      ...EMPTY_BOOKING,
      bookable: true,
      durationMinutes: '45',
      minHours: '3',
      bufferMinutes: '10',
      resourceKinds: ' Dentist , chair,, ',
      instructions: ' Venir sin comer ',
    });

    expect(api).toMatchObject({
      mode: 'appointment',
      durationMinutes: 45,
      minMinutes: 0,
      bufferMinutes: 10,
      priceUnit: 'booking',
      resourceKinds: ['dentist', 'chair'],
      instructions: 'Venir sin comer',
    });
  });

  it('sends a restaurant table with how many people fit', () => {
    const api = bookingToApi({ ...EMPTY_BOOKING, bookable: true, durationMinutes: '120', minPartySize: '1', maxPartySize: '6' });

    expect(api).toMatchObject({ durationMinutes: 120, minPartySize: 1, maxPartySize: 6 });
  });

  it('sends machinery by the hour in minutes', () => {
    const draft = changeMode({ ...EMPTY_BOOKING, bookable: true }, 'hourly');

    const api = bookingToApi({ ...draft, minHours: '2', maxHours: '10.5' });

    expect(api).toMatchObject({ mode: 'hourly', minMinutes: 120, maxMinutes: 630, stepMinutes: 30, priceUnit: 'hour', durationMinutes: 0 });
  });

  it('switching to nights sets check-in, check-out and the price per night', () => {
    const draft = changeMode({ ...EMPTY_BOOKING, bookable: true }, 'nightly');

    expect(draft).toMatchObject({ priceUnit: 'night', startTime: '15:00', endTime: '12:00' });
    expect(bookingToApi({ ...draft, minUnits: '2', maxPartySize: '4' })).toMatchObject({
      mode: 'nightly',
      minUnits: 2,
      maxUnits: 0,
      startTime: '15:00',
      endTime: '12:00',
      maxPartySize: 4,
    });
  });

  it('keeps a price unit the user chose when switching mode', () => {
    const perPerson = { ...EMPTY_BOOKING, priceUnit: 'person' as const };

    expect(changeMode(perPerson, 'nightly').priceUnit).toBe('person');
    expect(changeMode(changeMode(EMPTY_BOOKING, 'daily'), 'appointment')).toMatchObject({ priceUnit: 'booking', startTime: '', endTime: '' });
  });

  it('reads what the API returns back into the editor', () => {
    const draft = bookingFromApi({
      bookable: true,
      mode: 'hourly',
      durationMinutes: 0,
      minMinutes: 90,
      maxMinutes: 480,
      stepMinutes: 30,
      minUnits: 0,
      maxUnits: 0,
      bufferMinutes: 30,
      priceUnit: 'hour',
      priceIsFrom: true,
      minPartySize: 0,
      maxPartySize: 0,
      resourceKinds: ['excavator'],
    });

    expect(draft).toMatchObject({ minHours: '1.5', maxHours: '8', bufferMinutes: '30', resourceKinds: 'excavator', priceIsFrom: true });
    expect(bookingFromApi(null)).toEqual(EMPTY_BOOKING);
  });

  it.each([
    [{ bookable: true, durationMinutes: '' }, 'al menos 5'],
    [{ bookable: true, mode: 'hourly' as const, minHours: '' }, 'minimo de horas'],
    [{ mode: 'hourly' as const, minHours: '4', maxHours: '2' }, 'menor que el minimo'],
    [{ mode: 'nightly' as const, minUnits: '3', maxUnits: '2' }, 'noches no puede ser menor'],
    [{ mode: 'daily' as const, startTime: '8am' }, 'HH:mm'],
    [{ minPartySize: '8', maxPartySize: '4' }, 'personas'],
    [{ bufferMinutes: '-5' }, 'cero o mayores'],
  ])('rejects settings that cannot work', (patch, reason) => {
    expect(validateBooking({ ...EMPTY_BOOKING, ...patch })).toContain(reason);
  });

  it('lets an item that is not reserved be saved without booking data', () => {
    expect(validateBooking(EMPTY_BOOKING)).toBeNull();
  });
});
