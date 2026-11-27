import { useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

// ----------------------------------------------------------------------

export type StageKey = 'Current' | 'Dpd1To30' | 'Dpd31To60' | 'Dpd61To90' | 'Dpd90Plus';

export const STAGE_KEYS: StageKey[] = ['Current', 'Dpd1To30', 'Dpd31To60', 'Dpd61To90', 'Dpd90Plus'];

export const STAGE_COLOR: Record<StageKey, 'success' | 'info' | 'warning' | 'error' | 'default'> = {
  Current: 'success',
  Dpd1To30: 'info',
  Dpd31To60: 'warning',
  Dpd61To90: 'error',
  Dpd90Plus: 'error',
};

export const STAGE_LABEL: Record<StageKey, string> = {
  Current: 'Al corriente',
  Dpd1To30: '1-30 días',
  Dpd31To60: '31-60 días',
  Dpd61To90: '61-90 días',
  Dpd90Plus: '90+ días',
};

/** Loan documents serialize enums as numbers; list endpoints send names. */
export const stageKey = (stage: StageKey | number | string): StageKey =>
  typeof stage === 'number' ? (STAGE_KEYS[stage] ?? 'Current') : ((stage as StageKey) ?? 'Current');

export const LOAN_STATUS_KEYS = ['Active', 'PaidOff', 'ChargedOff', 'Restructured'] as const;
export const LOAN_STATUS_LABEL: Record<string, string> = {
  Active: 'Activo',
  PaidOff: 'Liquidado',
  ChargedOff: 'Castigado',
  Restructured: 'Reestructurado',
};
export const loanStatusLabel = (status: number | string) =>
  LOAN_STATUS_LABEL[typeof status === 'number' ? LOAN_STATUS_KEYS[status] : status] ?? 'Activo';

export const PROMISE_STATUS = [
  { key: 'Pending', label: 'Pendiente', color: 'info' },
  { key: 'Kept', label: 'Cumplida', color: 'success' },
  { key: 'Broken', label: 'Incumplida', color: 'error' },
  { key: 'Cancelled', label: 'Cancelada', color: 'default' },
] as const;

export const promiseStatus = (status: number | string) =>
  (typeof status === 'number' ? PROMISE_STATUS[status] : PROMISE_STATUS.find((p) => p.key === status)) ??
  PROMISE_STATUS[0];

export const OUTCOME_SOURCE_LABEL: Record<string, string> = {
  system: 'Sistema',
  reply: 'Respuesta del cliente',
  agent: 'Asistente',
  workflow: 'Automatización',
  human: 'Operador',
};

export const money = (value: number | undefined | null, currency = 'MXN') =>
  value == null
    ? '—'
    : new Intl.NumberFormat('es-MX', { style: 'currency', currency: currency || 'MXN', maximumFractionDigits: 2 }).format(
        value
      );

/** Dates without time (cut-offs, due dates) are stored at UTC midnight. */
export const day = (value?: string | null) =>
  value ? new Date(value).toLocaleDateString('es-MX', { timeZone: 'UTC' }) : '—';

export const dateTime = (value?: string | null) =>
  value ? new Date(value).toLocaleString('es-MX', { dateStyle: 'short', timeStyle: 'short' }) : '—';

export const todayIso = () => new Date().toISOString().slice(0, 10);

export function useDebounced<T>(value: T, delay = 350): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(id);
  }, [value, delay]);
  return debounced;
}

// ----------------------------------------------------------------------

export type CollectionPromise = {
  id: string;
  partyId: string;
  customerName?: string;
  loanIds: string[];
  amount?: number | null;
  promisedDate: string;
  status: number | string;
  overdueAtCreation: number;
  overdueAtResolution?: number | null;
  source: string;
  campaignId?: string | null;
  notes?: string | null;
  createdAt: string;
  resolvedAt?: string | null;
};

export type ContactHistoryItem = {
  id: string;
  campaignId: string;
  campaignName: string;
  channel: string;
  recipient: string;
  status: string;
  renderedMessage?: string | null;
  lastReply?: string | null;
  repliedAt?: string | null;
  outcome?: string | null;
  outcomeLabel?: string | null;
  outcomeSource?: string | null;
  outcomeNotes?: string | null;
  promiseAmount?: number | null;
  promiseDate?: string | null;
  skipReason?: string | null;
  createdAt: string;
};

export function Kpi({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <Card variant="outlined">
      <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
        <Typography variant="caption" color="text.secondary">
          {label}
        </Typography>
        <Typography variant="h5">{value}</Typography>
        {hint && (
          <Typography variant="caption" color="text.secondary">
            {hint}
          </Typography>
        )}
      </CardContent>
    </Card>
  );
}

export function Field({ label, value }: { label: string; value: string }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="body2">{value}</Typography>
    </Box>
  );
}

export function SectionTitle({ children, action }: { children: React.ReactNode; action?: React.ReactNode }) {
  return (
    <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mt: 3, mb: 1 }}>
      <Typography variant="overline" color="text.secondary">
        {children}
      </Typography>
      {action}
    </Stack>
  );
}

export function PromiseList({ promises, currency }: { promises: CollectionPromise[]; currency?: string }) {
  if (promises.length === 0)
    return (
      <Typography variant="body2" color="text.secondary">
        Sin promesas de pago.
      </Typography>
    );
  return (
    <Stack spacing={1}>
      {promises.map((p) => {
        const st = promiseStatus(p.status);
        return (
          <Card key={p.id} variant="outlined" sx={{ px: 1.5, py: 1 }}>
            <Stack direction="row" justifyContent="space-between" alignItems="center">
              <Typography variant="subtitle2">
                {p.amount ? money(p.amount, currency) : 'Monto no indicado'} · {day(p.promisedDate)}
              </Typography>
              <Chip size="small" label={st.label} color={st.color} variant="soft" />
            </Stack>
            <Typography variant="caption" color="text.secondary">
              {OUTCOME_SOURCE_LABEL[p.source] ?? p.source} · registrada {dateTime(p.createdAt)} · vencido al prometer{' '}
              {money(p.overdueAtCreation, currency)}
              {p.overdueAtResolution != null ? ` · al resolver ${money(p.overdueAtResolution, currency)}` : ''}
            </Typography>
            {p.notes && (
              <Typography variant="caption" component="div" color="text.secondary">
                {p.notes}
              </Typography>
            )}
          </Card>
        );
      })}
    </Stack>
  );
}

export function ContactHistory({ contacts }: { contacts: ContactHistoryItem[] }) {
  if (contacts.length === 0)
    return (
      <Typography variant="body2" color="text.secondary">
        Ninguna campaña ha contactado a este cliente.
      </Typography>
    );
  return (
    <Stack spacing={1}>
      {contacts.map((c) => (
        <Card key={c.id} variant="outlined" sx={{ px: 1.5, py: 1 }}>
          <Stack direction="row" justifyContent="space-between" alignItems="center" spacing={1}>
            <Typography variant="subtitle2" noWrap>
              {c.campaignName}
            </Typography>
            <Chip
              size="small"
              variant="soft"
              color={c.outcome ? 'primary' : 'default'}
              label={c.outcomeLabel ?? 'Sin resultado aún'}
            />
          </Stack>
          <Typography variant="caption" color="text.secondary">
            {dateTime(c.createdAt)} · {c.channel} · {c.recipient}
            {c.outcomeSource ? ` · resultado por ${OUTCOME_SOURCE_LABEL[c.outcomeSource] ?? c.outcomeSource}` : ''}
          </Typography>
          {c.renderedMessage && (
            <Typography variant="body2" sx={{ mt: 0.5, color: 'text.secondary', fontStyle: 'italic' }}>
              Enviado: “{c.renderedMessage}”
            </Typography>
          )}
          {c.lastReply && (
            <Typography variant="body2" sx={{ mt: 0.5 }}>
              Respondió: “{c.lastReply}”
            </Typography>
          )}
          {c.promiseAmount != null || c.promiseDate ? (
            <Typography variant="caption" color="success.main">
              Promesa: {c.promiseAmount != null ? money(c.promiseAmount) : 'monto no indicado'} para el {day(c.promiseDate)}
            </Typography>
          ) : null}
          {c.skipReason && (
            <Typography variant="caption" component="div" color="error.main">
              {c.skipReason}
            </Typography>
          )}
        </Card>
      ))}
    </Stack>
  );
}

/** Tiny inline chart of a series (e.g. overdue balance per cut-off), oldest first. */
export function Sparkline({ values, height = 48 }: { values: number[]; height?: number }) {
  if (values.length < 2) return null;
  const width = 240;
  const max = Math.max(...values);
  const min = Math.min(...values);
  const span = max - min || 1;
  const points = values
    .map((v, i) => `${(i / (values.length - 1)) * width},${height - ((v - min) / span) * (height - 6) - 3}`)
    .join(' ');
  return (
    <Box component="svg" viewBox={`0 0 ${width} ${height}`} sx={{ width: '100%', height, color: 'warning.main' }}>
      <polyline points={points} fill="none" stroke="currentColor" strokeWidth={2} />
    </Box>
  );
}
