// Campaign enums travel as numbers (the API has no string-enum converter); these maps give the
// numeric value its Spanish label so operators never see "WorkflowStart" or "Collections".

export const CAMPAIGN_STATUS = [
  { label: 'Borrador', color: 'default' },
  { label: 'Publicada', color: 'info' },
  { label: 'Activa', color: 'success' },
  { label: 'Pausada', color: 'warning' },
  { label: 'Terminada', color: 'default' },
  { label: 'Con error', color: 'error' },
  { label: 'Archivada', color: 'default' },
] as const;

export const CAMPAIGN_TYPE = ['Ventas', 'Cobranza', 'Recordatorio', 'Reactivación', 'Otra'] as const;
export const CampaignTypeValue = { Sales: 0, Collections: 1, Reminder: 2, Reactivation: 3, Custom: 4 } as const;

export const EXECUTION_MODE = { Workflow: 0, Direct: 1, Hybrid: 2 } as const;
export const TRIGGER_TYPE = { Schedule: 0, Manual: 1, EventAssisted: 2 } as const;
export const CHANNEL_ACTION = { Message: 0, Call: 1, WorkflowStart: 2 } as const;

export const SCHEDULE_TYPE = { Once: 0, Hourly: 1, Daily: 2, Weekly: 3, Cron: 4 } as const;
export const SCHEDULE_LABEL = ['Una vez', 'Cada hora', 'Diaria', 'Semanal', 'Personalizada'];

export const RUN_STATUS = [
  { label: 'Pendiente', color: 'default' },
  { label: 'En curso', color: 'info' },
  { label: 'Pausada', color: 'warning' },
  { label: 'Completada', color: 'success' },
  { label: 'Con errores', color: 'error' },
  { label: 'Cancelada', color: 'default' },
] as const;
export const RUN_TRIGGER = ['Programada', 'Manual', 'Reintento'];

export const CONTACT_STATUS = [
  { label: 'En cola', color: 'default' },
  { label: 'Enviado', color: 'info' },
  { label: 'Entregado', color: 'info' },
  { label: 'Leído', color: 'success' },
  { label: 'Falló', color: 'error' },
  { label: 'Conectado', color: 'success' },
  { label: 'En automatización', color: 'info' },
  { label: 'Omitido', color: 'default' },
] as const;

export const WEEKDAYS = [
  { value: 'Monday', label: 'Lunes' },
  { value: 'Tuesday', label: 'Martes' },
  { value: 'Wednesday', label: 'Miércoles' },
  { value: 'Thursday', label: 'Jueves' },
  { value: 'Friday', label: 'Viernes' },
  { value: 'Saturday', label: 'Sábado' },
  { value: 'Sunday', label: 'Domingo' },
];

export const OUTCOME_CATEGORY_COLOR: Record<string, 'success' | 'warning' | 'error' | 'default' | 'info'> = {
  positive: 'success',
  neutral: 'info',
  negative: 'error',
  unreachable: 'default',
};

export const OUTCOME_SOURCE_LABEL: Record<string, string> = {
  system: 'Sistema',
  reply: 'Respuesta del cliente',
  agent: 'Asistente',
  workflow: 'Automatización',
  human: 'Operador',
};

export type Campaign = {
  id: string;
  name: string;
  description: string;
  status: number;
  campaignType: number;
  executionMode: number;
  triggerType: number;
  channelAction: number;
  channel: string;
  goal: string;
  workflowDefinitionId?: string | null;
  messageDraft?: string | null;
  scheduleType: number;
  scheduleExpression: string;
  timezone: string;
  startAt: string;
  segmentId?: string | null;
  audienceFilterJson: string;
  enabled: boolean;
  lastRunAt?: string | null;
  nextRunAt?: string | null;
  createdAt: string;
  updatedAt: string;
};

export type AudienceContact = {
  partyId: string;
  channel: string;
  recipient: string;
  displayName: string;
  reason: string;
  outstandingAmount: number;
  variables: Record<string, string>;
};

export type AudiencePreview = {
  estimatedCount: number;
  contacts: AudienceContact[];
  warnings: string[];
};

export type OutcomeDefinition = { code: string; label: string; category: string; campaignTypes: number[] };

export type CampaignContact = {
  id: string;
  runId: string;
  partyId?: string | null;
  channel: string;
  recipient: string;
  displayName?: string | null;
  status: number;
  skipReason?: string | null;
  renderedMessage?: string | null;
  workflowExecutionId?: string | null;
  repliedAt?: string | null;
  lastReply?: string | null;
  replyCount: number;
  outcome?: string | null;
  outcomeSource?: string | null;
  outcomeAt?: string | null;
  outcomeNotes?: string | null;
  promiseAmount?: number | null;
  promiseDate?: string | null;
  createdAt: string;
};

export type CampaignResults = {
  contacts: number;
  sent: number;
  failed: number;
  skipped: number;
  replied: number;
  withOutcome: number;
  pending: number;
  replyRate: number;
  contactRate: number;
  promises: number;
  promisedAmount: number;
  outcomes: { code: string; label: string; category: string; count: number }[];
  contactsList: CampaignContact[];
};

export type CampaignRun = {
  id: string;
  status: number;
  triggeredBy: number;
  startedAt: string;
  completedAt?: string | null;
  countersJson: string;
  errorSummaryJson?: string | null;
};

/** Audience filter edited by the wizard; serialized to the campaign's audienceFilterJson. */
export type AudienceFilter = {
  source: 'collections' | 'commerce';
  stages: string[];
  minDaysPastDue?: number;
  maxDaysPastDue?: number;
  minOverdueBalance?: number;
  includeChargedOff?: boolean;
  customerIds?: string[];
  query?: string;
  kind?: string;
  minPurchaseCount?: number;
  minOutstandingAmount?: number;
  invoiceStatus?: string;
  excludeCampaignContactDays?: number;
};

export const parseFilter = (json?: string | null): AudienceFilter => {
  try {
    const raw = JSON.parse(json || '{}');
    return {
      ...raw,
      source: raw.source === 'collections' ? 'collections' : 'commerce',
      stages: Array.isArray(raw.stages) ? raw.stages : [],
      customerIds: Array.isArray(raw.customerIds) ? raw.customerIds : undefined,
    };
  } catch {
    return { source: 'commerce', stages: [] };
  }
};

export const serializeFilter = (filter: AudienceFilter): string =>
  JSON.stringify(
    Object.fromEntries(
      Object.entries(filter).filter(
        ([, v]) => v !== undefined && v !== '' && v !== null && !(Array.isArray(v) && v.length === 0) && v !== false
      )
    )
  );

/** Same rendering the API does: {{nombre}} / {{ variables.nombre }} filled from the contact. */
export const renderMessage = (template: string, variables?: Record<string, string>) =>
  (template || '').replace(/\{\{\s*([\w.]+)\s*\}\}/g, (match, key: string) => {
    const name = key.startsWith('variables.') ? key.slice('variables.'.length) : key;
    const value = variables?.[name];
    return value ? value : match;
  });

export const COLLECTIONS_VARIABLES = [
  { key: 'primer_nombre', label: 'Primer nombre' },
  { key: 'nombre', label: 'Nombre completo' },
  { key: 'total_vencido', label: 'Saldo vencido' },
  { key: 'saldo_total', label: 'Saldo total' },
  { key: 'dias_atraso', label: 'Días de atraso' },
  { key: 'num_prestamos', label: 'Núm. de préstamos' },
  { key: 'prestamos', label: 'Ids de préstamos' },
  { key: 'proximo_pago_fecha', label: 'Fecha próximo pago' },
  { key: 'proximo_pago_monto', label: 'Monto próximo pago' },
  { key: 'moneda', label: 'Moneda' },
];

export const COMMERCE_VARIABLES = [
  { key: 'primer_nombre', label: 'Primer nombre' },
  { key: 'nombre', label: 'Nombre completo' },
  { key: 'compras', label: 'Núm. de compras' },
  { key: 'total_comprado', label: 'Total comprado' },
  { key: 'saldo_pendiente', label: 'Saldo pendiente' },
];

export const DEFAULT_COLLECTIONS_MESSAGE =
  'Hola {{primer_nombre}}, te escribimos de parte de tu financiera. Tu saldo vencido es de ${{total_vencido}} {{moneda}} con {{dias_atraso}} días de atraso. ¿Cuándo podrías realizar tu pago? Respóndenos con la fecha y el monto.';

export const money = (value?: number | null, currency = 'MXN') =>
  value == null
    ? '—'
    : new Intl.NumberFormat('es-MX', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value);

export const dateTime = (value?: string | null) =>
  value ? new Date(value).toLocaleString('es-MX', { dateStyle: 'short', timeStyle: 'short' }) : '—';

export const day = (value?: string | null) =>
  value ? new Date(value).toLocaleDateString('es-MX', { timeZone: 'UTC' }) : '—';

export const pct = (value: number) => `${Math.round(value * 100)}%`;
