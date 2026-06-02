import type { Theme } from '@mui/material/styles';

import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import List from '@mui/material/List';
import Grid from '@mui/material/Grid';
import Chip from '@mui/material/Chip';
import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import Paper from '@mui/material/Paper';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import { alpha } from '@mui/material/styles';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import ListItemText from '@mui/material/ListItemText';
import ListItemButton from '@mui/material/ListItemButton';
import LinearProgress from '@mui/material/LinearProgress';
import CircularProgress from '@mui/material/CircularProgress';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { BrandPageHeader } from 'src/aiagentflow/components/BrandPageHeader';

import { Iconify } from 'src/components/iconify';

type CampaignStatus =
  | 'Draft'
  | 'Published'
  | 'Active'
  | 'Paused'
  | 'Completed'
  | 'Failed'
  | 'Archived';

type CampaignItem = {
  id: string;
  name: string;
  description?: string;
  status: CampaignStatus;
  campaignType: string;
  executionMode: string;
  channelAction: string;
  channel: string;
  runtimeModelProfileId?: string | null;
  workflowDefinitionId?: string | null;
  startAt: string;
  nextRunAt?: string | null;
  updatedAt: string;
};

type SegmentItem = {
  id: string;
  name: string;
  description?: string;
  estimatedCount?: number | null;
  updatedAt: string;
};

type RunItem = {
  id: string;
  campaignId: string;
  status: string;
  triggeredBy: string;
  startedAt: string;
  completedAt?: string | null;
  requestedBy?: string;
};

type BuilderDraft = {
  campaignDraft: Partial<CampaignItem> & {
    id?: string;
    audienceFilterJson?: string;
    workflowDefinitionId?: string | null;
    runtimeModelProfileId?: string | null;
    messageDraft?: string | null;
    callScriptDraft?: string | null;
    scheduleExpression?: string;
    scheduleType?: string;
  };
  segmentDraft: {
    id?: string;
    name?: string;
    description?: string;
    filterJson?: string;
  };
  recommendedWorkflowLink?: string | null;
  messageDraft?: string | null;
  callScriptDraft?: string | null;
  assumptions?: string[];
  warnings?: string[];
};

type OverviewMetrics = {
  campaigns: number;
  activeCampaigns: number;
  segments: number;
  runs: number;
};

type ManualCampaignForm = {
  name: string;
  description: string;
  campaignType: string;
  executionMode: string;
  channelAction: string;
  channel: string;
  scheduleType: string;
  scheduleExpression: string;
  startAt: string;
  segmentId: string;
  audienceFilterJson: string;
  workflowDefinitionId: string;
  runtimeModelProfileId: string;
  messageDraft: string;
  callScriptDraft: string;
};

type ManualSegmentForm = {
  name: string;
  description: string;
  filterJson: string;
};

type SegmentPreview = {
  estimatedCount?: number;
  contacts?: Array<Record<string, unknown>>;
};

type SectionKey = 'summary' | 'campaigns' | 'segments' | 'calendar' | 'results';

const sections: Array<{ key: SectionKey; label: string; description: string }> = [
  { key: 'summary', label: 'Resumen', description: 'Vista operacional, manual y asistida.' },
  { key: 'campaigns', label: 'Campañas', description: 'Crea campañas nativas del producto.' },
  { key: 'segments', label: 'Segmentos', description: 'Define audiencias y valida su alcance.' },
  { key: 'calendar', label: 'Calendario', description: 'Próximas salidas programadas.' },
  { key: 'results', label: 'Resultados', description: 'Corridas, estado y trazabilidad.' },
];

const campaignTypeOptions = ['Sales', 'Collections', 'Reminder', 'Reactivation', 'Custom'];
const executionModeOptions = ['Workflow', 'Direct', 'Hybrid'];
const channelActionOptions = ['Message', 'Call', 'WorkflowStart'];
const scheduleTypeOptions = ['Once', 'Hourly', 'Daily', 'Weekly', 'Cron'];

const defaultManualCampaign = (): ManualCampaignForm => ({
  name: '',
  description: '',
  campaignType: 'Custom',
  executionMode: 'Workflow',
  channelAction: 'WorkflowStart',
  channel: 'whatsapp',
  scheduleType: 'Once',
  scheduleExpression: '',
  startAt: new Date().toISOString().slice(0, 16),
  segmentId: '',
  audienceFilterJson: '{}',
  workflowDefinitionId: '',
  runtimeModelProfileId: '',
  messageDraft: '',
  callScriptDraft: '',
});

const defaultManualSegment = (): ManualSegmentForm => ({
  name: '',
  description: '',
  filterJson: '{}',
});

function statusColor(status: string): 'default' | 'success' | 'warning' | 'error' | 'info' {
  switch (status) {
    case 'Active':
    case 'Completed':
      return 'success';
    case 'Published':
      return 'info';
    case 'Paused':
      return 'warning';
    case 'Failed':
      return 'error';
    default:
      return 'default';
  }
}

function panelSurface(theme: Theme, tone: 'brand' | 'soft' = 'soft') {
  return tone === 'brand'
    ? {
        backgroundImage: `linear-gradient(135deg, ${alpha(theme.palette.success.dark, 0.52)} 0%, ${alpha(theme.palette.warning.light, 0.26)} 100%)`,
        borderColor: alpha(theme.palette.success.main, 0.28),
      }
    : {
        backgroundColor: alpha(theme.palette.background.paper, 0.9),
        borderColor: alpha(theme.palette.divider, 0.18),
      };
}

function formatDate(value?: string | null) {
  if (!value) return 'Sin fecha';
  return new Date(value).toLocaleString();
}

function toIsoOrNow(value: string) {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? new Date().toISOString() : parsed.toISOString();
}

function prettyJson(value: unknown) {
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return '[]';
  }
}

export default function CampaignsPage() {
  const tenantId = useTenantId();
  const [activeSection, setActiveSection] = useState<SectionKey>('summary');
  const [prompt, setPrompt] = useState('');
  const [draft, setDraft] = useState<BuilderDraft | null>(null);
  const [campaigns, setCampaigns] = useState<CampaignItem[]>([]);
  const [segments, setSegments] = useState<SegmentItem[]>([]);
  const [runs, setRuns] = useState<RunItem[]>([]);
  const [manualCampaign, setManualCampaign] = useState<ManualCampaignForm>(defaultManualCampaign);
  const [manualSegment, setManualSegment] = useState<ManualSegmentForm>(defaultManualSegment);
  const [segmentPreview, setSegmentPreview] = useState<SegmentPreview | null>(null);
  const [loading, setLoading] = useState(true);
  const [savingDraft, setSavingDraft] = useState(false);
  const [savingCampaign, setSavingCampaign] = useState(false);
  const [savingSegment, setSavingSegment] = useState(false);
  const [previewingSegment, setPreviewingSegment] = useState(false);
  const [buildingDraft, setBuildingDraft] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const overview = useMemo<OverviewMetrics>(
    () => ({
      campaigns: campaigns.length,
      activeCampaigns: campaigns.filter((item) => item.status === 'Active' || item.status === 'Published').length,
      segments: segments.length,
      runs: runs.length,
    }),
    [campaigns, runs, segments.length]
  );

  const scheduledItems = useMemo(
    () =>
      campaigns
        .filter((item) => item.nextRunAt)
        .sort((a, b) => new Date(a.nextRunAt || a.startAt).getTime() - new Date(b.nextRunAt || b.startAt).getTime())
        .slice(0, 6),
    [campaigns]
  );

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [campaignsRes, segmentsRes, runsRes] = await Promise.all([
        axios.get(endpoints.agentflow.campaigns.list(tenantId)),
        axios.get(endpoints.agentflow.campaignSegments.list(tenantId)),
        axios.get(endpoints.agentflow.campaigns.allRuns(tenantId, null, 25)),
      ]);

      setCampaigns(campaignsRes.data ?? []);
      setSegments(segmentsRes.data ?? []);
      setRuns(runsRes.data ?? []);
    } catch (err: any) {
      setError(err?.message ?? 'No fue posible cargar campañas.');
    } finally {
      setLoading(false);
    }
  }, [tenantId]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  const handleDraft = async () => {
    if (!prompt.trim()) return;
    setBuildingDraft(true);
    setNotice(null);
    setError(null);
    try {
      const response = await axios.post(endpoints.agentflow.campaignBuilder.draftFromPrompt(tenantId), {
        prompt,
      });
      setDraft(response.data as BuilderDraft);
      setActiveSection('summary');
    } catch (err: any) {
      setError(err?.message ?? 'No fue posible generar el borrador.');
    } finally {
      setBuildingDraft(false);
    }
  };

  const handleSaveDraft = async () => {
    if (!draft) return;
    setSavingDraft(true);
    setNotice(null);
    setError(null);
    try {
      const payload = {
        name: draft.campaignDraft.name,
        description: draft.campaignDraft.description,
        status: 'Draft',
        campaignType: draft.campaignDraft.campaignType,
        executionMode: draft.campaignDraft.executionMode,
        triggerType: 'Schedule',
        channelAction: draft.campaignDraft.channelAction,
        channel: draft.campaignDraft.channel,
        workflowDefinitionId: draft.campaignDraft.workflowDefinitionId,
        runtimeModelProfileId: draft.campaignDraft.runtimeModelProfileId,
        messageDraft: draft.messageDraft ?? draft.campaignDraft.messageDraft,
        callScriptDraft: draft.callScriptDraft ?? draft.campaignDraft.callScriptDraft,
        promptOrigin: prompt,
        scheduleType: draft.campaignDraft.scheduleType,
        scheduleExpression: draft.campaignDraft.scheduleExpression,
        startAt: draft.campaignDraft.startAt,
        audienceFilterJson: draft.campaignDraft.audienceFilterJson ?? draft.segmentDraft.filterJson,
        enabled: true,
      };

      await axios.post(endpoints.agentflow.campaigns.create(tenantId), payload);

      if (draft.segmentDraft.filterJson && draft.segmentDraft.name) {
        await axios.post(endpoints.agentflow.campaignSegments.create(tenantId), {
          name: draft.segmentDraft.name,
          description: draft.segmentDraft.description,
          sourceModules: ['commerce', 'inbox', 'audit', 'threads'],
          filterJson: draft.segmentDraft.filterJson,
        });
      }

      setNotice('La campaña y su segmento sugerido quedaron guardados como borrador.');
      await refresh();
    } catch (err: any) {
      setError(err?.message ?? 'No fue posible guardar el borrador.');
    } finally {
      setSavingDraft(false);
    }
  };

  const handleCreateCampaign = async () => {
    if (!manualCampaign.name.trim()) {
      setError('La campaña manual necesita un nombre.');
      return;
    }

    setSavingCampaign(true);
    setNotice(null);
    setError(null);
    try {
      await axios.post(endpoints.agentflow.campaigns.create(tenantId), {
        name: manualCampaign.name,
        description: manualCampaign.description,
        status: 'Draft',
        campaignType: manualCampaign.campaignType,
        executionMode: manualCampaign.executionMode,
        triggerType: 'Schedule',
        channelAction: manualCampaign.channelAction,
        channel: manualCampaign.channel,
        scheduleType: manualCampaign.scheduleType,
        scheduleExpression: manualCampaign.scheduleExpression || null,
        startAt: toIsoOrNow(manualCampaign.startAt),
        segmentId: manualCampaign.segmentId || null,
        audienceFilterJson: manualCampaign.audienceFilterJson,
        workflowDefinitionId: manualCampaign.workflowDefinitionId || null,
        runtimeModelProfileId: manualCampaign.runtimeModelProfileId || null,
        messageDraft: manualCampaign.messageDraft || null,
        callScriptDraft: manualCampaign.callScriptDraft || null,
        enabled: true,
      });

      setManualCampaign(defaultManualCampaign());
      setNotice('La campaña manual quedó creada como borrador.');
      setActiveSection('campaigns');
      await refresh();
    } catch (err: any) {
      setError(err?.message ?? 'No fue posible crear la campaña manual.');
    } finally {
      setSavingCampaign(false);
    }
  };

  const handlePreviewSegment = async () => {
    setPreviewingSegment(true);
    setNotice(null);
    setError(null);
    try {
      const response = await axios.post(endpoints.agentflow.campaignSegments.preview(tenantId), {
        filterJson: manualSegment.filterJson,
      });
      setSegmentPreview(response.data as SegmentPreview);
    } catch (err: any) {
      setError(err?.message ?? 'No fue posible previsualizar el segmento.');
    } finally {
      setPreviewingSegment(false);
    }
  };

  const handleCreateSegment = async () => {
    if (!manualSegment.name.trim()) {
      setError('El segmento manual necesita un nombre.');
      return;
    }

    setSavingSegment(true);
    setNotice(null);
    setError(null);
    try {
      await axios.post(endpoints.agentflow.campaignSegments.create(tenantId), {
        name: manualSegment.name,
        description: manualSegment.description,
        sourceModules: ['commerce', 'inbox', 'audit', 'threads'],
        filterJson: manualSegment.filterJson,
      });
      setManualSegment(defaultManualSegment());
      setSegmentPreview(null);
      setNotice('El segmento manual quedó guardado.');
      setActiveSection('segments');
      await refresh();
    } catch (err: any) {
      setError(err?.message ?? 'No fue posible crear el segmento manual.');
    } finally {
      setSavingSegment(false);
    }
  };

  const heroMeta = (
    <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
      <Chip size="small" color="success" label={`${overview.activeCampaigns} activas o publicadas`} />
      <Chip size="small" variant="outlined" label={`${overview.segments} segmentos reusables`} />
      <Chip size="small" variant="outlined" label={`${overview.runs} corridas recientes`} />
      <Chip size="small" color="warning" variant="outlined" label="Módulo nativo del producto" />
    </Stack>
  );

  return (
    <>
      <Helmet>
        <title>Campañas | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        {loading && <LinearProgress sx={{ mb: 2 }} />}

        <BrandPageHeader
          eyebrow="Operación comercial"
          title="Campañas"
          description="Programa salidas, mensajes, llamadas y arranques de workflow con segmentos del negocio. No requiere instalar nada desde integraciones."
          icon="mdi:bullhorn-variant-outline"
          meta={heroMeta}
          actions={
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
              <Button
                variant="contained"
                color="success"
                startIcon={buildingDraft ? <CircularProgress color="inherit" size={16} /> : <Iconify icon="mdi:auto-fix" width={18} />}
                onClick={handleDraft}
                disabled={buildingDraft || !prompt.trim()}
              >
                Generar desde prompt
              </Button>
              <Button variant="outlined" startIcon={<Iconify icon="mdi:refresh" width={18} />} onClick={refresh}>
                Actualizar
              </Button>
            </Stack>
          }
        />

        {error && <Alert severity="error" sx={{ mb: 2.5 }}>{error}</Alert>}
        {notice && <Alert severity="success" sx={{ mb: 2.5 }}>{notice}</Alert>}

        <Grid container spacing={2.5}>
          <Grid item xs={12} lg={3}>
            <Card variant="outlined" sx={{ borderRadius: 3, p: 1.25 }}>
              <List disablePadding>
                {sections.map((section) => (
                  <ListItemButton
                    key={section.key}
                    selected={activeSection === section.key}
                    onClick={() => setActiveSection(section.key)}
                    sx={{ borderRadius: 2, mb: 0.75, alignItems: 'flex-start' }}
                  >
                    <ListItemText
                      primary={section.label}
                      secondary={section.description}
                      primaryTypographyProps={{ variant: 'subtitle2', fontWeight: 700 }}
                      secondaryTypographyProps={{ variant: 'caption' }}
                    />
                  </ListItemButton>
                ))}
              </List>
            </Card>
          </Grid>

          <Grid item xs={12} lg={9}>
            <Stack spacing={2.5}>
              <Card variant="outlined" sx={{ borderRadius: 3, p: { xs: 2, md: 2.5 } }}>
                <Stack spacing={2}>
                  <Stack direction="row" justifyContent="space-between" alignItems="center" spacing={2} flexWrap="wrap" useFlexGap>
                    <Box>
                      <Typography variant="subtitle1">Creador asistido</Typography>
                      <Typography variant="body2" color="text.secondary">
                        Describe la campaña en lenguaje natural y deja que el asistente proponga un borrador editable.
                      </Typography>
                    </Box>
                    <Chip size="small" color="info" label="Asistido" />
                  </Stack>
                  <TextField
                    fullWidth
                    multiline
                    minRows={3}
                    label="Describe la campaña"
                    placeholder="Ejemplo: crea una campaña de cobro para clientes con factura vencida hace 3 días por WhatsApp y luego llamada."
                    value={prompt}
                    onChange={(event) => setPrompt(event.target.value)}
                  />
                  {draft && (
                    <Paper variant="outlined" sx={{ borderRadius: 2.5, p: 2, bgcolor: 'background.neutral' }}>
                      <Stack spacing={1.25}>
                        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                          <Typography variant="subtitle2">{draft.campaignDraft.name ?? 'Borrador de campaña'}</Typography>
                          <Chip size="small" color="info" label={draft.campaignDraft.channel ?? 'canal'} />
                          <Chip size="small" variant="outlined" label={draft.campaignDraft.executionMode ?? 'modo'} />
                          {draft.campaignDraft.runtimeModelProfileId && (
                            <Chip size="small" variant="outlined" label={`Runtime ${draft.campaignDraft.runtimeModelProfileId}`} />
                          )}
                        </Stack>
                        <Typography variant="body2" color="text.secondary">
                          {draft.campaignDraft.description}
                        </Typography>
                        <Typography variant="body2">
                          <strong>Mensaje sugerido:</strong> {draft.messageDraft ?? 'Sin mensaje sugerido'}
                        </Typography>
                        <Typography variant="body2">
                          <strong>Segmento sugerido:</strong> {draft.segmentDraft.name ?? 'Sin nombre'}
                        </Typography>
                        {draft.assumptions && draft.assumptions.length > 0 && (
                          <Typography variant="caption" color="text.secondary">
                            {draft.assumptions.join(' ')}
                          </Typography>
                        )}
                        {draft.warnings && draft.warnings.length > 0 && (
                          <Alert severity="warning">{draft.warnings.join(' ')}</Alert>
                        )}
                        <Stack direction="row" spacing={1}>
                          <Button
                            variant="contained"
                            color="success"
                            disabled={savingDraft}
                            startIcon={savingDraft ? <CircularProgress color="inherit" size={16} /> : <Iconify icon="mdi:content-save-outline" width={18} />}
                            onClick={handleSaveDraft}
                          >
                            Guardar borrador
                          </Button>
                        </Stack>
                      </Stack>
                    </Paper>
                  )}
                </Stack>
              </Card>

              {activeSection === 'summary' && (
                <Grid container spacing={2}>
                  <Grid item xs={12} md={6}>
                    <SummaryCard
                      title="Campañas configuradas"
                      value={overview.campaigns}
                      helper={`${overview.activeCampaigns} listas para salir o ya publicadas`}
                      tone="brand"
                    />
                  </Grid>
                  <Grid item xs={12} md={6}>
                    <SummaryCard
                      title="Segmentos reusables"
                      value={overview.segments}
                      helper="Audiencias listas para venta, cobro y recordatorios"
                    />
                  </Grid>
                  <Grid item xs={12} md={6}>
                    <SummaryCard
                      title="Corridas recientes"
                      value={overview.runs}
                      helper="Ejecuciones disparadas manualmente o por schedule"
                    />
                  </Grid>
                  <Grid item xs={12} md={6}>
                    <SummaryCard
                      title="Próximo foco"
                      value={scheduledItems[0]?.name ?? 'Sin programación'}
                      helper={
                        scheduledItems[0]?.nextRunAt
                          ? `Siguiente salida: ${formatDate(scheduledItems[0].nextRunAt)}`
                          : 'Publica una campaña con schedule para verla aquí.'
                      }
                    />
                  </Grid>
                </Grid>
              )}

              {activeSection === 'campaigns' && (
                <Stack spacing={2}>
                  <Card variant="outlined" sx={{ borderRadius: 3, p: { xs: 2, md: 2.5 } }}>
                    <Stack spacing={1.5}>
                      <Stack direction="row" justifyContent="space-between" alignItems="center" flexWrap="wrap" useFlexGap>
                        <Typography variant="subtitle1">Nueva campaña manual</Typography>
                        <Chip size="small" color="success" label="Manual" />
                      </Stack>
                      <Grid container spacing={2}>
                        <Grid item xs={12} md={6}>
                          <TextField fullWidth label="Nombre" value={manualCampaign.name} onChange={(e) => setManualCampaign((prev) => ({ ...prev, name: e.target.value }))} />
                        </Grid>
                        <Grid item xs={12} md={6}>
                          <TextField fullWidth label="Canal" value={manualCampaign.channel} onChange={(e) => setManualCampaign((prev) => ({ ...prev, channel: e.target.value }))} helperText="Ejemplo: whatsapp, voice, sms o email" />
                        </Grid>
                        <Grid item xs={12}>
                          <TextField fullWidth label="Descripción" value={manualCampaign.description} onChange={(e) => setManualCampaign((prev) => ({ ...prev, description: e.target.value }))} />
                        </Grid>
                        <Grid item xs={12} md={4}>
                          <TextField select fullWidth label="Tipo" value={manualCampaign.campaignType} onChange={(e) => setManualCampaign((prev) => ({ ...prev, campaignType: e.target.value }))}>
                            {campaignTypeOptions.map((option) => <MenuItem key={option} value={option}>{option}</MenuItem>)}
                          </TextField>
                        </Grid>
                        <Grid item xs={12} md={4}>
                          <TextField select fullWidth label="Modo de ejecución" value={manualCampaign.executionMode} onChange={(e) => setManualCampaign((prev) => ({ ...prev, executionMode: e.target.value }))}>
                            {executionModeOptions.map((option) => <MenuItem key={option} value={option}>{option}</MenuItem>)}
                          </TextField>
                        </Grid>
                        <Grid item xs={12} md={4}>
                          <TextField select fullWidth label="Acción del canal" value={manualCampaign.channelAction} onChange={(e) => setManualCampaign((prev) => ({ ...prev, channelAction: e.target.value }))}>
                            {channelActionOptions.map((option) => <MenuItem key={option} value={option}>{option}</MenuItem>)}
                          </TextField>
                        </Grid>
                        <Grid item xs={12} md={4}>
                          <TextField select fullWidth label="Schedule" value={manualCampaign.scheduleType} onChange={(e) => setManualCampaign((prev) => ({ ...prev, scheduleType: e.target.value }))}>
                            {scheduleTypeOptions.map((option) => <MenuItem key={option} value={option}>{option}</MenuItem>)}
                          </TextField>
                        </Grid>
                        <Grid item xs={12} md={4}>
                          <TextField fullWidth label="Expresión schedule" value={manualCampaign.scheduleExpression} onChange={(e) => setManualCampaign((prev) => ({ ...prev, scheduleExpression: e.target.value }))} helperText="Para Cron, Hourly, Daily o Weekly" />
                        </Grid>
                        <Grid item xs={12} md={4}>
                          <TextField fullWidth type="datetime-local" label="Inicio" InputLabelProps={{ shrink: true }} value={manualCampaign.startAt} onChange={(e) => setManualCampaign((prev) => ({ ...prev, startAt: e.target.value }))} />
                        </Grid>
                        <Grid item xs={12} md={6}>
                          <TextField select fullWidth label="Segmento guardado" value={manualCampaign.segmentId} onChange={(e) => setManualCampaign((prev) => ({ ...prev, segmentId: e.target.value }))} helperText="Opcional. También puedes usar filtro inline abajo.">
                            <MenuItem value="">Sin segmento enlazado</MenuItem>
                            {segments.map((segment) => <MenuItem key={segment.id} value={segment.id}>{segment.name}</MenuItem>)}
                          </TextField>
                        </Grid>
                        <Grid item xs={12} md={6}>
                          <TextField fullWidth label="WorkflowDefinitionId" value={manualCampaign.workflowDefinitionId} onChange={(e) => setManualCampaign((prev) => ({ ...prev, workflowDefinitionId: e.target.value }))} helperText="Úsalo cuando el modo sea Workflow o Hybrid." />
                        </Grid>
                        <Grid item xs={12} md={6}>
                          <TextField fullWidth label="RuntimeModelProfileId" value={manualCampaign.runtimeModelProfileId} onChange={(e) => setManualCampaign((prev) => ({ ...prev, runtimeModelProfileId: e.target.value }))} helperText="Opcional para campañas de voz o salidas especializadas." />
                        </Grid>
                        <Grid item xs={12} md={6}>
                          <TextField fullWidth label="Mensaje base" value={manualCampaign.messageDraft} onChange={(e) => setManualCampaign((prev) => ({ ...prev, messageDraft: e.target.value }))} />
                        </Grid>
                        <Grid item xs={12}>
                          <TextField fullWidth multiline minRows={3} label="Filtro de audiencia inline (JSON)" value={manualCampaign.audienceFilterJson} onChange={(e) => setManualCampaign((prev) => ({ ...prev, audienceFilterJson: e.target.value }))} />
                        </Grid>
                        <Grid item xs={12}>
                          <TextField fullWidth multiline minRows={3} label="Script de llamada" value={manualCampaign.callScriptDraft} onChange={(e) => setManualCampaign((prev) => ({ ...prev, callScriptDraft: e.target.value }))} />
                        </Grid>
                      </Grid>
                      <Stack direction="row" spacing={1}>
                        <Button variant="contained" color="success" onClick={handleCreateCampaign} disabled={savingCampaign}>
                          {savingCampaign ? 'Guardando...' : 'Crear campaña manual'}
                        </Button>
                        <Button variant="outlined" onClick={() => setManualCampaign(defaultManualCampaign())}>
                          Limpiar
                        </Button>
                      </Stack>
                    </Stack>
                  </Card>

                  <Card variant="outlined" sx={{ borderRadius: 3, p: { xs: 2, md: 2.5 } }}>
                    <Stack spacing={1.5}>
                      <Typography variant="subtitle1">Campañas guardadas</Typography>
                      {campaigns.length === 0 ? (
                        <Alert severity="info">Todavía no hay campañas creadas para este tenant.</Alert>
                      ) : (
                        campaigns.map((campaign) => (
                          <Paper key={campaign.id} variant="outlined" sx={{ borderRadius: 2.5, p: 2 }}>
                            <Stack spacing={1.25}>
                              <Stack direction="row" justifyContent="space-between" spacing={1} flexWrap="wrap" useFlexGap>
                                <Stack spacing={0.5}>
                                  <Typography variant="subtitle2">{campaign.name}</Typography>
                                  <Typography variant="caption" color="text.secondary">
                                    {campaign.description || 'Sin descripción'}
                                  </Typography>
                                </Stack>
                                <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                                  <Chip size="small" color={statusColor(campaign.status)} label={campaign.status} />
                                  <Chip size="small" variant="outlined" label={campaign.channel} />
                                  <Chip size="small" variant="outlined" label={campaign.executionMode} />
                                </Stack>
                              </Stack>
                              <Typography variant="body2" color="text.secondary">
                                Tipo {campaign.campaignType} - Acción {campaign.channelAction} - Actualizada {formatDate(campaign.updatedAt)}
                              </Typography>
                            </Stack>
                          </Paper>
                        ))
                      )}
                    </Stack>
                  </Card>
                </Stack>
              )}

              {activeSection === 'segments' && (
                <Stack spacing={2}>
                  <Card variant="outlined" sx={{ borderRadius: 3, p: { xs: 2, md: 2.5 } }}>
                    <Stack spacing={1.5}>
                      <Stack direction="row" justifyContent="space-between" alignItems="center" flexWrap="wrap" useFlexGap>
                        <Typography variant="subtitle1">Nuevo segmento manual</Typography>
                        <Chip size="small" color="success" label="Manual" />
                      </Stack>
                      <Grid container spacing={2}>
                        <Grid item xs={12} md={6}>
                          <TextField fullWidth label="Nombre" value={manualSegment.name} onChange={(e) => setManualSegment((prev) => ({ ...prev, name: e.target.value }))} />
                        </Grid>
                        <Grid item xs={12} md={6}>
                          <TextField fullWidth label="Descripción" value={manualSegment.description} onChange={(e) => setManualSegment((prev) => ({ ...prev, description: e.target.value }))} />
                        </Grid>
                        <Grid item xs={12}>
                          <TextField fullWidth multiline minRows={6} label="Filtro del segmento (JSON)" value={manualSegment.filterJson} onChange={(e) => setManualSegment((prev) => ({ ...prev, filterJson: e.target.value }))} helperText="Ejemplo: filtra por intención, compras, deuda o actividad reciente." />
                        </Grid>
                      </Grid>
                      <Stack direction="row" spacing={1}>
                        <Button variant="outlined" onClick={handlePreviewSegment} disabled={previewingSegment}>
                          {previewingSegment ? 'Calculando...' : 'Previsualizar audiencia'}
                        </Button>
                        <Button variant="contained" color="success" onClick={handleCreateSegment} disabled={savingSegment}>
                          {savingSegment ? 'Guardando...' : 'Guardar segmento'}
                        </Button>
                        <Button variant="outlined" onClick={() => { setManualSegment(defaultManualSegment()); setSegmentPreview(null); }}>
                          Limpiar
                        </Button>
                      </Stack>
                      {segmentPreview && (
                        <Paper variant="outlined" sx={{ borderRadius: 2.5, p: 2, bgcolor: 'background.neutral' }}>
                          <Stack spacing={1}>
                            <Typography variant="subtitle2">Preview del segmento</Typography>
                            <Typography variant="body2" color="text.secondary">
                              Contactos estimados: {segmentPreview.estimatedCount ?? 0}
                            </Typography>
                            <TextField
                              fullWidth
                              multiline
                              minRows={6}
                              label="Muestra de contactos"
                              value={prettyJson(segmentPreview.contacts ?? [])}
                              InputProps={{ readOnly: true }}
                            />
                          </Stack>
                        </Paper>
                      )}
                    </Stack>
                  </Card>

                  <Card variant="outlined" sx={{ borderRadius: 3, p: { xs: 2, md: 2.5 } }}>
                    <Stack spacing={1.5}>
                      <Typography variant="subtitle1">Segmentos guardados</Typography>
                      {segments.length === 0 ? (
                        <Alert severity="info">Todavía no hay segmentos guardados.</Alert>
                      ) : (
                        segments.map((segment) => (
                          <Paper key={segment.id} variant="outlined" sx={{ borderRadius: 2.5, p: 2 }}>
                            <Stack spacing={0.75}>
                              <Typography variant="subtitle2">{segment.name}</Typography>
                              <Typography variant="body2" color="text.secondary">
                                {segment.description || 'Sin descripción'}
                              </Typography>
                              <Typography variant="caption" color="text.secondary">
                                Alcance estimado: {segment.estimatedCount ?? 0} contactos - Actualizado {formatDate(segment.updatedAt)}
                              </Typography>
                            </Stack>
                          </Paper>
                        ))
                      )}
                    </Stack>
                  </Card>
                </Stack>
              )}

              {activeSection === 'calendar' && (
                <Card variant="outlined" sx={{ borderRadius: 3, p: { xs: 2, md: 2.5 } }}>
                  <Stack spacing={1.5}>
                    <Typography variant="subtitle1">Calendario operativo</Typography>
                    {scheduledItems.length === 0 ? (
                      <Alert severity="info">No hay campañas con siguiente corrida programada.</Alert>
                    ) : (
                      scheduledItems.map((item) => (
                        <Paper key={item.id} variant="outlined" sx={{ borderRadius: 2.5, p: 2 }}>
                          <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={1}>
                            <Box>
                              <Typography variant="subtitle2">{item.name}</Typography>
                              <Typography variant="body2" color="text.secondary">
                                {item.channel} - {item.executionMode} - {item.channelAction}
                              </Typography>
                            </Box>
                            <Chip color="info" label={item.nextRunAt ? formatDate(item.nextRunAt) : 'Sin próxima corrida'} />
                          </Stack>
                        </Paper>
                      ))
                    )}
                  </Stack>
                </Card>
              )}

              {activeSection === 'results' && (
                <Card variant="outlined" sx={{ borderRadius: 3, p: { xs: 2, md: 2.5 } }}>
                  <Stack spacing={1.5}>
                    <Typography variant="subtitle1">Resultados</Typography>
                    {runs.length === 0 ? (
                      <Alert severity="info">Todavía no hay corridas de campañas.</Alert>
                    ) : (
                      runs.map((run) => (
                        <Paper key={run.id} variant="outlined" sx={{ borderRadius: 2.5, p: 2 }}>
                          <Stack spacing={0.75}>
                            <Stack direction="row" justifyContent="space-between" spacing={1} flexWrap="wrap" useFlexGap>
                              <Typography variant="subtitle2">{run.id}</Typography>
                              <Chip size="small" color={statusColor(run.status)} label={run.status} />
                            </Stack>
                            <Typography variant="body2" color="text.secondary">
                              Campaña {run.campaignId} - Trigger {run.triggeredBy} - Inicio {formatDate(run.startedAt)}
                            </Typography>
                            {run.completedAt && (
                              <Typography variant="caption" color="text.secondary">
                                Finalizó {formatDate(run.completedAt)}
                              </Typography>
                            )}
                          </Stack>
                        </Paper>
                      ))
                    )}
                  </Stack>
                </Card>
              )}
            </Stack>
          </Grid>
        </Grid>
      </DashboardContent>
    </>
  );
}

function SummaryCard({
  title,
  value,
  helper,
  tone = 'soft',
}: {
  title: string;
  value: string | number;
  helper: string;
  tone?: 'brand' | 'soft';
}) {
  return (
    <Card
      variant="outlined"
      sx={(theme) => ({
        borderRadius: 3,
        p: 2,
        ...panelSurface(theme, tone),
      })}
    >
      <Stack spacing={1}>
        <Typography variant="overline" color="text.secondary">
          {title}
        </Typography>
        <Typography variant="h4">{value}</Typography>
        <Divider />
        <Typography variant="body2" color="text.secondary">
          {helper}
        </Typography>
      </Stack>
    </Card>
  );
}
