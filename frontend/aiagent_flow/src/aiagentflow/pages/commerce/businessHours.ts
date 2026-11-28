// Branch opening hours and service booking data: API shapes, the editor's state and the conversions.

export const DAYS = [
  { key: 'monday', label: 'Lunes' },
  { key: 'tuesday', label: 'Martes' },
  { key: 'wednesday', label: 'Miercoles' },
  { key: 'thursday', label: 'Jueves' },
  { key: 'friday', label: 'Viernes' },
  { key: 'saturday', label: 'Sabado' },
  { key: 'sunday', label: 'Domingo' },
] as const;

export type DayKey = (typeof DAYS)[number]['key'];

export const WEEKDAYS: DayKey[] = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday'];

// Time zones offered in the selector; any other IANA name can still be typed.
export const COMMON_TIMEZONES = [
  'America/Managua',
  'America/Tegucigalpa',
  'America/El_Salvador',
  'America/Guatemala',
  'America/Costa_Rica',
  'America/Panama',
  'America/Mexico_City',
  'America/Bogota',
  'America/Lima',
  'America/Santo_Domingo',
  'America/New_York',
  'Europe/Madrid',
];

export type TimeRange = { open: string; close: string };

export type ApiOpeningInterval = { day: string; open: string; close: string };

export type ApiClosure = { date: string; reason?: string | null; intervals?: TimeRange[] };

export type WeekHours = Record<DayKey, TimeRange[]>;

export type ClosureDraft = { date: string; reason: string; closedAllDay: boolean; intervals: TimeRange[] };

export type BranchHoursDraft = {
  timezone: string;
  week: WeekHours;
  closures: ClosureDraft[];
  mapsUrl: string;
  notes: string;
};

export const emptyWeek = (): WeekHours =>
  DAYS.reduce((week, day) => ({ ...week, [day.key]: [] }), {} as WeekHours);

export const EMPTY_BRANCH_HOURS: BranchHoursDraft = {
  timezone: '',
  week: emptyWeek(),
  closures: [],
  mapsUrl: '',
  notes: '',
};

export function weekFromApi(hours?: ApiOpeningInterval[] | null): WeekHours {
  const week = emptyWeek();
  (hours ?? []).forEach((interval) => {
    const day = interval.day?.toLowerCase() as DayKey;
    if (week[day]) week[day] = [...week[day], { open: interval.open, close: interval.close }];
  });
  return week;
}

export function weekToApi(week: WeekHours): ApiOpeningInterval[] {
  return DAYS.flatMap((day) =>
    week[day.key]
      .filter((range) => range.open && range.close)
      .map((range) => ({ day: day.key, open: range.open, close: range.close }))
  );
}

export function closuresFromApi(closures?: ApiClosure[] | null): ClosureDraft[] {
  return (closures ?? []).map((closure) => ({
    date: closure.date,
    reason: closure.reason ?? '',
    closedAllDay: !closure.intervals || closure.intervals.length === 0,
    intervals: closure.intervals ?? [],
  }));
}

export function closuresToApi(closures: ClosureDraft[]): ApiClosure[] {
  return closures
    .filter((closure) => closure.date)
    .map((closure) => ({
      date: closure.date,
      reason: closure.reason.trim() || undefined,
      intervals: closure.closedAllDay ? [] : closure.intervals.filter((range) => range.open && range.close),
    }));
}

/** Copies one day's hours to the given days (for "same hours Monday to Friday"). */
export function copyDay(week: WeekHours, from: DayKey, to: DayKey[]): WeekHours {
  const next = { ...week };
  to.forEach((day) => {
    next[day] = week[from].map((range) => ({ ...range }));
  });
  return next;
}

/** "08:00-12:00, 14:00-18:00" per day, or "Sin horario" when nothing is set. */
export function describeWeek(week: WeekHours): string {
  const parts = DAYS.filter((day) => week[day.key].length > 0).map(
    (day) => `${day.label.slice(0, 3)} ${week[day.key].map((r) => `${r.open}-${r.close}`).join(', ')}`
  );
  return parts.length > 0 ? parts.join(' · ') : 'Sin horario';
}

const OPEN_TIME = /^([01]\d|2[0-3]):[0-5]\d$/;
const CLOSE_TIME = /^(([01]\d|2[0-3]):[0-5]\d|24:00)$/;

/** Why a day's ranges cannot be saved (same rules as the API), or null. */
export function validateRanges(label: string, ranges: TimeRange[]): string | null {
  const sorted = [...ranges].sort((a, b) => a.open.localeCompare(b.open));
  for (let i = 0; i < sorted.length; i += 1) {
    const { open, close } = sorted[i];
    if (!open || !close) return `${label}: completa la hora de apertura y la de cierre, o quita ese horario.`;
    if (!OPEN_TIME.test(open)) return `${label}: la apertura "${open}" debe ser HH:mm, por ejemplo 08:00.`;
    if (!CLOSE_TIME.test(close)) return `${label}: el cierre "${close}" debe ser HH:mm, por ejemplo 18:00 (24:00 = medianoche).`;
    if (close <= open) return `${label}: el cierre debe ser despues de la apertura; si pasa la medianoche usa dos horarios (hasta 24:00 y desde 00:00).`;
    if (i > 0 && open < sorted[i - 1].close) return `${label}: los horarios se enciman.`;
  }
  return null;
}

/** Checks the whole editor before saving. */
export function validateBranchHours(draft: BranchHoursDraft): string | null {
  for (const day of DAYS) {
    const error = validateRanges(day.label, draft.week[day.key]);
    if (error) return error;
  }
  const dates = new Set<string>();
  for (const closure of draft.closures) {
    if (!closure.date) return 'Cada feriado o dia especial necesita una fecha.';
    if (dates.has(closure.date)) return `La fecha ${closure.date} esta repetida.`;
    dates.add(closure.date);
    if (!closure.closedAllDay) {
      if (closure.intervals.length === 0) return `${closure.date}: agrega el horario especial o marcalo como cerrado.`;
      const error = validateRanges(closure.date, closure.intervals);
      if (error) return error;
    }
  }
  return null;
}
