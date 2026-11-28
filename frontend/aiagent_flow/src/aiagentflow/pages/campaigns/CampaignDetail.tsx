import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Tabs from '@mui/material/Tabs';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Drawer from '@mui/material/Drawer';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import LinearProgress from '@mui/material/LinearProgress';
import TableContainer from '@mui/material/TableContainer';

import axios, { endpoints } from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

import { useLineNames } from './useLineNames';
import {
  day,
  pct,
  money,
  dateTime,
  RUN_STATUS,
  RUN_TRIGGER,
  parseFilter,
  channelLabel,
  CAMPAIGN_TYPE,
  deliveryLabel,
  CONTACT_STATUS,
  SCHEDULE_LABEL,
  CAMPAIGN_STATUS,
  OUTCOME_SOURCE_LABEL,
  OUTCOME_CATEGORY_COLOR,
} from './campaignModel';

import type { Campaign, CampaignRun, CampaignResults, CampaignContact, OutcomeDefinition } from './campaignModel';

// ----------------------------------------------------------------------

type Props = {
  tenantId: string;
  campaign: Campaign | null;
  onClose: () => void;
  onEdit: (campaign: Campaign) => void;
  onChanged: () => void;
};

type TabKey = 'results' | 'contacts' | 'runs' | 'settings';

export function CampaignDetail({ tenantId, campaign, onClose, onEdit, onChanged }: Props) {
  const lineNames = useLineNames(tenantId);
  const [tab, setTab] = useState<TabKey>('results');
  const [results, setResults] = useState<CampaignResults | null>(null);
  const [runs, setRuns] = useState<CampaignRun[]>([]);
  const [catalog, setCatalog] = useState<OutcomeDefinition[]>([]);
  const [loading, setLoading] = useState(false);
  const [notice, setNotice] = useState<{ severity: 'success' | 'error' | 'info'; text: string } | null>(null);
  const [confirmRun, setConfirmRun] = useState<number | null>(null);
  const [editing, setEditing] = useState<CampaignContact | null>(null);
  const [outcomeFilter, setOutcomeFilter] = useState('');

  const base = `/api/v1/tenants/${tenantId}`;

  const load = useCallback(async () => {
    if (!campaign) return;
    setLoading(true);
    try {
      const [resultsRes, runsRes, catalogRes] = await Promise.all([
        axios.get<CampaignResults>(`${base}/campaigns/${campaign.id}/results`),
        axios.get<CampaignRun[]>(endpoints.agentflow.campaigns.runs(tenantId, campaign.id)),
        axios.get<OutcomeDefinition[]>(`${base}/campaign-outcomes`, { params: { campaignType: campaign.campaignType } }),
      ]);
      setResults(resultsRes.data);
      setRuns(runsRes.data ?? []);
      setCatalog(catalogRes.data ?? []);
    } finally {
      setLoading(false);
    }
  }, [base, campaign, tenantId]);

  useEffect(() => {
    setTab('results');
    setNotice(null);
    setResults(null);
    setOutcomeFilter('');
    load();
  }, [load]);

  if (!campaign) return null;

  const status = CAMPAIGN_STATUS[campaign.status] ?? CAMPAIGN_STATUS[0];
  const filter = parseFilter(campaign.audienceFilterJson);
  const active = campaign.enabled && (campaign.status === 1 || campaign.status === 2);

  const askRun = async () => {
    const res = await axios.post(endpoints.agentflow.campaignSegments.preview(tenantId), {
      filterJson: campaign.audienceFilterJson,
      campaignId: campaign.id,
    });
    setConfirmRun(res.data?.estimatedCount ?? 0);
  };

  const runNow = async () => {
    setConfirmRun(null);
    setNotice({ severity: 'info', text: 'Enviando…' });
    try {
      const res = await axios.post(endpoints.agentflow.campaigns.runNow(tenantId, campaign.id));
      const counters = JSON.parse(res.data?.countersJson || '{}');
      setNotice({
        severity: res.data?.status === 4 ? 'error' : 'success',
        text: `Envío terminado: ${counters.Sent ?? 0} enviados, ${counters.WorkflowStarted ?? 0} en automatización, ${counters.Failed ?? 0} fallidos, ${counters.Skipped ?? 0} omitidos.`,
      });
      setTab('contacts');
      load();
      onChanged();
    } catch (err: any) {
      setNotice({ severity: 'error', text: err?.response?.data?.message ?? 'No se pudo enviar la campaña.' });
    }
  };

  const toggleActive = async () => {
    const url = active
      ? endpoints.agentflow.campaigns.pause(tenantId, campaign.id)
      : campaign.status === 0
        ? endpoints.agentflow.campaigns.publish(tenantId, campaign.id)
        : endpoints.agentflow.campaigns.resume(tenantId, campaign.id);
    await axios.post(url);
    onChanged();
  };

  const retry = async (runId: string) => {
    await axios.post(endpoints.agentflow.campaigns.retryFailures(tenantId, runId));
    load();
  };

  const contacts = (results?.contactsList ?? []).filter((c) =>
    !outcomeFilter ? true : outcomeFilter === 'attention' ? !!c.needsAttention : (c.outcome ?? 'pending') === outcomeFilter
  );
  const currency = results?.currency || 'MXN';

  const resolveAttention = async (contactId: string) => {
    await axios.post(`${base}/campaign-contacts/${contactId}/attention/resolve`);
    load();
  };
  const outcomeLabel = (code?: string | null) => catalog.find((o) => o.code === code)?.label ?? results?.outcomes.find((o) => o.code === code)?.label ?? code;
  const outcomeColor = (code?: string | null) =>
    OUTCOME_CATEGORY_COLOR[catalog.find((o) => o.code === code)?.category ?? results?.outcomes.find((o) => o.code === code)?.category ?? ''] ?? 'default';

  return (
    <Drawer anchor="right" open={!!campaign} onClose={onClose} PaperProps={{ sx: { width: { xs: '100%', md: 920 } } }}>
      {loading && <LinearProgress />}
      <Box sx={{ p: 3 }}>
        <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" spacing={2}>
          <Box>
            <Stack direction="row" spacing={1} alignItems="center">
              <Typography variant="h5">{campaign.name}</Typography>
              <Chip size="small" label={status.label} color={status.color} variant="soft" />
            </Stack>
            <Typography variant="body2" color="text.secondary">
              {CAMPAIGN_TYPE[campaign.campaignType]} · {channelLabel(campaign.channel, lineNames)} ·{' '}
              {filter.source === 'collections' ? 'Cartera de cobranza' : 'Clientes y prospectos'}
              {campaign.lastRunAt ? ` · último envío ${dateTime(campaign.lastRunAt)}` : ' · aún sin envíos'}
            </Typography>
          </Box>
          <Stack direction="row" spacing={1} alignItems="flex-start">
            <Button variant="outlined" startIcon={<Iconify icon="mdi:pencil" />} onClick={() => onEdit(campaign)}>
              Editar
            </Button>
            <Button variant="outlined" color={active ? 'warning' : 'success'} onClick={toggleActive}>
              {active ? 'Pausar' : 'Activar'}
            </Button>
            <Button variant="contained" startIcon={<Iconify icon="mdi:send" />} onClick={askRun}>
              Enviar ahora
            </Button>
          </Stack>
        </Stack>

        {notice && (
          <Alert severity={notice.severity} sx={{ mt: 2 }} onClose={() => setNotice(null)}>
            {notice.text}
          </Alert>
        )}

        <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mt: 2, borderBottom: 1, borderColor: 'divider' }}>
          <Tab value="results" label="Resultados" />
          <Tab value="contacts" label={`Contactos${results ? ` (${results.contacts})` : ''}`} />
          <Tab value="runs" label={`Envíos (${runs.length})`} />
          <Tab value="settings" label="Configuración" />
        </Tabs>

        {tab === 'results' && results && (
          <Box sx={{ mt: 2 }}>
            {results.contacts === 0 ? (
              <Alert severity="info">Esta campaña aún no se ha enviado. Usa «Enviar ahora» o prográmala.</Alert>
            ) : (
              <>
                <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' } }}>
                  <Metric label="Contactos" value={results.contacts} />
                  <Metric label="Enviados" value={results.sent} hint={results.failed ? `${results.failed} fallidos` : undefined} />
                  <Metric label="Respondieron" value={results.replied} hint={`${pct(results.replyRate)} de enviados`} />
                  <Metric label="Con resultado" value={results.withOutcome} hint={`${results.pending} pendientes`} />
                  <Metric
                    label="Promesas de pago"
                    value={results.promises}
                    hint={results.promisedAmount ? money(results.promisedAmount, currency) : undefined}
                  />
                  <Metric label="Seguimientos enviados" value={results.followUpsSent} />
                  <Box
                    sx={{ cursor: results.needsAttention ? 'pointer' : 'default' }}
                    onClick={() => {
                      if (!results.needsAttention) return;
                      setOutcomeFilter('attention');
                      setTab('contacts');
                    }}
                  >
                    <Metric label="Para atender" value={results.needsAttention} hint={results.needsAttention ? 'Ver contactos' : undefined} />
                  </Box>
                </Box>

                <Typography variant="overline" color="text.secondary" component="div" sx={{ mt: 3, mb: 1 }}>
                  ¿Qué pasó con cada contacto?
                </Typography>
                <Stack spacing={1}>
                  {results.outcomes.map((o) => (
                    <Box
                      key={o.code}
                      sx={{ cursor: 'pointer' }}
                      onClick={() => {
                        setOutcomeFilter(o.code);
                        setTab('contacts');
                      }}
                    >
                      <Stack direction="row" justifyContent="space-between">
                        <Typography variant="body2">{o.label}</Typography>
                        <Typography variant="body2" color="text.secondary">
                          {o.count} · {pct(o.count / results.contacts)}
                        </Typography>
                      </Stack>
                      <LinearProgress
                        variant="determinate"
                        value={(o.count / results.contacts) * 100}
                        color={progressColor(o.category)}
                        sx={{ height: 8, borderRadius: 1 }}
                      />
                    </Box>
                  ))}
                  {results.pending > 0 && (
                    <Typography variant="caption" color="text.secondary">
                      {results.pending} contacto(s) sin resultado aún: se marcan «Sin respuesta» 24 h después del envío si no contestan.
                    </Typography>
                  )}
                </Stack>
              </>
            )}
          </Box>
        )}

        {tab === 'contacts' && results && (
          <Box sx={{ mt: 2 }}>
            <TextField select size="small" label="Resultado" value={outcomeFilter} onChange={(e) => setOutcomeFilter(e.target.value)} sx={{ minWidth: 240, mb: 2 }}>
              <MenuItem value="">Todos</MenuItem>
              <MenuItem value="attention">Para atender ({results.needsAttention})</MenuItem>
              <MenuItem value="pending">Sin resultado aún</MenuItem>
              {results.outcomes.map((o) => (
                <MenuItem key={o.code} value={o.code}>
                  {o.label} ({o.count})
                </MenuItem>
              ))}
            </TextField>
            <TableContainer>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Contacto</TableCell>
                    <TableCell>Envío</TableCell>
                    <TableCell>Respuesta</TableCell>
                    <TableCell>Resultado</TableCell>
                    <TableCell />
                  </TableRow>
                </TableHead>
                <TableBody>
                  {contacts.length === 0 && (
                    <TableRow>
                      <TableCell colSpan={5} align="center" sx={{ py: 3, color: 'text.secondary' }}>
                        Sin contactos con este resultado.
                      </TableCell>
                    </TableRow>
                  )}
                  {contacts.map((c) => {
                    const st = CONTACT_STATUS[c.status] ?? CONTACT_STATUS[0];
                    return (
                      <TableRow key={c.id} hover>
                        <TableCell sx={{ maxWidth: 180 }}>
                          <Typography variant="subtitle2" noWrap>
                            {c.displayName || c.recipient}
                          </Typography>
                          <Typography variant="caption" color="text.secondary">
                            {c.recipient}
                          </Typography>
                        </TableCell>
                        <TableCell sx={{ maxWidth: 220 }}>
                          <Chip size="small" label={st.label} color={st.color} variant="soft" />
                          <Typography variant="caption" color="text.secondary" component="div">
                            {dateTime(c.createdAt)}
                          </Typography>
                          {c.skipReason && (
                            <Typography variant="caption" color="error.main" component="div">
                              {SKIP_REASON[c.skipReason] ?? c.skipReason}
                            </Typography>
                          )}
                          {(c.followUps ?? []).map((f) => (
                            <Typography
                              key={`${f.ruleId}-${f.at}`}
                              variant="caption"
                              component="div"
                              color={f.status === 'failed' ? 'error.main' : 'text.secondary'}
                              title={f.message ?? f.error ?? ''}
                            >
                              {'\u21b3'} {FOLLOW_UP_LABEL[f.trigger.startsWith('outcome:') ? 'outcome' : f.trigger] ?? f.ruleId}:{' '}
                              {FOLLOW_UP_STATUS[f.status] ?? f.status} {dateTime(f.at)}
                              {f.template ? ` · plantilla ${f.template}` : ''}
                              {f.error ? ` (${f.error})` : ''}
                            </Typography>
                          ))}
                        </TableCell>
                        <TableCell sx={{ maxWidth: 260 }}>
                          {c.lastReply ? (
                            <>
                              <Typography variant="body2">“{c.lastReply}”</Typography>
                              <Typography variant="caption" color="text.secondary">
                                {dateTime(c.repliedAt)}
                                {c.replyCount > 1 ? ` · ${c.replyCount} mensajes` : ''}
                              </Typography>
                            </>
                          ) : (
                            <Typography variant="caption" color="text.secondary">
                              —
                            </Typography>
                          )}
                        </TableCell>
                        <TableCell sx={{ maxWidth: 220 }}>
                          {c.outcome ? (
                            <Chip size="small" label={outcomeLabel(c.outcome)} color={outcomeColor(c.outcome)} variant="soft" />
                          ) : (
                            <Typography variant="caption" color="text.secondary">
                              Pendiente
                            </Typography>
                          )}
                          {c.outcomeSource && (
                            <Typography variant="caption" color="text.secondary" component="div">
                              por {OUTCOME_SOURCE_LABEL[c.outcomeSource] ?? c.outcomeSource}
                            </Typography>
                          )}
                          {c.promiseDate && (
                            <Typography variant="caption" color="success.main" component="div">
                              {c.promiseAmount ? money(c.promiseAmount, currency) : 'Monto no indicado'} el {day(c.promiseDate)}
                            </Typography>
                          )}
                          {c.needsAttention && (
                            <Chip
                              size="small"
                              color="warning"
                              variant="soft"
                              sx={{ mt: 0.5 }}
                              label={`Para atender: ${c.attentionReason ?? ''}`}
                            />
                          )}
                        </TableCell>
                        <TableCell align="right">
                          <Stack spacing={0.5} alignItems="flex-end">
                            <Button size="small" onClick={() => setEditing(c)}>
                              Cambiar
                            </Button>
                            {c.needsAttention && (
                              <Button size="small" color="warning" onClick={() => resolveAttention(c.id)}>
                                Marcar atendido
                              </Button>
                            )}
                          </Stack>
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </TableContainer>
          </Box>
        )}

        {tab === 'runs' && (
          <Stack spacing={1} sx={{ mt: 2 }}>
            {runs.length === 0 && <Alert severity="info">Sin envíos todavía.</Alert>}
            {runs.map((r) => {
              const st = RUN_STATUS[r.status] ?? RUN_STATUS[0];
              const counters = JSON.parse(r.countersJson || '{}');
              const errors: string[] = r.errorSummaryJson ? JSON.parse(r.errorSummaryJson) : [];
              return (
                <Card key={r.id} variant="outlined" sx={{ p: 1.5 }}>
                  <Stack direction="row" justifyContent="space-between" alignItems="center">
                    <Typography variant="subtitle2">
                      {dateTime(r.startedAt)} · {RUN_TRIGGER[r.triggeredBy] ?? ''}
                    </Typography>
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Chip size="small" label={st.label} color={st.color} variant="soft" />
                      {(counters.Failed ?? 0) > 0 && (
                        <Button size="small" onClick={() => retry(r.id)}>
                          Reintentar fallidos
                        </Button>
                      )}
                    </Stack>
                  </Stack>
                  <Typography variant="caption" color="text.secondary">
                    {counters.Sent ?? 0} enviados · {counters.WorkflowStarted ?? 0} en automatización · {counters.Failed ?? 0} fallidos ·{' '}
                    {counters.Skipped ?? 0} omitidos
                  </Typography>
                  {errors.slice(0, 3).map((e, i) => (
                    <Typography key={i} variant="caption" color="error.main" component="div">
                      {e}
                    </Typography>
                  ))}
                </Card>
              );
            })}
          </Stack>
        )}

        {tab === 'settings' && <SettingsTab tenantId={tenantId} campaign={campaign} onChanged={load} />}
      </Box>

      <Dialog open={confirmRun !== null} onClose={() => setConfirmRun(null)}>
        <DialogTitle>¿Enviar la campaña ahora?</DialogTitle>
        <DialogContent>
          <Typography>
            Se enviará a <b>{confirmRun?.toLocaleString('es-MX')}</b> contacto(s) por{' '}
            {campaign.channel === 'whatsapp' ? 'WhatsApp' : campaign.channel}. Esta acción no se puede deshacer.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmRun(null)}>Cancelar</Button>
          <Button variant="contained" disabled={!confirmRun} onClick={runNow}>
            Enviar
          </Button>
        </DialogActions>
      </Dialog>

      <OutcomeDialog
        base={base}
        contact={editing}
        catalog={catalog}
        onClose={() => setEditing(null)}
        onSaved={() => {
          setEditing(null);
          load();
        }}
      />
    </Drawer>
  );
}

const progressColor = (category: string) => {
  const color = OUTCOME_CATEGORY_COLOR[category] ?? 'default';
  return color === 'default' ? 'inherit' : color;
};

function Metric({ label, value, hint }: { label: string; value: number; hint?: string }) {
  return (
    <Card variant="outlined" sx={{ p: 1.5 }}>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="h5">{value.toLocaleString('es-MX')}</Typography>
      {hint && (
        <Typography variant="caption" color="text.secondary">
          {hint}
        </Typography>
      )}
    </Card>
  );
}

const SKIP_REASON: Record<string, string> = {
  opt_out: 'Pidió no ser contactado',
  limite_diario: 'Ya recibió el máximo de mensajes de hoy',
};

const FOLLOW_UP_LABEL: Record<string, string> = {
  no_response: 'Seguimiento por no responder',
  promise_due: 'Recordatorio de promesa',
  promise_broken: 'Promesa incumplida',
  outcome: 'Seguimiento por respuesta',
};

const FOLLOW_UP_STATUS: Record<string, string> = {
  sent: 'enviado',
  failed: 'falló',
  handed_off: 'pasado a persona',
  skipped: 'omitido',
};

function SettingsTab({ tenantId, campaign, onChanged }: { tenantId: string; campaign: Campaign; onChanged: () => void }) {
  const lineNames = useLineNames(tenantId);
  const [followUpResult, setFollowUpResult] = useState<string | null>(null);

  const runFollowUps = async () => {
    setFollowUpResult(null);
    try {
      const res = await axios.post(`/api/v1/tenants/${tenantId}/campaigns/${campaign.id}/follow-ups/run`);
      const r = res.data ?? {};
      setFollowUpResult(
        `${r.sent ?? 0} enviados · ${r.handedOff ?? 0} pasados a persona · ${r.failed ?? 0} fallidos` +
          (r.deferred ? ` · ${r.deferred} esperan el horario` : '') +
          (r.limited ? ` · ${r.limited} por límite diario` : '') +
          (r.settled ? ` · ${r.settled} ya pagaron` : '')
      );
      onChanged();
    } catch (err: any) {
      setFollowUpResult(err?.response?.data?.message ?? 'No se pudieron ejecutar los seguimientos.');
    }
  };
  const [recipient, setRecipient] = useState('');
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null);
  const filter = parseFilter(campaign.audienceFilterJson);

  const testSend = async () => {
    setResult(null);
    try {
      await axios.post(`/api/v1/tenants/${tenantId}/campaigns/${campaign.id}/test-send`, { recipient });
      setResult({ ok: true, text: 'Mensaje de prueba enviado.' });
    } catch (err: any) {
      setResult({ ok: false, text: err?.response?.data?.error ?? err?.response?.data?.message ?? 'No se pudo enviar la prueba.' });
    }
  };

  return (
    <Stack spacing={2} sx={{ mt: 2 }}>
      <Card variant="outlined" sx={{ p: 2 }}>
        <Typography variant="subtitle2">Seguimientos</Typography>
        <Typography variant="caption" color="text.secondary" component="div">
          {campaign.followupPolicyJson
            ? 'Se ejecutan solos cada 5 minutos, respetando el horario de contacto. Puedes forzarlos ahora.'
            : 'Esta campaña no tiene seguimientos. Configúralos en Editar → Seguimiento.'}
        </Typography>
        <Button variant="outlined" size="small" sx={{ mt: 1 }} disabled={!campaign.followupPolicyJson} onClick={runFollowUps}>
          Ejecutar seguimientos ahora
        </Button>
        {followUpResult && (
          <Alert severity="info" sx={{ mt: 1 }}>
            {followUpResult}
          </Alert>
        )}
      </Card>
      <Card variant="outlined" sx={{ p: 2 }}>
        <Typography variant="subtitle2">Enviar una prueba</Typography>
        <Typography variant="caption" color="text.secondary">
          Envía el mensaje a un número tuyo, con los datos del primer contacto de la audiencia.
        </Typography>
        <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
          <TextField size="small" placeholder="+52 55 1234 5678" value={recipient} onChange={(e) => setRecipient(e.target.value)} />
          <Button variant="outlined" disabled={!recipient.trim()} onClick={testSend}>
            Enviar prueba
          </Button>
        </Stack>
        {result && (
          <Alert severity={result.ok ? 'success' : 'error'} sx={{ mt: 1 }}>
            {result.text}
          </Alert>
        )}
      </Card>
      <Card variant="outlined" sx={{ p: 2 }}>
        <Stack spacing={1}>
          <Row label="Objetivo" value={campaign.description || campaign.goal || '—'} />
          <Row
            label="Audiencia"
            value={
              filter.source === 'collections'
                ? `Cartera${filter.stages.length ? ` · etapas ${filter.stages.join(', ')}` : ''}${filter.minDaysPastDue ? ` · desde ${filter.minDaysPastDue} días` : ''}${
                    filter.customerIds?.length ? ` · clientes ${filter.customerIds.join(', ')}` : ''
                  }`
                : 'Clientes y prospectos'
            }
          />
          <Row
            label="Envío"
            value={campaign.workflowDefinitionId ? `Automatización ${campaign.workflowDefinitionId}` : `${deliveryLabel(campaign)} · ${channelLabel(campaign.channel, lineNames)}`}
          />
          <Row label="Programación" value={campaign.triggerType === 1 ? 'Manual' : `${SCHEDULE_LABEL[campaign.scheduleType]} ${campaign.scheduleExpression}`} />
          <Row label="Próximo envío" value={dateTime(campaign.nextRunAt)} />
          <Row label="Mensaje" value={campaign.messageDraft || '—'} />
        </Stack>
      </Card>
    </Stack>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <Stack direction="row" spacing={2}>
      <Typography variant="body2" color="text.secondary" sx={{ minWidth: 120 }}>
        {label}
      </Typography>
      <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
        {value}
      </Typography>
    </Stack>
  );
}

function OutcomeDialog({
  base,
  contact,
  catalog,
  onClose,
  onSaved,
}: {
  base: string;
  contact: CampaignContact | null;
  catalog: OutcomeDefinition[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [outcome, setOutcome] = useState('');
  const [notes, setNotes] = useState('');
  const [amount, setAmount] = useState('');
  const [date, setDate] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!contact) return;
    setOutcome(contact.outcome && contact.outcome !== 'no_response' && contact.outcome !== 'unreachable' ? contact.outcome : '');
    setNotes(contact.outcomeNotes ?? '');
    setAmount(contact.promiseAmount != null ? String(contact.promiseAmount) : '');
    setDate(contact.promiseDate ? contact.promiseDate.slice(0, 10) : '');
    setError(null);
  }, [contact]);

  const save = async () => {
    if (!contact) return;
    try {
      await axios.post(`${base}/campaign-contacts/${contact.id}/outcome`, {
        outcome,
        notes: notes || null,
        promiseAmount: outcome === 'promise_to_pay' && amount ? Number(amount) : null,
        promiseDate: outcome === 'promise_to_pay' && date ? `${date}T00:00:00Z` : null,
      });
      onSaved();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudo guardar el resultado.');
    }
  };

  return (
    <Dialog open={!!contact} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Resultado del contacto</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Typography variant="body2" color="text.secondary">
            {contact?.displayName || contact?.recipient}
            {contact?.lastReply ? ` respondió: “${contact.lastReply}”` : ''}
          </Typography>
          <TextField select label="Resultado" value={outcome} onChange={(e) => setOutcome(e.target.value)}>
            {catalog.map((o) => (
              <MenuItem key={o.code} value={o.code}>
                {o.label}
              </MenuItem>
            ))}
          </TextField>
          {outcome === 'promise_to_pay' && (
            <Stack direction="row" spacing={2}>
              <TextField label="Monto" type="number" value={amount} onChange={(e) => setAmount(e.target.value)} />
              <TextField label="Fecha" type="date" value={date} onChange={(e) => setDate(e.target.value)} InputLabelProps={{ shrink: true }} />
            </Stack>
          )}
          <TextField label="Notas" value={notes} onChange={(e) => setNotes(e.target.value)} multiline minRows={2} />
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="contained" disabled={!outcome} onClick={save}>
          Guardar
        </Button>
      </DialogActions>
    </Dialog>
  );
}
