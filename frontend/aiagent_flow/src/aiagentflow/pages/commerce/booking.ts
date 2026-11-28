// How a catalog item is reserved. Anything can be: a doctor, a table, a room, a house, a machine.
// What changes is how time is measured (the mode), what the price is for and for how many people.

export type BookingMode = 'appointment' | 'hourly' | 'daily' | 'nightly';

export type PriceUnit = 'booking' | 'hour' | 'day' | 'night' | 'person';

export const BOOKING_MODES: Array<{ value: BookingMode; label: string; examples: string }> = [
  { value: 'appointment', label: 'Cita o turno de duracion fija', examples: 'Doctor, taller, mesa de restaurante' },
  { value: 'hourly', label: 'Por horas', examples: 'Maquinaria por hora, cancha, sala' },
  { value: 'daily', label: 'Por dias', examples: 'Alquiler de equipo o vehiculo' },
  { value: 'nightly', label: 'Por noches', examples: 'Habitacion de hotel, casa' },
];

export const PRICE_UNITS: Array<{ value: PriceUnit; label: string }> = [
  { value: 'booking', label: 'Por reserva' },
  { value: 'person', label: 'Por persona' },
  { value: 'hour', label: 'Por hora' },
  { value: 'day', label: 'Por dia' },
  { value: 'night', label: 'Por noche' },
];

export const DEFAULT_PRICE_UNIT: Record<BookingMode, PriceUnit> = {
  appointment: 'booking',
  hourly: 'hour',
  daily: 'day',
  nightly: 'night',
};

export type ApiBooking = {
  bookable: boolean;
  mode: BookingMode;
  durationMinutes: number;
  minMinutes: number;
  maxMinutes: number;
  stepMinutes: number;
  minUnits: number;
  maxUnits: number;
  startTime?: string | null;
  endTime?: string | null;
  bufferMinutes: number;
  priceUnit: PriceUnit;
  priceIsFrom: boolean;
  minPartySize: number;
  maxPartySize: number;
  resourceKinds: string[];
  instructions?: string | null;
  summary?: string;
};

// Numbers are kept as text while editing, so a field can be empty.
export type BookingDraft = {
  bookable: boolean;
  mode: BookingMode;
  durationMinutes: string;
  minHours: string;
  maxHours: string;
  minUnits: string;
  maxUnits: string;
  startTime: string;
  endTime: string;
  bufferMinutes: string;
  priceUnit: PriceUnit;
  priceIsFrom: boolean;
  minPartySize: string;
  maxPartySize: string;
  resourceKinds: string;
  instructions: string;
};

export const EMPTY_BOOKING: BookingDraft = {
  bookable: false,
  mode: 'appointment',
  durationMinutes: '',
  minHours: '',
  maxHours: '',
  minUnits: '1',
  maxUnits: '',
  startTime: '',
  endTime: '',
  bufferMinutes: '',
  priceUnit: 'booking',
  priceIsFrom: false,
  minPartySize: '',
  maxPartySize: '',
  resourceKinds: '',
  instructions: '',
};

/** Check-in/check-out or hand-over/return defaults when switching mode. */
export const DEFAULT_TIMES: Partial<Record<BookingMode, { start: string; end: string }>> = {
  daily: { start: '08:00', end: '17:00' },
  nightly: { start: '15:00', end: '12:00' },
};

const text = (value?: number | null) => (value ? String(value) : '');
const hours = (minutes?: number | null) => (minutes ? String(Math.round((minutes / 60) * 100) / 100) : '');
const num = (value: string) => Number(value || 0);

export function bookingFromApi(booking?: ApiBooking | null): BookingDraft {
  if (!booking) return { ...EMPTY_BOOKING };
  return {
    bookable: Boolean(booking.bookable),
    mode: booking.mode ?? 'appointment',
    durationMinutes: text(booking.durationMinutes),
    minHours: hours(booking.minMinutes),
    maxHours: hours(booking.maxMinutes),
    minUnits: text(booking.minUnits) || '1',
    maxUnits: text(booking.maxUnits),
    startTime: booking.startTime ?? '',
    endTime: booking.endTime ?? '',
    bufferMinutes: text(booking.bufferMinutes),
    priceUnit: booking.priceUnit ?? DEFAULT_PRICE_UNIT[booking.mode ?? 'appointment'],
    priceIsFrom: Boolean(booking.priceIsFrom),
    minPartySize: text(booking.minPartySize),
    maxPartySize: text(booking.maxPartySize),
    resourceKinds: (booking.resourceKinds ?? []).join(', '),
    instructions: booking.instructions ?? '',
  };
}

export function bookingToApi(draft: BookingDraft): Omit<ApiBooking, 'summary'> {
  const byRange = draft.mode === 'daily' || draft.mode === 'nightly';
  return {
    bookable: draft.bookable,
    mode: draft.mode,
    durationMinutes: draft.mode === 'appointment' ? num(draft.durationMinutes) : 0,
    minMinutes: draft.mode === 'hourly' ? Math.round(num(draft.minHours) * 60) : 0,
    maxMinutes: draft.mode === 'hourly' ? Math.round(num(draft.maxHours) * 60) : 0,
    stepMinutes: draft.mode === 'hourly' ? 30 : 0,
    minUnits: byRange ? Math.max(1, num(draft.minUnits)) : 0,
    maxUnits: byRange ? num(draft.maxUnits) : 0,
    startTime: byRange ? draft.startTime || null : null,
    endTime: byRange ? draft.endTime || null : null,
    bufferMinutes: num(draft.bufferMinutes),
    priceUnit: draft.priceUnit,
    priceIsFrom: draft.priceIsFrom,
    minPartySize: num(draft.minPartySize),
    maxPartySize: num(draft.maxPartySize),
    resourceKinds: draft.resourceKinds
      .split(',')
      .map((kind) => kind.trim().toLowerCase())
      .filter(Boolean),
    instructions: draft.instructions.trim() || null,
  };
}

/** Switching mode also switches the price unit and the default times, unless the user picked their own unit. */
export function changeMode(draft: BookingDraft, mode: BookingMode): BookingDraft {
  const keepUnit = draft.priceUnit !== DEFAULT_PRICE_UNIT[draft.mode];
  const times = DEFAULT_TIMES[mode];
  return {
    ...draft,
    mode,
    priceUnit: keepUnit ? draft.priceUnit : DEFAULT_PRICE_UNIT[mode],
    startTime: times ? draft.startTime || times.start : '',
    endTime: times ? draft.endTime || times.end : '',
  };
}

const CLOCK = /^([01]\d|2[0-3]):[0-5]\d$/;

/** Same rules as the API, checked before saving. */
export function validateBooking(draft: BookingDraft): string | null {
  const values = [draft.durationMinutes, draft.minHours, draft.maxHours, draft.minUnits, draft.maxUnits, draft.bufferMinutes, draft.minPartySize, draft.maxPartySize];
  if (values.some((value) => value !== '' && (Number.isNaN(Number(value)) || Number(value) < 0)))
    return 'Los numeros de la reserva deben ser cero o mayores.';
  if (num(draft.bufferMinutes) > 1440) return 'El tiempo bloqueado despues de cada reserva debe ser de 1440 minutos o menos.';
  if (num(draft.maxPartySize) > 0 && num(draft.minPartySize) > num(draft.maxPartySize))
    return 'El minimo de personas no puede ser mayor que el maximo.';

  if (draft.mode === 'appointment') {
    if (num(draft.durationMinutes) > 1440) return 'La duracion debe ser de 1440 minutos o menos.';
    if (draft.bookable && num(draft.durationMinutes) < 5) return 'Una cita necesita una duracion de al menos 5 minutos.';
  }
  if (draft.mode === 'hourly') {
    if (draft.bookable && num(draft.minHours) * 60 < 5) return 'Indica el minimo de horas por reserva.';
    if (num(draft.maxHours) > 0 && num(draft.maxHours) < num(draft.minHours)) return 'El maximo de horas no puede ser menor que el minimo.';
    if (num(draft.maxHours) > 336) return 'El maximo es de 336 horas (14 dias); para mas, usa "Por dias".';
  }
  if (draft.mode === 'daily' || draft.mode === 'nightly') {
    const unit = draft.mode === 'daily' ? 'dias' : 'noches';
    if (num(draft.maxUnits) > 365) return `El maximo es de 365 ${unit}.`;
    if (num(draft.maxUnits) > 0 && num(draft.maxUnits) < Math.max(1, num(draft.minUnits)))
      return `El maximo de ${unit} no puede ser menor que el minimo.`;
    if (draft.startTime && !CLOCK.test(draft.startTime)) return `La hora "${draft.startTime}" debe ser HH:mm.`;
    if (draft.endTime && !CLOCK.test(draft.endTime)) return `La hora "${draft.endTime}" debe ser HH:mm.`;
  }
  return null;
}
