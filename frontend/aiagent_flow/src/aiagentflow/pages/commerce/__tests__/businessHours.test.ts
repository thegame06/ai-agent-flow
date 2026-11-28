import { it, expect, describe } from 'vitest';

import {
  copyDay,
  WEEKDAYS,
  emptyWeek,
  weekToApi,
  weekFromApi,
  describeWeek,
  closuresToApi,
  closuresFromApi,
  EMPTY_BRANCH_HOURS,
  validateBranchHours,
} from '../businessHours';

describe('branch hours', () => {
  it('round-trips the weekly hours through the API shape', () => {
    const api = [
      { day: 'monday', open: '08:00', close: '12:00' },
      { day: 'monday', open: '14:00', close: '18:00' },
      { day: 'saturday', open: '08:00', close: '12:00' },
    ];

    const week = weekFromApi(api);

    expect(week.monday).toHaveLength(2);
    expect(week.sunday).toEqual([]);
    expect(weekToApi(week)).toEqual(api);
  });

  it('copies Monday to the other weekdays without sharing objects', () => {
    const week = { ...emptyWeek(), monday: [{ open: '08:00', close: '17:00' }] };

    const copied = copyDay(week, 'monday', WEEKDAYS.filter((d) => d !== 'monday'));

    expect(copied.friday).toEqual([{ open: '08:00', close: '17:00' }]);
    expect(copied.friday[0]).not.toBe(copied.monday[0]);
    expect(copied.saturday).toEqual([]);
  });

  it('describes the week in one line', () => {
    expect(describeWeek(emptyWeek())).toBe('Sin horario');
    expect(describeWeek({ ...emptyWeek(), monday: [{ open: '08:00', close: '12:00' }] })).toBe('Lun 08:00-12:00');
  });

  it('sends a holiday closed all day without hours, and a special day with them', () => {
    const closures = closuresToApi([
      { date: '2026-12-08', reason: ' Purisima ', closedAllDay: true, intervals: [{ open: '08:00', close: '12:00' }] },
      { date: '2026-12-24', reason: '', closedAllDay: false, intervals: [{ open: '08:00', close: '11:00' }] },
    ]);

    expect(closures).toEqual([
      { date: '2026-12-08', reason: 'Purisima', intervals: [] },
      { date: '2026-12-24', reason: undefined, intervals: [{ open: '08:00', close: '11:00' }] },
    ]);
    expect(closuresFromApi(closures)[0].closedAllDay).toBe(true);
    expect(closuresFromApi(closures)[1].closedAllDay).toBe(false);
  });

  it.each([
    [{ monday: [{ open: '8', close: '12:00' }] }, 'HH:mm'],
    [{ monday: [{ open: '12:00', close: '08:00' }] }, 'despues de la apertura'],
    [{ monday: [{ open: '22:00', close: '02:00' }] }, 'medianoche'],
    [{ monday: [{ open: '08:00', close: '13:00' }, { open: '12:00', close: '18:00' }] }, 'enciman'],
  ])('rejects hours that cannot work', (days, reason) => {
    const draft = { ...EMPTY_BRANCH_HOURS, week: { ...emptyWeek(), ...days } };

    expect(validateBranchHours(draft)).toContain(reason);
  });

  it('accepts open until midnight and a valid week', () => {
    const draft = {
      ...EMPTY_BRANCH_HOURS,
      week: { ...emptyWeek(), friday: [{ open: '20:00', close: '24:00' }], saturday: [{ open: '00:00', close: '02:00' }] },
    };

    expect(validateBranchHours(draft)).toBeNull();
  });

  it('asks for a date and for special hours on special days', () => {
    expect(validateBranchHours({ ...EMPTY_BRANCH_HOURS, closures: [{ date: '', reason: '', closedAllDay: true, intervals: [] }] })).toContain('fecha');
    expect(
      validateBranchHours({ ...EMPTY_BRANCH_HOURS, closures: [{ date: '2026-12-24', reason: '', closedAllDay: false, intervals: [] }] })
    ).toContain('horario especial');
  });
});
