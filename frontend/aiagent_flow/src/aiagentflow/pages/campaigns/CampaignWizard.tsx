import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Step from '@mui/material/Step';
import Alert from '@mui/material/Alert';
import Radio from '@mui/material/Radio';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Switch from '@mui/material/Switch';
import Stepper from '@mui/material/Stepper';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import StepLabel from '@mui/material/StepLabel';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import RadioGroup from '@mui/material/RadioGroup';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import LinearProgress from '@mui/material/LinearProgress';
import FormControlLabel from '@mui/material/FormControlLabel';

import axios, { endpoints } from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

import { FollowUpEditor } from './FollowUpEditor';
import { missingFor } from '../templates/templateModel';
import { TemplateBubble } from '../templates/TemplateBubble';
import {
  money,
  WEEKDAYS,
  parseFilter,
  TRIGGER_TYPE,
  CAMPAIGN_TYPE,
  SCHEDULE_TYPE,
  renderMessage,
  CHANNEL_ACTION,
  EXECUTION_MODE,
  parseFollowUps,
  serializeFilter,
  defaultFollowUps,
  CampaignTypeValue,
  COMMERCE_VARIABLES,
  serializeFollowUps,
  COLLECTIONS_VARIABLES,
  DEFAULT_COLLECTIONS_MESSAGE,
} from './campaignModel';

import type { WaTemplate } from '../templates/templateModel';
import type { Campaign, FollowUpForm, AudienceFilter, AudiencePreview } from './campaignModel';

// ----------------------------------------------------------------------

const STEPS = ['Objetivo', 'Audiencia', 'Mensaje', 'Programación', 'Seguimiento', 'Revisar'];

const STAGES = [
  { key: 'Dpd1To30', label: '1-30 días' },
  { key: 'Dpd31To60', label: '31-60 días' },
  { key: 'Dpd61To90', label: '61-90 días' },
  { key: 'Dpd90Plus', label: '90+ días' },
  { key: 'Current', label: 'Al corriente' },
];

type ScheduleMode = 'manual' | 'once' | 'daily' | 'weekly';
type DeliveryMode = 'message' | 'workflow';

type Form = {
  name: string;
  description: string;
  campaignType: number;
  filter: AudienceFilter;
  channel: string;
  delivery: DeliveryMode;
  workflowDefinitionId: string;
  messageDraft: string;
  /** Approved WhatsApp (Meta) template for the first message; empty = free text. */
  whatsAppTemplateId: string;
  schedule: ScheduleMode;
  startAt: string;
  time: string;
  weekday: string;
  timezone: string;
  followUps: FollowUpForm;
};

export type WizardPreset = { source?: 'collections' | 'commerce'; stage?: string };

type Props = {
  open: boolean;
  tenantId: string;
  campaign: Campaign | null;
  preset?: WizardPreset | null;
  collectionsEnabled: boolean;
  onClose: () => void;
  onSaved: (campaign: Campaign) => void;
};

const localDateTime = (date = new Date()) =>
  new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16);

function initialForm(campaign: Campaign | null, preset: WizardPreset | null | undefined, collectionsEnabled: boolean): Form {
  if (campaign) {
    const [weekday, ...timeParts] = (campaign.scheduleExpression || '').split(':');
    const schedule: ScheduleMode =
      campaign.triggerType === TRIGGER_TYPE.Manual
        ? 'manual'
        : campaign.scheduleType === SCHEDULE_TYPE.Daily
          ? 'daily'
          : campaign.scheduleType === SCHEDULE_TYPE.Weekly
            ? 'weekly'
            : 'once';
    return {
      name: campaign.name,
      description: campaign.description,
      campaignType: campaign.campaignType,
      filter: parseFilter(campaign.audienceFilterJson),
      channel: campaign.channel || 'whatsapp',
      delivery: campaign.channelAction === CHANNEL_ACTION.WorkflowStart || campaign.workflowDefinitionId ? 'workflow' : 'message',
      workflowDefinitionId: campaign.workflowDefinitionId ?? '',
      messageDraft: campaign.messageDraft ?? '',
      whatsAppTemplateId: campaign.whatsAppTemplateId ?? '',
      schedule,
      startAt: localDateTime(new Date(campaign.startAt)),
      time: schedule === 'weekly' ? timeParts.join(':') || '09:00' : campaign.scheduleExpression || '09:00',
      weekday: schedule === 'weekly' ? weekday || 'Monday' : 'Monday',
      timezone: campaign.timezone || 'America/Mexico_City',
      followUps: parseFollowUps(campaign, campaign.campaignType === CampaignTypeValue.Collections),
    };
  }
  const collections = (preset?.source ?? (collectionsEnabled ? 'collections' : 'commerce')) === 'collections';
  return {
    name: '',
    description: '',
    campaignType: collections ? CampaignTypeValue.Collections : CampaignTypeValue.Sales,
    filter: {
      source: collections ? 'collections' : 'commerce',
      stages: preset?.stage ? [preset.stage] : [],
      excludeCampaignContactDays: 3,
    },
    channel: 'whatsapp',
    delivery: 'message',
    workflowDefinitionId: '',
    messageDraft: collections ? DEFAULT_COLLECTIONS_MESSAGE : 'Hola {{primer_nombre}}, ',
    whatsAppTemplateId: '',
    schedule: 'manual',
    startAt: localDateTime(),
    time: '09:00',
    weekday: 'Monday',
    timezone: 'America/Mexico_City',
    followUps: defaultFollowUps(collections),
  };
}

export function CampaignWizard({ open, tenantId, campaign, preset, collectionsEnabled, onClose, onSaved }: Props) {
  const [step, setStep] = useState(0);
  const [form, setForm] = useState<Form>(() => initialForm(campaign, preset, collectionsEnabled));
  const [preview, setPreview] = useState<AudiencePreview | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [workflows, setWorkflows] = useState<{ id: string; name: string }[]>([]);
  const [segments, setSegments] = useState<{ id: string; name: string; filterJson: string }[]>([]);
  const [waTemplates, setWaTemplates] = useState<WaTemplate[]>([]);
  // WhatsApp numbers: each is a channel, with its own Meta account (collections, sales, marketing...).
  const [waChannels, setWaChannels] = useState<
    { id: string; name: string; business: boolean; provider: string; businessAccountId?: string; connectionId?: string }[]
  >([]);
  const [prompt, setPrompt] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setStep(0);
    setError(null);
    setPreview(null);
    setPrompt('');
    setForm(initialForm(campaign, preset, collectionsEnabled));
    axios
      .get(endpoints.agentflow.workflows.list(tenantId))
      .then((res) => setWorkflows(((res.data ?? []) as any[]).filter((w) => w?.id).map((w) => ({ id: w.id, name: w.name || w.id }))))
      .catch(() => setWorkflows([]));
    axios
      .get(endpoints.agentflow.campaignSegments.list(tenantId))
      .then((res) => setSegments(((res.data ?? []) as any[]).map((s) => ({ id: s.id, name: s.name, filterJson: s.filterJson }))))
      .catch(() => setSegments([]));
    axios
      .get<{ items: WaTemplate[] }>(`/api/v1/tenants/${tenantId}/whatsapp/templates`)
      .then((res) => setWaTemplates(res.data.items ?? []))
      .catch(() => setWaTemplates([]));
    axios
      .get(endpoints.agentflow.channels.list(tenantId))
      .then((res) =>
        setWaChannels(
          ((res.data ?? []) as any[])
            .filter((c) => c?.type === 'WhatsApp')
            .map((c) => ({
              id: c.id,
              name: c.name,
              business: (c.config?.AuthMode ?? 'qr') !== 'qr',
              provider: c.config?.AuthMode === 'twilio' ? 'twilio' : 'meta',
              businessAccountId: c.config?.BusinessAccountId,
              connectionId: c.config?.ConnectionId,
            }))
        )
      )
      .catch(() => setWaChannels([]));
  }, [open, campaign, preset, collectionsEnabled, tenantId]);

  const filterJson = useMemo(() => serializeFilter(form.filter), [form.filter]);

  // Live audience preview while editing the audience (debounced).
  useEffect(() => {
    if (!open) return undefined;
    setPreviewing(true);
    const id = setTimeout(() => {
      axios
        .post<AudiencePreview>(endpoints.agentflow.campaignSegments.preview(tenantId), { filterJson, campaignId: campaign?.id })
        .then((res) => setPreview(res.data))
        .catch(() => setPreview(null))
        .finally(() => setPreviewing(false));
    }, 400);
    return () => clearTimeout(id);
  }, [open, tenantId, filterJson, campaign?.id]);

  const set = <K extends keyof Form>(key: K, value: Form[K]) => setForm((f) => ({ ...f, [key]: value }));
  const setFilter = (patch: Partial<AudienceFilter>) => setForm((f) => ({ ...f, filter: { ...f.filter, ...patch } }));
  const num = (v: string) => (v === '' ? undefined : Number(v));

  const isCollections = form.filter.source === 'collections';
  const variables = isCollections ? COLLECTIONS_VARIABLES : COMMERCE_VARIABLES;
  const sample = preview?.contacts?.[0];
  const rendered = renderMessage(form.messageDraft, sample?.variables);
  const chosenChannel = waChannels.find((c) => c.id === form.channel) ?? null;
  const isWhatsApp = form.channel === 'whatsapp' || !!chosenChannel;
  // Only approved templates the chosen number can send (its Twilio account, or its Meta account);
  // the one already chosen stays listed.
  const fitsChannel = (t: WaTemplate) =>
    !chosenChannel ||
    (chosenChannel.provider === 'twilio'
      ? t.provider === 'twilio' && (t.connectionId ?? '') === (chosenChannel.connectionId ?? '')
      : t.provider !== 'twilio' && (!chosenChannel.businessAccountId || t.businessAccountId === chosenChannel.businessAccountId));
  const templateOptions = waTemplates.filter((t) => (t.status === 'APPROVED' && fitsChannel(t)) || t.id === form.whatsAppTemplateId);
  const chosenTemplate = waTemplates.find((t) => t.id === form.whatsAppTemplateId) ?? null;
  const templateMissing = chosenTemplate && sample ? missingFor(chosenTemplate, sample.variables) : [];
  const missing = Array.from(rendered.matchAll(/\{\{\s*([\w.]+)\s*\}\}/g)).map((m) => m[1]);

  const stepError = (() => {
    if (step === 0 && !form.name.trim()) return 'Ponle un nombre a la campaña.';
    if (step === 2 && form.delivery === 'message' && !form.whatsAppTemplateId && !form.messageDraft.trim()) return 'Escribe el mensaje.';
    if (step === 2 && chosenTemplate && chosenTemplate.status !== 'APPROVED') return `La plantilla ${chosenTemplate.name} no está aprobada por WhatsApp.`;
    if (step === 2 && form.delivery === 'message' && chosenChannel?.business && !form.whatsAppTemplateId)
      return 'Con la API oficial de WhatsApp el primer mensaje debe ser una plantilla aprobada.';
    if (step === 2 && form.delivery === 'workflow' && !form.workflowDefinitionId) return 'Elige la automatización.';
    return null;
  })();

  const generateWithAi = async () => {
    if (!prompt.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const res = await axios.post(endpoints.agentflow.campaignBuilder.draftFromPrompt(tenantId), { prompt });
      const draft = res.data ?? {};
      setForm((f) => ({
        ...f,
        name: draft.campaignDraft?.name || f.name,
        description: draft.campaignDraft?.description || f.description,
        campaignType: typeof draft.campaignDraft?.campaignType === 'number' ? draft.campaignDraft.campaignType : f.campaignType,
        messageDraft: draft.messageDraft || draft.campaignDraft?.messageDraft || f.messageDraft,
      }));
    } catch {
      setError('No se pudo generar el borrador con IA. Puedes llenarlo manualmente.');
    } finally {
      setBusy(false);
    }
  };

  const save = async (activate: boolean) => {
    setBusy(true);
    setError(null);
    const scheduleType =
      form.schedule === 'daily' ? SCHEDULE_TYPE.Daily : form.schedule === 'weekly' ? SCHEDULE_TYPE.Weekly : SCHEDULE_TYPE.Once;
    const payload = {
      name: form.name.trim(),
      description: form.description,
      goal: form.description,
      campaignType: form.campaignType,
      status: activate ? 2 : 0,
      enabled: activate,
      triggerType: form.schedule === 'manual' ? TRIGGER_TYPE.Manual : TRIGGER_TYPE.Schedule,
      channel: form.channel,
      channelAction: form.delivery === 'workflow' ? CHANNEL_ACTION.WorkflowStart : CHANNEL_ACTION.Message,
      executionMode: form.delivery === 'workflow' ? EXECUTION_MODE.Workflow : EXECUTION_MODE.Direct,
      workflowDefinitionId: form.delivery === 'workflow' ? form.workflowDefinitionId : '',
      messageDraft: form.messageDraft,
      // "" clears it on the server.
      whatsAppTemplateId: form.delivery === 'message' && isWhatsApp ? form.whatsAppTemplateId : '',
      scheduleType,
      scheduleExpression: form.schedule === 'weekly' ? `${form.weekday}:${form.time}` : form.schedule === 'daily' ? form.time : '',
      startAt: form.schedule === 'once' ? new Date(form.startAt).toISOString() : new Date().toISOString(),
      timezone: form.timezone,
      audienceFilterJson: filterJson,
      ...serializeFollowUps(form.followUps, isCollections),
    };
    try {
      const res = campaign
        ? await axios.put(endpoints.agentflow.campaigns.update(tenantId, campaign.id), payload)
        : await axios.post(endpoints.agentflow.campaigns.create(tenantId), payload);
      onSaved(res.data);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? err?.response?.data?.title ?? 'No se pudo guardar la campaña.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{campaign ? 'Editar campaña' : 'Nueva campaña'}</DialogTitle>
      <DialogContent>
        <Stepper activeStep={step} alternativeLabel sx={{ mb: 3, mt: 1 }}>
          {STEPS.map((label) => (
            <Step key={label}>
              <StepLabel>{label}</StepLabel>
            </Step>
          ))}
        </Stepper>

        {step === 0 && (
          <Stack spacing={2}>
            <TextField label="Nombre" value={form.name} onChange={(e) => set('name', e.target.value)} autoFocus />
            <TextField select label="Tipo de campaña" value={form.campaignType} onChange={(e) => set('campaignType', Number(e.target.value))}>
              {CAMPAIGN_TYPE.map((label, i) => (
                <MenuItem key={label} value={i}>
                  {label}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              label="Objetivo (para tu equipo)"
              value={form.description}
              onChange={(e) => set('description', e.target.value)}
              multiline
              minRows={2}
              placeholder="Ej. Recuperar pagos de clientes con 1 a 30 días de atraso antes de fin de mes"
            />
            {!campaign && (
              <Card variant="outlined" sx={{ p: 2 }}>
                <Typography variant="subtitle2" sx={{ mb: 1 }}>
                  ¿Prefieres describirla? Genera un borrador con IA
                </Typography>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                  <TextField
                    size="small"
                    fullWidth
                    value={prompt}
                    onChange={(e) => setPrompt(e.target.value)}
                    placeholder="Ej. Recordar a clientes que su pago vence esta semana"
                  />
                  <Button variant="outlined" disabled={!prompt.trim() || busy} onClick={generateWithAi}>
                    Generar
                  </Button>
                </Stack>
              </Card>
            )}
          </Stack>
        )}

        {step === 1 && (
          <Stack spacing={2}>
            <RadioGroup
              row
              value={form.filter.source}
              onChange={(e) =>
                setForm((f) => ({ ...f, filter: { source: e.target.value as AudienceFilter['source'], stages: [], excludeCampaignContactDays: f.filter.excludeCampaignContactDays } }))
              }
            >
              <FormControlLabel value="collections" control={<Radio />} label="Cartera de cobranza" disabled={!collectionsEnabled} />
              <FormControlLabel value="commerce" control={<Radio />} label="Clientes y prospectos" />
            </RadioGroup>

            {segments.length > 0 && (
              <TextField
                select
                size="small"
                label="Usar un segmento guardado (opcional)"
                value=""
                onChange={(e) => {
                  const segment = segments.find((s) => s.id === e.target.value);
                  if (segment) set('filter', parseFilter(segment.filterJson));
                }}
              >
                {segments.map((s) => (
                  <MenuItem key={s.id} value={s.id}>
                    {s.name}
                  </MenuItem>
                ))}
              </TextField>
            )}

            {isCollections ? (
              <>
                <Box>
                  <Typography variant="body2" sx={{ mb: 1 }}>
                    Etapa de atraso (ninguna = todas con saldo)
                  </Typography>
                  <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                    {STAGES.map((s) => {
                      const active = form.filter.stages.includes(s.key);
                      return (
                        <Chip
                          key={s.key}
                          label={s.label}
                          color={active ? 'primary' : 'default'}
                          variant={active ? 'filled' : 'outlined'}
                          onClick={() =>
                            setFilter({ stages: active ? form.filter.stages.filter((x) => x !== s.key) : [...form.filter.stages, s.key] })
                          }
                        />
                      );
                    })}
                  </Stack>
                </Box>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                  <TextField size="small" type="number" label="Días de atraso desde" value={form.filter.minDaysPastDue ?? ''} onChange={(e) => setFilter({ minDaysPastDue: num(e.target.value) })} />
                  <TextField size="small" type="number" label="hasta" value={form.filter.maxDaysPastDue ?? ''} onChange={(e) => setFilter({ maxDaysPastDue: num(e.target.value) })} />
                  <TextField size="small" type="number" label="Saldo vencido mínimo" value={form.filter.minOverdueBalance ?? ''} onChange={(e) => setFilter({ minOverdueBalance: num(e.target.value) })} />
                </Stack>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems="center">
                  <TextField
                    size="small"
                    label="Solo estos clientes (ids separados por coma)"
                    value={(form.filter.customerIds ?? []).join(', ')}
                    onChange={(e) =>
                      setFilter({ customerIds: e.target.value.split(',').map((x) => x.trim()).filter(Boolean) })
                    }
                    sx={{ flex: 1 }}
                  />
                  <FormControlLabel
                    control={<Switch checked={!!form.filter.includeChargedOff} onChange={(e) => setFilter({ includeChargedOff: e.target.checked })} />}
                    label="Incluir castigados"
                  />
                </Stack>
              </>
            ) : (
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                <TextField select size="small" label="Tipo de contacto" value={form.filter.kind ?? ''} onChange={(e) => setFilter({ kind: e.target.value || undefined })} sx={{ minWidth: 180 }}>
                  <MenuItem value="">Todos</MenuItem>
                  <MenuItem value="customer">Clientes</MenuItem>
                  <MenuItem value="lead">Prospectos</MenuItem>
                </TextField>
                <TextField size="small" type="number" label="Compras mínimas" value={form.filter.minPurchaseCount ?? ''} onChange={(e) => setFilter({ minPurchaseCount: num(e.target.value) })} />
                <TextField size="small" type="number" label="Saldo pendiente mínimo" value={form.filter.minOutstandingAmount ?? ''} onChange={(e) => setFilter({ minOutstandingAmount: num(e.target.value) })} />
                <TextField size="small" label="Buscar por nombre" value={form.filter.query ?? ''} onChange={(e) => setFilter({ query: e.target.value || undefined })} />
              </Stack>
            )}

            <TextField
              size="small"
              type="number"
              label="No volver a contactar a quien esta campaña contactó en los últimos N días"
              value={form.filter.excludeCampaignContactDays ?? ''}
              onChange={(e) => setFilter({ excludeCampaignContactDays: num(e.target.value) })}
            />

            <Card variant="outlined">
              {previewing && <LinearProgress />}
              <Box sx={{ p: 2 }}>
                <Typography variant="subtitle1">
                  {preview ? `${preview.estimatedCount.toLocaleString('es-MX')} contactos` : 'Calculando audiencia…'}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  Quienes pidieron no ser contactados se omiten al enviar.
                </Typography>
                {preview?.warnings?.map((w) => (
                  <Alert key={w} severity="info" sx={{ mt: 1 }}>
                    {w}
                  </Alert>
                ))}
              </Box>
              {preview && preview.contacts.length > 0 && (
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Contacto</TableCell>
                      <TableCell>Teléfono</TableCell>
                      <TableCell>Por qué entra</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {preview.contacts.slice(0, 8).map((c) => (
                      <TableRow key={`${c.partyId}-${c.recipient}`}>
                        <TableCell>{c.displayName}</TableCell>
                        <TableCell>{c.recipient}</TableCell>
                        <TableCell>{c.reason}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </Card>
          </Stack>
        )}

        {step === 2 && (
          <Stack spacing={2}>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField
                select
                label="Canal"
                value={form.channel}
                onChange={(e) => setForm((f) => ({ ...f, channel: e.target.value, whatsAppTemplateId: '' }))}
                sx={{ minWidth: 220 }}
              >
                {waChannels.map((c) => (
                  <MenuItem key={c.id} value={c.id}>
                    WhatsApp · {c.name}
                    {c.business ? '' : ' (QR)'}
                  </MenuItem>
                ))}
                <MenuItem value="whatsapp">WhatsApp (primer número activo)</MenuItem>
                <MenuItem value="api">API / webhook</MenuItem>
                <MenuItem value="webchat">Webchat</MenuItem>
              </TextField>
              <TextField select label="Cómo se envía" value={form.delivery} onChange={(e) => set('delivery', e.target.value as DeliveryMode)} sx={{ flex: 1 }}>
                <MenuItem value="message">Mensaje directo a cada contacto</MenuItem>
                <MenuItem value="workflow">Ejecutar una automatización por contacto</MenuItem>
              </TextField>
            </Stack>

            {form.delivery === 'workflow' && (
              <TextField
                select
                label="Automatización"
                value={form.workflowDefinitionId}
                onChange={(e) => set('workflowDefinitionId', e.target.value)}
                helperText="La automatización recibe el contacto y sus datos ({{variables.total_vencido}}, {{recipient}}, {{message}}) y puede registrar el resultado con el paso «Registrar resultado de campaña»."
              >
                {workflows.length === 0 && <MenuItem disabled>No hay automatizaciones</MenuItem>}
                {workflows.map((w) => (
                  <MenuItem key={w.id} value={w.id}>
                    {w.name}
                  </MenuItem>
                ))}
              </TextField>
            )}

            {form.delivery === 'message' && isWhatsApp && (
              <TextField
                select
                label="Plantilla de WhatsApp"
                value={form.whatsAppTemplateId}
                onChange={(e) => set('whatsAppTemplateId', e.target.value)}
                helperText={
                  waTemplates.length === 0
                    ? 'No hay plantillas. Con la API oficial de WhatsApp (Meta o Twilio) solo se puede iniciar la conversación con una plantilla aprobada (créalas o sincronízalas en Plantillas WhatsApp).'
                    : 'Con la API oficial de WhatsApp (Meta o Twilio), el primer mensaje al cliente debe ser una plantilla aprobada.'
                }
              >
                <MenuItem value="">Sin plantilla: mensaje de texto (WhatsApp por QR, o dentro de 24 h)</MenuItem>
                {templateOptions.map((t) => (
                  <MenuItem key={t.id} value={t.id}>
                    {t.name} · {t.language}
                    {t.status !== 'APPROVED' ? ` (${t.status})` : ''}
                  </MenuItem>
                ))}
              </TextField>
            )}

            {chosenTemplate ? (
              <Card variant="outlined" sx={{ p: 2 }}>
                <Typography variant="caption" color="text.secondary">
                  Vista previa {sample ? `para ${sample.displayName}` : 'con datos de ejemplo'}
                </Typography>
                <Box sx={{ mt: 1 }}>
                  <TemplateBubble template={chosenTemplate} variables={sample?.variables ?? null} />
                </Box>
                {templateMissing.length > 0 && (
                  <Alert severity="warning" sx={{ mt: 1 }}>
                    Sin dato para: {templateMissing.join(', ')}. A los contactos sin ese dato no se les enviará.
                  </Alert>
                )}
              </Card>
            ) : (
            <>
            <TextField
              label={form.delivery === 'workflow' ? 'Mensaje (disponible para la automatización como {{message}})' : 'Mensaje'}
              value={form.messageDraft}
              onChange={(e) => set('messageDraft', e.target.value)}
              multiline
              minRows={4}
            />
            <Box>
              <Typography variant="caption" color="text.secondary">
                Insertar dato del contacto:
              </Typography>
              <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }}>
                {variables.map((v) => (
                  <Chip key={v.key} size="small" variant="outlined" label={v.label} onClick={() => set('messageDraft', `${form.messageDraft}{{${v.key}}}`)} />
                ))}
              </Stack>
            </Box>
            <Card variant="outlined" sx={{ p: 2, bgcolor: 'background.neutral' }}>
              <Typography variant="caption" color="text.secondary">
                Vista previa {sample ? `para ${sample.displayName}` : '(sin contactos en la audiencia)'}
              </Typography>
              <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', mt: 0.5 }}>
                {rendered || '—'}
              </Typography>
              {sample && missing.length > 0 && (
                <Alert severity="warning" sx={{ mt: 1 }}>
                  Sin dato para: {missing.join(', ')}. A los contactos sin ese dato no se les enviará.
                </Alert>
              )}
            </Card>
            </>
            )}
          </Stack>
        )}

        {step === 3 && (
          <Stack spacing={2}>
            <RadioGroup value={form.schedule} onChange={(e) => set('schedule', e.target.value as ScheduleMode)}>
              <FormControlLabel value="manual" control={<Radio />} label="Manual: la lanzo yo con «Enviar ahora»" />
              <FormControlLabel value="once" control={<Radio />} label="Una vez, en una fecha y hora" />
              <FormControlLabel value="daily" control={<Radio />} label="Todos los días" />
              <FormControlLabel value="weekly" control={<Radio />} label="Cada semana" />
            </RadioGroup>
            {form.schedule === 'once' && (
              <TextField type="datetime-local" label="Fecha y hora" value={form.startAt} onChange={(e) => set('startAt', e.target.value)} InputLabelProps={{ shrink: true }} />
            )}
            {(form.schedule === 'daily' || form.schedule === 'weekly') && (
              <Stack direction="row" spacing={2}>
                {form.schedule === 'weekly' && (
                  <TextField select label="Día" value={form.weekday} onChange={(e) => set('weekday', e.target.value)} sx={{ minWidth: 160 }}>
                    {WEEKDAYS.map((d) => (
                      <MenuItem key={d.value} value={d.value}>
                        {d.label}
                      </MenuItem>
                    ))}
                  </TextField>
                )}
                <TextField type="time" label="Hora" value={form.time} onChange={(e) => set('time', e.target.value)} InputLabelProps={{ shrink: true }} />
              </Stack>
            )}
            <Alert severity="info">
              Con audiencias de cartera, cada envío toma el último corte importado: un cliente que ya pagó deja de recibir mensajes.
            </Alert>
          </Stack>
        )}

        {step === 4 && (
          <FollowUpEditor
            value={form.followUps}
            collections={isCollections}
            onChange={(v) => set('followUps', v)}
            templates={templateOptions.filter((t) => t.status === 'APPROVED')}
            official={!!chosenChannel?.business}
          />
        )}

        {step === 5 && (
          <Stack spacing={1.5}>
            <Review label="Campaña" value={`${form.name} · ${CAMPAIGN_TYPE[form.campaignType]}`} />
            <Review
              label="Audiencia"
              value={`${isCollections ? 'Cartera de cobranza' : 'Clientes y prospectos'} · ${preview?.estimatedCount ?? '—'} contactos${
                form.filter.stages.length ? ` · etapas ${form.filter.stages.map((s) => STAGES.find((x) => x.key === s)?.label).join(', ')}` : ''
              }`}
            />
            <Review
              label="Envío"
              value={`${chosenChannel ? `WhatsApp · ${chosenChannel.name}` : form.channel === 'whatsapp' ? 'WhatsApp' : form.channel} · ${
                form.delivery === 'workflow' ? `automatización ${workflows.find((w) => w.id === form.workflowDefinitionId)?.name ?? ''}` : 'mensaje directo'
              }`}
            />
            <Review
              label="Cuándo"
              value={
                form.schedule === 'manual'
                  ? 'Manual'
                  : form.schedule === 'once'
                    ? new Date(form.startAt).toLocaleString('es-MX')
                    : form.schedule === 'daily'
                      ? `Diario a las ${form.time}`
                      : `${WEEKDAYS.find((d) => d.value === form.weekday)?.label} a las ${form.time}`
              }
            />
            <Review label="Seguimiento" value={followUpSummary(form.followUps, isCollections)} />
            <Card variant="outlined" sx={{ p: 2 }}>
              <Typography variant="caption" color="text.secondary">
                Ejemplo de mensaje
              </Typography>
              <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                {rendered}
              </Typography>
            </Card>
            {preview && preview.contacts.length > 0 && isCollections && (
              <Typography variant="caption" color="text.secondary">
                Saldo vencido de la audiencia mostrada: {money(preview.contacts.reduce((s, c) => s + c.outstandingAmount, 0))}
              </Typography>
            )}
          </Stack>
        )}

        {error && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {error}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Box sx={{ flex: 1 }} />
        {step > 0 && <Button onClick={() => setStep(step - 1)}>Atrás</Button>}
        {step < STEPS.length - 1 ? (
          <Button variant="contained" disabled={!!stepError} onClick={() => setStep(step + 1)} title={stepError ?? ''}>
            Siguiente
          </Button>
        ) : (
          <>
            <Button variant="outlined" disabled={busy} onClick={() => save(false)}>
              Guardar como borrador
            </Button>
            <Button variant="contained" disabled={busy} startIcon={<Iconify icon="mdi:check" />} onClick={() => save(true)}>
              Guardar y activar
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  );
}

function Review({ label, value }: { label: string; value: string }) {
  return (
    <Stack direction="row" spacing={2}>
      <Typography variant="body2" color="text.secondary" sx={{ minWidth: 110 }}>
        {label}
      </Typography>
      <Typography variant="body2">{value}</Typography>
    </Stack>
  );
}

function followUpSummary(f: FollowUpForm, collections: boolean): string {
  const parts: string[] = [];
  if (f.noResponse.on) parts.push(`sin respuesta en ${f.noResponse.hours} h`);
  if (collections && f.promiseReminder.on) parts.push('recordatorio de promesa');
  if (collections && f.promiseBroken.on) parts.push('promesa incumplida');
  if (f.callback.on) parts.push('volver a escribir');
  if (f.handoffHuman || f.handoffCannotPay) parts.push('pasa a persona');
  const rules = f.windowOn ? ` · de ${f.windowStart}:00 a ${f.windowEnd}:00${f.skipSunday ? ', sin domingos' : ''}` : '';
  const cap = f.maxPerDay > 0 ? ` · máx. ${f.maxPerDay}/día` : '';
  return `${parts.length ? parts.join(', ') : 'Sin seguimientos'}${rules}${cap}`;
}
