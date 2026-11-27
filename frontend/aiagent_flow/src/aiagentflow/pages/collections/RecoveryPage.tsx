import { useNavigate } from 'react-router';
import { Helmet } from 'react-helmet-async';
import { useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Tooltip from '@mui/material/Tooltip';
import TableRow from '@mui/material/TableRow';
import { useTheme } from '@mui/material/styles';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';
import ToggleButton from '@mui/material/ToggleButton';
import LinearProgress from '@mui/material/LinearProgress';
import TableContainer from '@mui/material/TableContainer';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';

import { paths } from 'src/routes/paths';

import axios from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { day, Kpi, money, todayIso, dateTime } from './shared';

// ----------------------------------------------------------------------

type Group = {
  key: string;
  label: string;
  contacts: number;
  reached: number;
  replied: number;
  promised: number;
  customersPaid: number;
  managedOverdue: number;
  recovered: number;
  recoveryRate: number;
};

type RecoveryReport = {
  from: string;
  to: string;
  currency: string;
  attributionWindowDays: number;
  totals: {
    recovered: number;
    attributed: number;
    unattributed: number;
    managedOverdue: number;
    recoveryRate: number;
    customersPaid: number;
    attributedCustomers: number;
    events: number;
    avgDaysToPay?: number | null;
  };
  funnel: { contacts: number; reached: number; replied: number; promised: number; paid: number };
  promises: {
    created: number;
    promisedAmount: number;
    kept: number;
    broken: number;
    pending: number;
    cancelled: number;
    keptRate: number;
    recoveredWithPromise: number;
  };
  resolution: { key: string; label: string; contacts: number }[];
  byCampaign: Group[];
  byStage: Group[];
  daily: { date: string; attributed: number; unattributed: number; promises: number }[];
  recent: {
    partyId: string;
    customer?: string | null;
    loanExternalId?: string | null;
    amount: number;
    at: string;
    kind: 'payment' | 'cutoff';
    campaignName?: string | null;
    withPromise: boolean;
  }[];
};

const PRESETS = [
  { key: '7', label: '7 días', days: 7 },
  { key: '30', label: '30 días', days: 30 },
  { key: '90', label: '90 días', days: 90 },
];

const shiftDays = (iso: string, days: number) => {
  const d = new Date(`${iso}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
};

const pct = (value: number) => `${(value * 100).toLocaleString('es-MX', { maximumFractionDigits: 1 })}%`;

// ----------------------------------------------------------------------

export default function RecoveryPage() {
  const tenantId = useTenantId();
  const navigate = useNavigate();
  const base = `/api/v1/tenants/${tenantId}/collections`;

  const [preset, setPreset] = useState<string>('30');
  const [to, setTo] = useState(todayIso());
  const [from, setFrom] = useState(shiftDays(todayIso(), -29));
  const [report, setReport] = useState<RecoveryReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    axios
      .get<RecoveryReport>(`${base}/recovery`, { params: { from, to } })
      .then((res) => setReport(res.data))
      .catch((err) => setError(err?.response?.data?.message ?? 'No se pudo cargar el tablero.'))
      .finally(() => setLoading(false));
  }, [base, from, to]);

  const applyPreset = (key: string | null) => {
    const p = PRESETS.find((x) => x.key === key);
    if (!p) return;
    const end = todayIso();
    setPreset(p.key);
    setTo(end);
    setFrom(shiftDays(end, -(p.days - 1)));
  };

  const currency = report?.currency ?? 'MXN';
  const t = report?.totals;

  return (
    <>
      <Helmet>
        <title>Recuperación de cobranza</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} sx={{ mb: 3 }}>
          <Box>
            <Typography variant="h4">Recuperación</Typography>
            <Typography variant="body2" color="text.secondary">
              Cuánto dinero vencido regresó y cuánto llegó después de que una campaña contactó al cliente.
            </Typography>
          </Box>
          <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
            <ToggleButtonGroup size="small" exclusive value={preset} onChange={(_, v) => applyPreset(v)}>
              {PRESETS.map((p) => (
                <ToggleButton key={p.key} value={p.key}>
                  {p.label}
                </ToggleButton>
              ))}
            </ToggleButtonGroup>
            <TextField
              size="small"
              type="date"
              label="Desde"
              value={from}
              onChange={(e) => {
                setPreset('');
                setFrom(e.target.value);
              }}
              InputLabelProps={{ shrink: true }}
            />
            <TextField
              size="small"
              type="date"
              label="Hasta"
              value={to}
              onChange={(e) => {
                setPreset('');
                setTo(e.target.value);
              }}
              InputLabelProps={{ shrink: true }}
            />
            <Button variant="outlined" startIcon={<Iconify icon="mdi:briefcase-outline" />} onClick={() => navigate(paths.dashboard.collections)}>
              Cartera
            </Button>
          </Stack>
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2 }} />}
        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        {report && t && (
          <>
            <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, mb: 3 }}>
              <Kpi
                label="Recuperado por campañas"
                value={money(t.attributed, currency)}
                hint={`${t.attributedCustomers} clientes · ${money(t.recovered, currency)} recuperado en total`}
              />
              <Kpi
                label="Tasa de recuperación"
                value={pct(t.recoveryRate)}
                hint={`de ${money(t.managedOverdue, currency)} vencido gestionado`}
              />
              <Kpi
                label="Promesas cumplidas"
                value={report.promises.kept + report.promises.broken > 0 ? pct(report.promises.keptRate) : '—'}
                hint={`${report.promises.kept} cumplidas · ${report.promises.broken} incumplidas · ${report.promises.pending} abiertas`}
              />
              <Kpi
                label="Días de contacto a pago"
                value={t.avgDaysToPay != null ? `${t.avgDaysToPay.toLocaleString('es-MX')} días` : '—'}
                hint="promedio de los pagos atribuidos"
              />
            </Box>

            {t.events === 0 && (
              <Alert severity="info" sx={{ mb: 3 }}>
                Aún no hay pagos en este periodo. Se cuentan los pagos registrados (API o desde la ficha) y la baja del
                saldo vencido entre dos cortes de cartera. Un pago se atribuye a la última campaña que contactó al cliente
                en los {report.attributionWindowDays} días anteriores.
              </Alert>
            )}

            <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', lg: '2fr 1fr' }, mb: 3 }}>
              <Card>
                <CardContent>
                  <Typography variant="subtitle1">Recuperado por día</Typography>
                  <Typography variant="caption" color="text.secondary">
                    Atribuido: el cliente fue contactado por una campaña en los {report.attributionWindowDays} días previos al
                    pago.
                  </Typography>
                  <DailyChart days={report.daily} currency={currency} />
                </CardContent>
              </Card>
              <Card>
                <CardContent>
                  <Typography variant="subtitle1">Embudo de contactos</Typography>
                  <Typography variant="caption" color="text.secondary">
                    Contactos de campañas de cobranza creados en el periodo
                  </Typography>
                  <Funnel funnel={report.funnel} />
                </CardContent>
              </Card>
            </Box>

            <Card sx={{ mb: 3 }}>
              <CardContent sx={{ pb: 0 }}>
                <Typography variant="subtitle1">Por campaña</Typography>
              </CardContent>
              <GroupTable rows={report.byCampaign} currency={currency} firstColumn="Campaña" />
            </Card>

            <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', lg: '2fr 1fr' }, mb: 3 }}>
              <Card>
                <CardContent sx={{ pb: 0 }}>
                  <Typography variant="subtitle1">Por etapa de atraso</Typography>
                  <Typography variant="caption" color="text.secondary">
                    Etapa del cliente cuando se le contactó
                  </Typography>
                </CardContent>
                <GroupTable rows={report.byStage} currency={currency} firstColumn="Etapa" compact />
              </Card>
              <Card>
                <CardContent>
                  <Typography variant="subtitle1">Quién resolvió</Typography>
                  <Typography variant="caption" color="text.secondary">
                    Contactos alcanzados según quién registró el resultado
                  </Typography>
                  <Resolution items={report.resolution} />
                  <Box sx={{ mt: 3 }}>
                    <Typography variant="subtitle1">Promesas del periodo</Typography>
                    <Stack spacing={0.5} sx={{ mt: 1 }}>
                      <Row label="Registradas" value={`${report.promises.created} · ${money(report.promises.promisedAmount, currency)}`} />
                      <Row label="Pagado con promesa vigente" value={money(report.promises.recoveredWithPromise, currency)} />
                      {report.promises.cancelled > 0 && (
                        <Row label="Reemplazadas o canceladas" value={String(report.promises.cancelled)} />
                      )}
                    </Stack>
                  </Box>
                </CardContent>
              </Card>
            </Box>

            <Card>
              <CardContent sx={{ pb: 0 }}>
                <Typography variant="subtitle1">Últimos pagos</Typography>
              </CardContent>
              <RecentTable items={report.recent} currency={currency} />
            </Card>
          </>
        )}
      </DashboardContent>
    </>
  );
}

// ----------------------------------------------------------------------

function Row({ label, value }: { label: string; value: string }) {
  return (
    <Stack direction="row" justifyContent="space-between">
      <Typography variant="body2" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="body2">{value}</Typography>
    </Stack>
  );
}

/** Stacked daily bars: attributed (primary) on the baseline, the rest (neutral) on top. One axis, hover per day. */
function DailyChart({ days, currency }: { days: RecoveryReport['daily']; currency: string }) {
  const theme = useTheme();
  const [hover, setHover] = useState<number | null>(null);
  const height = 180;
  const width = 640;
  const gap = days.length > 60 ? 1 : 2;
  const barWidth = Math.max(1, width / days.length - gap);
  const max = Math.max(1, ...days.map((d) => d.attributed + d.unattributed));
  const y = (v: number) => (v / max) * (height - 8);
  const attributedColor = theme.palette.primary.main;
  const otherColor = theme.palette.grey[400];
  const hovered = hover == null ? null : days[hover];
  const total = days.reduce((s, d) => s + d.attributed + d.unattributed, 0);

  return (
    <Box sx={{ mt: 2 }}>
      <Stack direction="row" spacing={2} sx={{ mb: 1 }}>
        <Legend color={attributedColor} label="Atribuido a campañas" />
        <Legend color={otherColor} label="Sin campaña previa" />
      </Stack>
      <Box sx={{ position: 'relative' }}>
        <Box
          component="svg"
          viewBox={`0 0 ${width} ${height}`}
          preserveAspectRatio="none"
          sx={{ width: '100%', height, display: 'block' }}
          onMouseLeave={() => setHover(null)}
          role="img"
          aria-label={`Recuperado por día, total ${money(total, currency)}`}
        >
          <line x1={0} x2={width} y1={height - 0.5} y2={height - 0.5} stroke={theme.palette.divider} />
          {days.map((d, i) => {
            const x = i * (barWidth + gap);
            const a = y(d.attributed);
            const u = y(d.unattributed);
            return (
              <g key={d.date} onMouseEnter={() => setHover(i)}>
                <rect x={x} y={0} width={barWidth + gap} height={height} fill="transparent" />
                {a > 0 && <rect x={x} y={height - a} width={barWidth} height={a} rx={1.5} fill={attributedColor} opacity={hover == null || hover === i ? 1 : 0.5} />}
                {u > 0 && (
                  <rect
                    x={x}
                    y={height - a - u - (a > 0 ? 2 : 0)}
                    width={barWidth}
                    height={u}
                    rx={1.5}
                    fill={otherColor}
                    opacity={hover == null || hover === i ? 1 : 0.5}
                  />
                )}
              </g>
            );
          })}
        </Box>
        {total === 0 && (
          <Typography
            variant="body2"
            color="text.secondary"
            sx={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center' }}
          >
            Sin pagos en el periodo
          </Typography>
        )}
      </Box>
      <Stack direction="row" justifyContent="space-between" sx={{ mt: 0.5 }}>
        <Typography variant="caption" color="text.secondary">
          {days[0] ? new Date(days[0].date).toLocaleDateString('es-MX', { timeZone: 'UTC', day: 'numeric', month: 'short' }) : ''}
        </Typography>
        <Typography variant="caption" color="text.secondary" sx={{ minHeight: 20 }}>
          {hovered
            ? `${new Date(hovered.date).toLocaleDateString('es-MX', { timeZone: 'UTC', day: 'numeric', month: 'short' })}: ${money(
                hovered.attributed,
                currency
              )} atribuido · ${money(hovered.unattributed, currency)} sin campaña · ${hovered.promises} promesas`
            : `Máximo por día: ${money(max === 1 && total === 0 ? 0 : max, currency)}`}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          {days.length ? new Date(days[days.length - 1].date).toLocaleDateString('es-MX', { timeZone: 'UTC', day: 'numeric', month: 'short' }) : ''}
        </Typography>
      </Stack>
    </Box>
  );
}

function Legend({ color, label }: { color: string; label: string }) {
  return (
    <Stack direction="row" spacing={0.75} alignItems="center">
      <Box sx={{ width: 10, height: 10, borderRadius: 0.5, bgcolor: color }} />
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
    </Stack>
  );
}

/** One hue, magnitude only: each step as a share of the contacts, with the step-to-step conversion. */
function Funnel({ funnel }: { funnel: RecoveryReport['funnel'] }) {
  const steps = [
    { label: 'Contactos', value: funnel.contacts },
    { label: 'Alcanzados', value: funnel.reached },
    { label: 'Respondieron', value: funnel.replied },
    { label: 'Prometieron', value: funnel.promised },
    { label: 'Pagaron', value: funnel.paid },
  ];
  const top = Math.max(1, funnel.contacts);

  return (
    <Stack spacing={1.5} sx={{ mt: 2 }}>
      {steps.map((s, i) => {
        const previous = i === 0 ? null : steps[i - 1].value;
        return (
          <Box key={s.label}>
            <Stack direction="row" justifyContent="space-between">
              <Typography variant="body2">{s.label}</Typography>
              <Typography variant="body2">
                <b>{s.value.toLocaleString('es-MX')}</b>
                {previous != null && previous > 0 && (
                  <Typography component="span" variant="caption" color="text.secondary">
                    {' '}
                    · {pct(s.value / previous)}
                  </Typography>
                )}
              </Typography>
            </Stack>
            <Box sx={{ height: 8, borderRadius: 1, bgcolor: 'action.hover', overflow: 'hidden' }}>
              <Box sx={{ height: '100%', width: `${(s.value / top) * 100}%`, bgcolor: 'primary.main', borderRadius: 1 }} />
            </Box>
          </Box>
        );
      })}
    </Stack>
  );
}

function Resolution({ items }: { items: RecoveryReport['resolution'] }) {
  const total = items.reduce((s, i) => s + i.contacts, 0);
  if (total === 0)
    return (
      <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
        Aún no hay contactos alcanzados.
      </Typography>
    );
  return (
    <Stack spacing={1} sx={{ mt: 2 }}>
      {items.map((i) => (
        <Box key={i.key}>
          <Stack direction="row" justifyContent="space-between">
            <Typography variant="body2">{i.label}</Typography>
            <Typography variant="body2">
              {i.contacts}{' '}
              <Typography component="span" variant="caption" color="text.secondary">
                · {pct(i.contacts / total)}
              </Typography>
            </Typography>
          </Stack>
          <Box sx={{ height: 6, borderRadius: 1, bgcolor: 'action.hover', overflow: 'hidden' }}>
            <Box sx={{ height: '100%', width: `${(i.contacts / total) * 100}%`, bgcolor: 'primary.main', borderRadius: 1 }} />
          </Box>
        </Box>
      ))}
    </Stack>
  );
}

function GroupTable({
  rows,
  currency,
  firstColumn,
  compact,
}: {
  rows: Group[];
  currency: string;
  firstColumn: string;
  compact?: boolean;
}) {
  return (
    <TableContainer>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>{firstColumn}</TableCell>
            <TableCell align="right">Alcanzados</TableCell>
            {!compact && <TableCell align="right">Respondieron</TableCell>}
            {!compact && <TableCell align="right">Prometieron</TableCell>}
            <TableCell align="right">Pagaron</TableCell>
            <TableCell align="right">Vencido gestionado</TableCell>
            <TableCell align="right">Recuperado</TableCell>
            <TableCell sx={{ minWidth: 120 }}>Tasa</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.length === 0 && (
            <TableRow>
              <TableCell colSpan={8} align="center" sx={{ py: 3, color: 'text.secondary' }}>
                Sin contactos de campañas en el periodo.
              </TableCell>
            </TableRow>
          )}
          {rows.map((r) => (
            <TableRow key={r.key}>
              <TableCell>{r.label}</TableCell>
              <TableCell align="right">{r.reached}</TableCell>
              {!compact && <TableCell align="right">{r.replied}</TableCell>}
              {!compact && <TableCell align="right">{r.promised}</TableCell>}
              <TableCell align="right">{r.customersPaid}</TableCell>
              <TableCell align="right">{money(r.managedOverdue, currency)}</TableCell>
              <TableCell align="right">{money(r.recovered, currency)}</TableCell>
              <TableCell>
                <Tooltip title={`${money(r.recovered, currency)} de ${money(r.managedOverdue, currency)}`}>
                  <Stack direction="row" spacing={1} alignItems="center">
                    <Box sx={{ flex: 1, height: 6, borderRadius: 1, bgcolor: 'action.hover', overflow: 'hidden' }}>
                      <Box
                        sx={{ height: '100%', width: `${Math.min(100, r.recoveryRate * 100)}%`, bgcolor: 'primary.main', borderRadius: 1 }}
                      />
                    </Box>
                    <Typography variant="caption">{pct(r.recoveryRate)}</Typography>
                  </Stack>
                </Tooltip>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}

function RecentTable({ items, currency }: { items: RecoveryReport['recent']; currency: string }) {
  return (
    <TableContainer>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>Cliente</TableCell>
            <TableCell align="right">Monto</TableCell>
            <TableCell>Origen</TableCell>
            <TableCell>Campaña</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {items.length === 0 && (
            <TableRow>
              <TableCell colSpan={4} align="center" sx={{ py: 3, color: 'text.secondary' }}>
                Sin pagos en el periodo.
              </TableCell>
            </TableRow>
          )}
          {items.map((e, i) => (
            <TableRow key={`${e.partyId}-${e.at}-${i}`}>
              <TableCell>
                <Typography variant="body2">{e.customer || 'Cliente'}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {e.loanExternalId} · {e.kind === 'cutoff' ? `corte del ${day(e.at)}` : dateTime(e.at)}
                </Typography>
              </TableCell>
              <TableCell align="right">{money(e.amount, currency)}</TableCell>
              <TableCell>
                <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                  <Chip size="small" variant="soft" label={e.kind === 'payment' ? 'Pago' : 'Corte'} />
                  {e.withPromise && <Chip size="small" variant="soft" color="success" label="Con promesa" />}
                </Stack>
              </TableCell>
              <TableCell>{e.campaignName ?? <Typography variant="caption" color="text.secondary">Sin campaña</Typography>}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
