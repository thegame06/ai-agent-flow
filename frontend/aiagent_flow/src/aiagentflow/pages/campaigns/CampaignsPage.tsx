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

type SectionKey = 'summary' | 'campaigns' | 'segments' | 'calendar' | 'results';

const sections: Array<{ key: SectionKey; label: string; description: string }> = [
  { key: 'summary', label: 'Resumen', description: 'Vista operacional y creador asistido.' },
  { key: 'campaigns', label: 'Campañas', description: 'Definiciones activas, borradores y estado.' },
  { key: 'segments', label: 'Segmentos', description: 'Audiencias reusables basadas en ventas y cobros.' },
  { key: 'calendar', label: 'Calendario', description: 'Proximas ejecuciones programadas.' },
  { key: 'results', label: 'Resultados', description: 'Corridas recientes y trazabilidad.' },
];

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
        backgroundImage: `linear-gradient(135deg, ${alpha(theme.palette.success.dark, 0.5)} 0%, ${alpha(theme.palette.success.main, 0.18)} 100%)`,
        borderColor: alpha(theme.palette.success.main, 0.24),
      }
    : {
        backgroundColor: alpha(theme.palette.background.paper, 0.84),
        borderColor: alpha(theme.palette.divider, 0.18),
      };
}

export default function CampaignsPage() {
  const tenantId = useTenantId();
  const [activeSection, setActiveSection] = useState<SectionKey>('summary');
  const [prompt, setPrompt] = useState('');
  const [draft, setDraft] = useState<BuilderDraft | null>(null);
  const [campaigns, setCampaigns] = useState<CampaignItem[]>([]);
  const [segments, setSegments] = useState<SegmentItem[]>([]);
  const [runs, setRuns] = useState<RunItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [savingDraft, setSavingDraft] = useState(false);
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

  const heroMeta = (
    <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
      <Chip size="small" color="success" label={`${overview.activeCampaigns} activas o publicadas`} />
      <Chip size="small" variant="outlined" label={`${overview.segments} segmentos reusables`} />
      <Chip size="small" variant="outlined" label={`${overview.runs} corridas recientes`} />
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
          eyebrow="Operacion orquestada"
          title="Campañas"
          description="Programa mensajes, arranques de workflow y seguimiento saliente con segmentos creados desde ventas, cobros y conversaciones."
          icon="mdi:bullhorn-variant-outline"
          meta={heroMeta}
          actions={
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
              <Button
                variant="contained"
                startIcon={buildingDraft ? <CircularProgress color="inherit" size={16} /> : <Iconify icon="mdi:auto-fix" width={18} />}
                onClick={handleDraft}
                disabled={buildingDraft || !prompt.trim()}
              >
                Generar desde prompt
              </Button>
              <Button
                variant="outlined"
                startIcon={<Iconify icon="mdi:refresh" width={18} />}
                onClick={refresh}
              >
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
                  <Typography variant="subtitle1">Creador asistido</Typography>
                  <TextField
                    fullWidth
                    multiline
                    minRows={3}
                    label="Describe la campaña"
                    placeholder="Ejemplo: Crea una campaña de cobro para clientes con factura vencida hace 3 días por WhatsApp y luego llamada."
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
                          {draft.campaignDraft.runtimeModelProfileId && <Chip size="small" variant="outlined" label={`runtime: ${draft.campaignDraft.runtimeModelProfileId}`} />}
                        </Stack>
                        <Typography variant="body2" color="text.secondary">
                          {draft.campaignDraft.description}
                        </Typography>
                        <Typography variant="body2">
                          <strong>Mensaje sugerido:</strong> {draft.messageDraft ?? 'Sin mensaje sugerido'}
                        </Typography>
                        <Typography variant="body2">
                          <strong>Segmento:</strong> {draft.segmentDraft.name ?? 'Sin nombre'}.
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
                      helper={`${overview.activeCampaigns} listas para correr o ya activas`}
                      tone="brand"
                    />
                  </Grid>
                  <Grid item xs={12} md={6}>
                    <SummaryCard
                      title="Segmentos reusables"
                      value={overview.segments}
                      helper="Audiencias disponibles para venta, cobro y recordatorios"
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
                      title="Proximo foco"
                      value={scheduledItems[0]?.name ?? 'Sin schedule'}
                      helper={scheduledItems[0]?.nextRunAt ? `Siguiente salida: ${new Date(scheduledItems[0].nextRunAt).toLocaleString()}` : 'Publica una campaña con schedule para verla aqui.'}
                    />
                  </Grid>
                </Grid>
              )}

              {activeSection === 'campaigns' && (
                <Card variant="outlined" sx={{ borderRadius: 3, p: { xs: 2, md: 2.5 } }}>
                  <Stack spacing={1.5}>
                    <Typography variant="subtitle1">Campañas</Typography>
                    {campaigns.length === 0 ? (
                      <Alert severity="info">Todavia no hay campañas creadas para este tenant.</Alert>
                    ) : (
                      campaigns.map((campaign) => (
                        <Paper key={campaign.id} variant="outlined" sx={{ borderRadius: 2.5, p: 2 }}>
                          <Stack spacing={1.25}>
                            <Stack direction="row" justifyContent="space-between" spacing={1} flexWrap="wrap" useFlexGap>
                              <Stack spacing={0.5}>
                                <Typography variant="subtitle2">{campaign.name}</Typography>
                                <Typography variant="caption" color="text.secondary">
                                  {campaign.description || 'Sin descripcion'}
                                </Typography>
                              </Stack>
                              <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                                <Chip size="small" color={statusColor(campaign.status)} label={campaign.status} />
                                <Chip size="small" variant="outlined" label={campaign.channel} />
                                <Chip size="small" variant="outlined" label={campaign.executionMode} />
                              </Stack>
                            </Stack>
                            <Typography variant="body2" color="text.secondary">
                              Tipo {campaign.campaignType} · Accion {campaign.channelAction} · Actualizada {new Date(campaign.updatedAt).toLocaleString()}
                            </Typography>
                          </Stack>
                        </Paper>
                      ))
                    )}
                  </Stack>
                </Card>
              )}

              {activeSection === 'segments' && (
                <Card variant="outlined" sx={{ borderRadius: 3, p: { xs: 2, md: 2.5 } }}>
                  <Stack spacing={1.5}>
                    <Typography variant="subtitle1">Segmentos</Typography>
                    {segments.length === 0 ? (
                      <Alert severity="info">Todavia no hay segmentos guardados.</Alert>
                    ) : (
                      segments.map((segment) => (
                        <Paper key={segment.id} variant="outlined" sx={{ borderRadius: 2.5, p: 2 }}>
                          <Stack spacing={0.75}>
                            <Typography variant="subtitle2">{segment.name}</Typography>
                            <Typography variant="body2" color="text.secondary">
                              {segment.description || 'Sin descripcion'}
                            </Typography>
                            <Typography variant="caption" color="text.secondary">
                              Alcance estimado: {segment.estimatedCount ?? 0} contactos · Actualizado {new Date(segment.updatedAt).toLocaleString()}
                            </Typography>
                          </Stack>
                        </Paper>
                      ))
                    )}
                  </Stack>
                </Card>
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
                                {item.channel} · {item.executionMode} · {item.channelAction}
                              </Typography>
                            </Box>
                            <Chip color="info" label={item.nextRunAt ? new Date(item.nextRunAt).toLocaleString() : 'Sin proxima corrida'} />
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
                      <Alert severity="info">Todavia no hay corridas de campañas.</Alert>
                    ) : (
                      runs.map((run) => (
                        <Paper key={run.id} variant="outlined" sx={{ borderRadius: 2.5, p: 2 }}>
                          <Stack spacing={0.75}>
                            <Stack direction="row" justifyContent="space-between" spacing={1} flexWrap="wrap" useFlexGap>
                              <Typography variant="subtitle2">{run.id}</Typography>
                              <Chip size="small" color={statusColor(run.status)} label={run.status} />
                            </Stack>
                            <Typography variant="body2" color="text.secondary">
                              Campaña {run.campaignId} · Trigger {run.triggeredBy} · Inicio {new Date(run.startedAt).toLocaleString()}
                            </Typography>
                            {run.completedAt && (
                              <Typography variant="caption" color="text.secondary">
                                Finalizo {new Date(run.completedAt).toLocaleString()}
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
