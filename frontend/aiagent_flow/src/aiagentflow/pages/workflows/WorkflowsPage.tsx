import { Helmet } from 'react-helmet-async';
import { useSearchParams } from 'react-router';
import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Tabs from '@mui/material/Tabs';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Badge from '@mui/material/Badge';
import Button from '@mui/material/Button';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { TermHelp } from 'src/aiagentflow/components/TermHelp';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { BrandPageHeader } from 'src/aiagentflow/components/BrandPageHeader';

import { Iconify } from 'src/components/iconify';

import { RuntimeMetricsCard } from './components/RuntimeMetricsCard';
import { AiAgentConfigDialog } from './components/AiAgentConfigDialog';
import { useWorkflowEditorState } from './hooks/useWorkflowEditorState';
import { ExecutionStepsDialog } from './components/ExecutionStepsDialog';
import { useWorkflowStudioRuntime } from './hooks/useWorkflowStudioRuntime';
import { WorkflowExecutionsCard } from './components/WorkflowExecutionsCard';
import { WorkflowVisualDesigner } from './components/WorkflowVisualDesigner';
import { TOOL_ACTIVITY_TYPES, WORKFLOW_QUICKSTARTS, type WorkflowDesignType } from './constants';

import type { WorkflowDefinition, WorkflowStartIntent } from './types';

type ActivePanel = 'none' | 'flows' | 'templates' | 'metrics' | 'executions';

const SYSTEM_EVENT_OPTIONS = [
  {
    value: 'connect.message.received',
    label: 'Mensaje recibido',
    helper: 'Lo dispara un canal cuando llega un mensaje del cliente.',
  },
  {
    value: 'connect.call.received',
    label: 'Llamada recibida',
    helper: 'Lo dispara un canal de voz o call center cuando entra una llamada.',
  },
  {
    value: 'connect.campaign.triggered',
    label: 'Campana iniciada',
    helper: 'Lo dispara una campana saliente o una regla programada.',
  },
  {
    value: 'payment.status.changed',
    label: 'Pago actualizado',
    helper: 'Lo dispara pagos cuando cambia el estado de una intencion de pago.',
  },
];

const defaultStartIntents = (eventName: string): WorkflowStartIntent[] => [
  {
    id: 'intent-main',
    label: 'Intencion principal',
    description: 'Frases o eventos que deben iniciar este flujo.',
    examples: ['Quiero informacion', 'Necesito ayuda'],
    eventName,
    triggerSource: 'message',
    confidenceThreshold: 0.7,
  },
];

const readStartIntents = (definitionJson: string, eventName: string): WorkflowStartIntent[] => {
  try {
    const parsed = JSON.parse(definitionJson) as { start?: { intents?: WorkflowStartIntent[] } };
    return parsed.start?.intents?.length ? parsed.start.intents : defaultStartIntents(eventName);
  } catch {
    return defaultStartIntents(eventName);
  }
};

const writeStartIntents = (definitionJson: string, intents: WorkflowStartIntent[]) => {
  try {
    const parsed = JSON.parse(definitionJson) as Record<string, any>;
    parsed.start = { ...(parsed.start ?? {}), intents };
    return JSON.stringify(parsed, null, 2);
  } catch {
    return JSON.stringify({ start: { intents }, activities: [] }, null, 2);
  }
};

const slugifyIntentKey = (value: string) =>
  value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/_+/g, '_')
    .replace(/^_+|_+$/g, '');

export default function WorkflowsPage() {
  const [editorMode, setEditorMode] = useState<'builder' | 'advanced'>('builder');
  const [designType, setDesignType] = useState<WorkflowDesignType>('workflow');
  const [mainTab, setMainTab] = useState(0);
  const [activePanel, setActivePanel] = useState<ActivePanel>('none');
  const [workflowSearch, setWorkflowSearch] = useState('');
  const [intentProbe, setIntentProbe] = useState('Quiero agendar una cita por WhatsApp');
  const [intentProbeResult, setIntentProbeResult] = useState<any>(null);
  const [syncingIntents, setSyncingIntents] = useState(false);
  const [probingIntent, setProbingIntent] = useState(false);
  const [syncMessage, setSyncMessage] = useState<string | null>(null);
  const [routingRules, setRoutingRules] = useState<any[]>([]);
  const tenantId = useTenantId();
  const [searchParams] = useSearchParams();
  const runtimeQueryKind = searchParams.get('runtimeKind');
  const runtimeStorageKey = `af:workflow:runtimeKind:${tenantId}`;
  const resolveRuntimeFromString = (value?: string | null): 'Text' | 'Voice' | 'MultimodalRealtime' | null => {
    if (!value) return null;
    const normalized = value.toLowerCase();
    if (normalized === 'text') return 'Text';
    if (normalized === 'voice') return 'Voice';
    if (normalized === 'multimodal' || normalized === 'multimodalrealtime') return 'MultimodalRealtime';
    return null;
  };
  const initialRuntime = resolveRuntimeFromString(runtimeQueryKind)
    ?? (typeof window !== 'undefined' ? resolveRuntimeFromString(localStorage.getItem(runtimeStorageKey)) : null)
    ?? 'Text';
  const [selectedRuntimeKind, setSelectedRuntimeKind] = useState<'Text' | 'Voice' | 'MultimodalRealtime'>(
    initialRuntime
  );
  const [metricsWindow, setMetricsWindow] = useState<'24h' | '7d' | '30d'>('24h');
  useEffect(() => {
    const resolved = resolveRuntimeFromString(runtimeQueryKind);
    if (resolved) setSelectedRuntimeKind(resolved);
  }, [runtimeQueryKind]);
  useEffect(() => {
    if (typeof window === 'undefined') return;
    localStorage.setItem(runtimeStorageKey, selectedRuntimeKind);
  }, [runtimeStorageKey, selectedRuntimeKind]);
  const {
    saving,
    running,
    error,
    workflows,
    executions,
    steps,
    stepsOpen,
    metrics,
    wizardMetrics,
    auditEvents,
    activityCatalog,
    availableModels,
    availableTools,
    availableAgents,
    availableChannels,
    integrations,
    connectTemplates,
    runtimeProfiles,
    setError,
    setStepsOpen,
    loadAll,
    saveWorkflow,
    publishWorkflow,
    runEvent,
    retryExecution,
    openSteps,
  } = useWorkflowStudioRuntime(tenantId, metricsWindow);
  const scopedWorkflows = useMemo(
    () =>
      workflows.filter(
        (workflow) => (workflow.runtimeKind ?? 'Text').toLowerCase() === selectedRuntimeKind.toLowerCase()
      ),
    [selectedRuntimeKind, workflows]
  );
  const scopedRuntimeProfiles = useMemo(
    () =>
      (runtimeProfiles ?? []).filter(
        (profile: any) => (profile.runtimeKind ?? 'Text').toLowerCase() === selectedRuntimeKind.toLowerCase()
      ),
    [runtimeProfiles, selectedRuntimeKind]
  );
  const [selectedRuntimeProfileId, setSelectedRuntimeProfileId] = useState<string>('');

  const {
    editor,
    activities,
    selectedWorkflowId,
    hasSelection,
    allowedTypes,
    requiredConfigByType,
    validationErrors,
    aiDialogOpen,
    aiTab,
    aiTarget,
    setAiTab,
    setEditorField,
    setDefinitionJson,
    selectWorkflow,
    createNew,
    addActivity,
    updateActivity,
    removeActivity,
    addActivityConfig,
    updateActivityConfig,
    removeActivityConfig,
    openAiConfig,
    closeAiConfig,
    updateAiAgentConfig,
    updateAiAgentConfigAt,
    setSelectedWorkflowId,
  } = useWorkflowEditorState(activityCatalog);

  const uiAllowedTypes = (
    designType === 'tool' ? allowedTypes.filter((t) => TOOL_ACTIVITY_TYPES.includes(t as any)) : allowedTypes
  ).filter((type) => !type.startsWith('kyc.'));
  const quickstarts =
    designType === 'tool'
      ? WORKFLOW_QUICKSTARTS.filter((q) => q.id.includes('payment'))
      : WORKFLOW_QUICKSTARTS.filter((q) => !q.id.includes('kyc'));
  const designValidationErrors = [
    ...validationErrors,
    ...(designType === 'tool'
      ? activities
          .filter((a) => !TOOL_ACTIVITY_TYPES.includes(a.type as any))
          .map((a) => `El nodo ${a.id || a.type} no esta permitido en modo herramienta tecnica.`)
      : []),
  ];
  const publishedCount = scopedWorkflows.filter((wf) => wf.status === 'Published').length;
  const failedExecutions = executions.filter((execution) => execution.status === 'Failed').length;
  const readyToPublish = hasSelection && designValidationErrors.length === 0;
  const selectedWorkflow = useMemo(
    () => scopedWorkflows.find((wf) => wf.id === selectedWorkflowId) ?? null,
    [selectedWorkflowId, scopedWorkflows]
  );
  useEffect(() => {
    if (!selectedWorkflow) {
      setSelectedRuntimeProfileId('');
      return;
    }
    const profileId = selectedWorkflow.metadata?.runtimeModelProfileId ?? '';
    setSelectedRuntimeProfileId(profileId);
  }, [selectedWorkflow]);
  const filteredWorkflows = useMemo(
    () =>
      scopedWorkflows.filter((workflow) =>
        `${workflow.name} ${workflow.id} ${workflow.triggerEventName}`
          .toLowerCase()
          .includes(workflowSearch.toLowerCase())
      ),
    [workflowSearch, scopedWorkflows]
  );
  const startIntents = useMemo(
    () => readStartIntents(editor.definitionJson, editor.triggerEventName),
    [editor.definitionJson, editor.triggerEventName]
  );
  const workflowChannel = useMemo(() => {
    const context = [
      editor.triggerEventName,
      ...startIntents.flatMap((intent) => [intent.label, intent.description, intent.eventName, ...(intent.examples ?? [])]),
    ]
      .join(' ')
      .toLowerCase();
    const activeChannels = availableChannels.filter((channel) => channel.status === 'Active');
    return (
      activeChannels.find((channel) => context.includes(channel.type.toLowerCase())) ??
      activeChannels.find((channel) => context.includes(channel.name.toLowerCase())) ??
      activeChannels[0] ??
      availableChannels[0] ??
      null
    );
  }, [availableChannels, editor.triggerEventName, startIntents]);
  const firstAgentNode = useMemo(
    () => activities.find((activity) => activity.type === 'ai.agent' && (activity.config?.agentId || activity.aiAgent?.agentId)),
    [activities]
  );
  const hasAiAgentNode = activities.some((activity) => activity.type === 'ai.agent');
  const completedSetupSteps = [
    Boolean(editor.name?.trim()),
    Boolean(editor.triggerEventName?.trim()),
    startIntents.length > 0,
    activities.length > 0,
    hasAiAgentNode,
    designValidationErrors.length === 0,
  ].filter(Boolean).length;
  const setupPercent = Math.round((completedSetupSteps / 6) * 100);

  const associatedRoutingIntents = useMemo(
    () => routingRules.filter((rule) => String(rule.workflowDefinitionId || '') === String(editor.id || '')),
    [routingRules, editor.id]
  );

  useEffect(() => {
    axios.get(endpoints.agentflow.intentRouting.rules(tenantId))
      .then((res) => setRoutingRules(res.data || []))
      .catch(() => setRoutingRules([]));
  }, [tenantId, workflows.length]);
  const workflowReadiness = (workflow: WorkflowDefinition) => {
    const intents = readStartIntents(workflow.definitionJson, workflow.triggerEventName);
    let activitiesInWorkflow: Array<{ type?: string; aiAgent?: { agentId?: string }; config?: Record<string, string> }> = [];
    try {
      const parsed = JSON.parse(workflow.definitionJson) as {
        activities?: Array<{ type?: string; aiAgent?: { agentId?: string }; config?: Record<string, string> }>;
      };
      activitiesInWorkflow = parsed.activities ?? [];
    } catch {
      activitiesInWorkflow = [];
    }

    const hasAgent = activitiesInWorkflow.some((activity) => activity.type === 'ai.agent');
    const score = [workflow.name, workflow.triggerEventName, intents.length > 0, activitiesInWorkflow.length > 0, hasAgent]
      .filter(Boolean).length;

    return {
      intents,
      activitiesInWorkflow,
      hasAgent,
      percent: Math.round((score / 5) * 100),
    };
  };

  const handleSelectWorkflow = (wf: WorkflowDefinition) => {
    selectWorkflow(wf);
    setActivePanel('none');
    setMainTab(0);
  };

  const handleCreateBlank = () => {
    setSelectedWorkflowId(null);
    setEditorField('id', `wf_${Date.now()}`);
    setEditorField('name', 'Nuevo flujo');
    setEditorField('triggerEventName', selectedRuntimeKind === 'Voice' ? 'connect.call.received' : 'connect.message.received');
    setDefinitionJson(JSON.stringify({ activities: [] }, null, 2));
    setActivePanel('none');
    setMainTab(0);
  };

  const handleCreateDefault = () => {
    setSelectedWorkflowId(null);
    createNew();
    setActivePanel('none');
    setMainTab(0);
  };

  const handleUseTemplate = (tpl: (typeof WORKFLOW_QUICKSTARTS)[number]) => {
    setSelectedWorkflowId(null);
    setEditorField('id', tpl.id);
    setEditorField('name', tpl.name);
    setEditorField('triggerEventName', tpl.triggerEventName);
    setDefinitionJson(tpl.definitionJson);
    setActivePanel('none');
    setMainTab(0);
  };

  const updateStartIntents = (intents: WorkflowStartIntent[]) => {
    setDefinitionJson(
      writeStartIntents(
        editor.definitionJson,
        intents.map((intent) => ({ ...intent, eventName: editor.triggerEventName }))
      )
    );
  };

  const syncIntentsToRouting = async () => {
    const sourceAgentId = workflowChannel?.routerAgentId || workflowChannel?.defaultAgentId || workflowChannel?.intentAgents?.[0] || workflowChannel?.routingAgents?.[0] || '';
    const targetAgentId = firstAgentNode?.config?.agentId || firstAgentNode?.aiAgent?.agentId || sourceAgentId;
    const channel = workflowChannel?.type?.toLowerCase();

    if (!sourceAgentId || !targetAgentId) {
      setSyncMessage(null);
      setError('No se pudieron sincronizar motivos: configura un canal con asistente asignado y un nodo de asistente.');
      return;
    }

    try {
      setSyncingIntents(true);
      await Promise.all(
        startIntents.map((intent, index) =>
          axios.post(endpoints.agentflow.intentRouting.rules(tenantId), {
            id: `brain-${editor.id || 'draft'}-${intent.id}`,
            intentKey: slugifyIntentKey(intent.label || intent.id),
            intentDescription: intent.description ?? '',
            examplePhrases: intent.examples ?? [],
            sourceAgentId,
            targetAgentId,
            workflowDefinitionId: editor.id,
            workflowName: editor.name,
            priority: 100 + index,
            enabled: true,
            channel,
            conditionsJson: JSON.stringify({
              workflowId: editor.id,
              workflowName: editor.name,
              eventName: editor.triggerEventName,
              examples: intent.examples ?? [],
              description: intent.description ?? '',
              triggerSource: intent.triggerSource ?? 'message',
              confidenceThreshold: intent.confidenceThreshold ?? 0.7,
            }),
            handoffPolicyJson: JSON.stringify({ source: 'brain-studio' }),
          })
        )
      );
      setError(null);
      setSyncMessage(`${startIntents.length} intencion(es) sincronizadas con el enrutamiento del canal.`);
    } catch (err: any) {
      setSyncMessage(null);
      setError(err?.message || 'No se pudieron sincronizar los motivos con las reglas de enrutamiento.');
    } finally {
      setSyncingIntents(false);
    }
  };

  const simulateCurrentIntent = async () => {
    const sourceAgentId = workflowChannel?.routerAgentId || workflowChannel?.defaultAgentId || workflowChannel?.intentAgents?.[0] || workflowChannel?.routingAgents?.[0] || '';

    if (!sourceAgentId) {
      setError('No se puede probar el motivo: selecciona un canal con asistente principal o asistente de enrutamiento.');
      return;
    }

    try {
      setProbingIntent(true);
      const res = await axios.post(endpoints.agentflow.intentRouting.simulate(tenantId), {
        sourceAgentId,
        intent: intentProbe,
        channel: workflowChannel?.type?.toLowerCase(),
      });
      setIntentProbeResult(res.data);
      setError(null);
    } catch (err: any) {
      setIntentProbeResult(null);
      setError(err?.message || 'No se pudo simular la decision de enrutamiento.');
    } finally {
      setProbingIntent(false);
    }
  };

  return (
    <>
      <Helmet>
        <title>Automatizaciones | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        {error && (
          <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
            {error}
          </Alert>
        )}


        <BrandPageHeader
          eyebrow="Construccion operativa"
          title="Automatizaciones"
          description="Disena el recorrido completo del caso: entrada del cliente, validaciones, respuestas, pagos, revision humana y cierre."
          icon="mdi:source-branch"
          help={<TermHelp title="Flujo automatizado es la secuencia de pasos que el sistema ejecuta para atender un caso de principio a fin." />}
          meta={
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
              <Chip size="small" color="info" label={`Modalidad ${selectedRuntimeKind}`} variant="outlined" />
              <ToggleButtonGroup
                size="small"
                value={selectedRuntimeKind}
                exclusive
                onChange={(_, value) => value && setSelectedRuntimeKind(value)}
              >
                <ToggleButton value="Text">Texto</ToggleButton>
                <ToggleButton value="Voice">Voz</ToggleButton>
                <ToggleButton value="MultimodalRealtime">Multimodal</ToggleButton>
              </ToggleButtonGroup>
            </Stack>
          }
          actions={
            <Stack direction="row" spacing={1} flexWrap="wrap">
              <Button variant="contained" startIcon={<Iconify icon="mingcute:add-line" />} onClick={handleCreateBlank}>
                Crear automatizacion
              </Button>
              <Button variant="outlined" startIcon={<Iconify icon="mdi:refresh" />} onClick={loadAll}>
                Actualizar
              </Button>
            </Stack>
          }
        />

        <Card variant="outlined" sx={{ p: 0.75, mb: 2.5, borderRadius: 3 }}>
          <Tabs
            value={mainTab}
            onChange={(_, v) => setMainTab(v)}
            variant="scrollable"
            allowScrollButtonsMobile
            sx={{ px: { xs: 0.5, md: 1 } }}
          >
          <Tab
            label="Diseno"
            icon={<Iconify icon="mdi:pencil-ruler" width={18} />}
            iconPosition="start"
          />
          <Tab
            label={
              <Badge badgeContent={scopedWorkflows.length} color="primary" max={99}>
                <Box sx={{ pr: 1.5 }}>Mis automatizaciones</Box>
              </Badge>
            }
            icon={<Iconify icon="mdi:folder-multiple-outline" width={18} />}
            iconPosition="start"
          />
          <Tab
            label={
              <Badge badgeContent={failedExecutions || undefined} color="error" max={9}>
                <Box sx={{ pr: failedExecutions ? 1.5 : 0 }}>Seguimiento</Box>
                
              </Badge>
            }
            icon={<Iconify icon="mdi:chart-box-outline" width={18} />}
            iconPosition="start"
          />
          </Tabs>
        </Card>

        {mainTab === 0 && (
          <>
            {!hasSelection && (
              <Card variant="outlined" sx={{ p: { xs: 3, md: 4 }, textAlign: 'center', mb: 2.5, borderRadius: 3 }}>
                <Iconify icon="mdi:graph-outline" width={48} sx={{ color: 'primary.main', mb: 1.5 }} />
                <Typography variant="h5" sx={{ mb: 0.5 }}>Sin flujo activo</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5, maxWidth: 480, mx: 'auto' }}>
                  Selecciona un flujo existente en &quot;Mis automatizaciones&quot; o crea uno nuevo desde cero.
                </Typography>
                <Stack direction="row" spacing={1.5} justifyContent="center" flexWrap="wrap">
                  <Button variant="contained" startIcon={<Iconify icon="mingcute:add-line" />} onClick={handleCreateBlank}>
                    Crear desde cero
                  </Button>
                  <Button variant="outlined" onClick={handleCreateDefault}>
                    Base de WhatsApp
                  </Button>
                  {quickstarts.slice(0, 3).map((tpl) => (
                    <Button key={tpl.id} variant="outlined" onClick={() => handleUseTemplate(tpl)}>
                      {tpl.name}
                    </Button>
                  ))}
                  <Button variant="text" startIcon={<Iconify icon="mdi:folder-open-outline" />} onClick={() => setMainTab(1)}>
                    Ver mis automatizaciones
                  </Button>
                </Stack>
              </Card>
            )}

            {hasSelection && (
        <Card variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, mb: 2.5, borderRadius: 3 }}>
          <Stack spacing={2}>
            <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" spacing={1.5} alignItems={{ lg: 'center' }}>
              <Stack direction="row" spacing={0.8} flexWrap="wrap" alignItems="center">
                <Chip size="small" color={selectedWorkflow?.status === 'Published' ? 'success' : 'default'} label={selectedWorkflow?.status === 'Published' ? 'Publicado' : 'Borrador'} />
                <Chip size="small" color={readyToPublish ? 'success' : 'warning'} label={`${setupPercent}% listo`} />
                <Chip size="small" label={`${startIntents.length} motivos del flujo`} />
                <Chip size="small" color="info" label={`${associatedRoutingIntents.length} intenciones asociadas`} />
                <Chip size="small" label={workflowChannel ? workflowChannel.name : 'Sin canal'} />
                <Chip size="small" color={hasAiAgentNode ? 'primary' : 'default'} label={hasAiAgentNode ? 'Asistente listo' : 'Sin asistente'} />
              </Stack>
              <Stack direction="row" spacing={1} flexWrap="wrap">
                <Button
                  size="small"
                  variant={activePanel === 'templates' ? 'contained' : 'outlined'}
                  startIcon={<Iconify icon="mingcute:add-line" />}
                  onClick={() => setActivePanel(activePanel === 'templates' ? 'none' : 'templates')}
                >
                  Plantillas
                </Button>
                <Button
                  size="small"
                  variant="contained"
                  onClick={() => saveWorkflow({ ...editor, designType, runtimeKind: selectedRuntimeKind, runtimeModelProfileId: selectedRuntimeProfileId || null }, designValidationErrors)}
                  disabled={saving || !editor.id}
                  startIcon={<Iconify icon="mdi:content-save-outline" />}
                >
                  {saving ? 'Guardando...' : 'Guardar'}
                </Button>
                <Button
                  size="small"
                  variant="outlined"
                  onClick={() => publishWorkflow(editor.id, hasSelection, designValidationErrors, { definitionJson: editor.definitionJson, runtimeKind: selectedRuntimeKind, triggerEventName: editor.triggerEventName })}
                  disabled={!hasSelection || designValidationErrors.length > 0}
                  startIcon={<Iconify icon="mdi:cloud-upload-outline" />}
                >
                  Publicar
                </Button>
                <Button
                  size="small"
                  variant="outlined"
                  color="success"
                  onClick={() => runEvent(editor.triggerEventName)}
                  disabled={running}
                  startIcon={<Iconify icon="mdi:play-outline" />}
                >
                  {running ? 'Probando...' : 'Probar'}
                </Button>
              </Stack>
            </Stack>

            {syncMessage && (
              <Alert severity="success" onClose={() => setSyncMessage(null)}>{syncMessage}</Alert>
            )}

            <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.2} alignItems={{ md: 'center' }}>
              <TextField
                label="Nombre del flujo"
                value={editor.name}
                onChange={(e) => setEditorField('name', e.target.value)}
                size="small"
                sx={{ flex: 1, minWidth: 200 }}
              />
                <TextField
                  label="Perfil de modelos runtime"
                  select
                  value={selectedRuntimeProfileId}
                  onChange={(e) => setSelectedRuntimeProfileId(e.target.value)}
                  size="small"
                  sx={{ minWidth: 260 }}
                >
                  <MenuItem value="">Default del runtime</MenuItem>
                  {scopedRuntimeProfiles.map((profile: any) => (
                    <MenuItem key={profile.id} value={profile.id}>
                      {profile.name} ({profile.id})
                    </MenuItem>
                  ))}
                </TextField>
                <TextField
                  label="Que activa este flujo"
                  select
                  value={editor.triggerEventName}
                  onChange={(e) => setEditorField('triggerEventName', e.target.value)}
                  size="small"
                  sx={{ minWidth: 260 }}
              >
                {SYSTEM_EVENT_OPTIONS.map((opt) => (
                  <MenuItem key={opt.value} value={opt.value}>
                    <Box>
                      <Typography variant="body2">{opt.label}</Typography>
                      <Typography variant="caption" color="text.secondary">{opt.helper}</Typography>
                    </Box>
                  </MenuItem>
                ))}
              </TextField>
              <ToggleButtonGroup
                value={designType}
                exclusive
                size="small"
                onChange={(_, v) => { if (v) setDesignType(v); }}
              >
                <ToggleButton value="workflow">Flujo</ToggleButton>
                <ToggleButton value="tool">Herramienta</ToggleButton>
              </ToggleButtonGroup>
              <ToggleButtonGroup
                value={editorMode}
                exclusive
                size="small"
                onChange={(_, v) => { if (v) setEditorMode(v); }}
              >
                <ToggleButton value="builder">Visual</ToggleButton>
                <ToggleButton value="advanced">JSON</ToggleButton>
              </ToggleButtonGroup>
            </Stack>

            <Card variant="outlined" sx={{ p: 1.5, bgcolor: 'background.neutral' }}>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.2} alignItems={{ md: 'center' }}>
                <Box sx={{ flex: 1 }}>
                  <Typography variant="caption" color="text.secondary">
                    Entrada principal: <strong>{workflowChannel?.name ?? 'sin canal'}</strong>
                    {' | '}Asistente responsable: <strong>{firstAgentNode?.aiAgent?.agentName || firstAgentNode?.config?.agentName || firstAgentNode?.config?.agentId || 'sin asistente'}</strong>
                  </Typography>
                </Box>
                <TextField
                  label="Probar mensaje del cliente"
                  value={intentProbe}
                  onChange={(e) => setIntentProbe(e.target.value)}
                  size="small"
                  sx={{ minWidth: { md: 300 } }}
                />
                <Button
                  variant="outlined"
                  size="small"
                  onClick={simulateCurrentIntent}
                  disabled={probingIntent}
                  startIcon={<Iconify icon="mdi:radar" />}
                >
                  {probingIntent ? 'Probando...' : 'Probar decision'}
                </Button>
                <Button
                  variant="outlined"
                  size="small"
                  onClick={syncIntentsToRouting}
                  disabled={syncingIntents}
                  startIcon={<Iconify icon="mdi:sync" />}
                >
                  {syncingIntents ? 'Sincronizando...' : 'Publicar motivos'}
                </Button>
              </Stack>
              {intentProbeResult && (
                <Alert severity={intentProbeResult?.matchedRuleId ? 'success' : 'warning'} sx={{ mt: 1 }}>
                  Regla aplicada: {intentProbeResult?.matchedRuleId ?? 'sin coincidencia'} | Destino:{' '}
                  {intentProbeResult?.selectedAgentId ?? 'N/A'} | Explicacion: {intentProbeResult?.decisionReason ?? 'N/A'}
                </Alert>
              )}
            </Card>

            {activePanel === 'templates' && (
              <Card variant="outlined" sx={{ p: 1.5, bgcolor: 'background.neutral' }}>
                <Stack direction={{ xs: 'column', md: 'row' }} spacing={1} flexWrap="wrap">
                    <Button variant="contained" onClick={handleCreateBlank}>Desde cero</Button>
                    <Button variant="outlined" onClick={handleCreateDefault}>Base de WhatsApp</Button>
                  {quickstarts.map((tpl) => (
                    <Button key={tpl.id} variant="outlined" onClick={() => handleUseTemplate(tpl)}>{tpl.name}</Button>
                  ))}
                </Stack>
              </Card>
            )}

            {editorMode === 'advanced' && (
              <Stack spacing={1.2}>
                <TextField label="ID interno" value={editor.id} onChange={(e) => setEditorField('id', e.target.value)} size="small" fullWidth />
                <TextField
                  label="JSON de definicion"
                  value={editor.definitionJson}
                  onChange={(e) => setDefinitionJson(e.target.value)}
                  multiline minRows={10} maxRows={18} fullWidth
                />
              </Stack>
            )}
          </Stack>
        </Card>
            )}

            {hasSelection && (
        <WorkflowVisualDesigner
          activities={activities}
          allowedTypes={uiAllowedTypes}
          requiredConfigByType={requiredConfigByType}
          validationErrors={designValidationErrors}
          runtimeKind={selectedRuntimeKind}
          triggerEventName={editor.triggerEventName}
          startIntents={startIntents}
          availableModels={availableModels}
          availableTools={availableTools}
          availableAgents={availableAgents}
          availableChannels={availableChannels}
          integrations={integrations}
          connectTemplates={connectTemplates}
          onAddActivity={addActivity}
          onChangeRuntimeKind={setSelectedRuntimeKind}
          onChangeTriggerEvent={(value) => setEditorField('triggerEventName', value)}
          onUpdateStartIntents={updateStartIntents}
          onUpdateActivity={updateActivity}
          onRemoveActivity={removeActivity}
          onOpenAiConfig={openAiConfig}
          onUpdateAiAgentConfig={updateAiAgentConfigAt}
          onAddActivityConfig={addActivityConfig}
          onUpdateActivityConfig={updateActivityConfig}
          onRemoveActivityConfig={removeActivityConfig}
        />
            )}
          </>
        )}

        {mainTab === 1 && (
          <Stack spacing={2.5}>
            <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.2} alignItems={{ md: 'center' }}>
              <TextField
                value={workflowSearch}
                onChange={(e) => setWorkflowSearch(e.target.value)}
                placeholder="Buscar por nombre, evento o ID"
                size="small"
                sx={{ maxWidth: 440 }}
                fullWidth
              />
              <Chip size="small" label={`${filteredWorkflows.length} de ${scopedWorkflows.length} automatizaciones`} />
              <Stack direction="row" spacing={1} sx={{ ml: { md: 'auto' } }}>
                <Button variant="contained" startIcon={<Iconify icon="mingcute:add-line" />} onClick={handleCreateBlank}>
                  Crear desde cero
                </Button>
                <Button variant="outlined" onClick={handleCreateDefault}>Base de WhatsApp</Button>
              </Stack>
            </Stack>

            <Grid container spacing={2}>
              {filteredWorkflows.map((workflow) => {
                const readiness = workflowReadiness(workflow);
                return (
                <Grid item xs={12} md={4} key={workflow.id}>
                  <Card
                    variant="outlined"
                    onClick={() => handleSelectWorkflow(workflow)}
                    sx={{
                      p: 2.2,
                      height: '100%',
                      cursor: 'pointer',
                      borderRadius: 2.5,
                      transition: '160ms ease',
                      '&:hover': { borderColor: 'primary.main', boxShadow: '0 16px 42px rgba(16,35,29,0.10)' },
                    }}
                  >
                    <Stack spacing={1.2}>
                      <Stack direction="row" justifyContent="space-between" spacing={1}>
                        <Iconify icon="mdi:source-branch" width={28} sx={{ color: 'primary.main' }} />
                        <Chip
                          size="small"
                          color={workflow.status === 'Published' ? 'success' : 'default'}
                          label={workflow.status === 'Published' ? 'Publicado' : 'Borrador'}
                        />
                      </Stack>
                      <Box>
                        <Typography variant="h6">{workflow.name}</Typography>
                        <Typography variant="caption" color="text.secondary">
                          {SYSTEM_EVENT_OPTIONS.find((ev) => ev.value === workflow.triggerEventName)?.label ?? (workflow.triggerEventName || 'sin evento')}
                        </Typography>
                      </Box>
                      <Stack direction="row" spacing={0.5} flexWrap="wrap">
                        <Chip size="small" color={readiness.percent >= 80 ? 'success' : 'warning'} label={`${readiness.percent}% listo`} />
                        <Chip size="small" label={`${readiness.intents.length} motivos`} />
                        <Chip size="small" color={readiness.hasAgent ? 'primary' : 'default'} label={readiness.hasAgent ? 'con asistente' : 'sin asistente'} />
                      </Stack>
                      <Typography variant="caption" color="text.secondary">
                        Actualizado: {workflow.updatedAt ? new Date(workflow.updatedAt).toLocaleString() : 'sin fecha'}
                      </Typography>
                      <Button size="small" variant="outlined">Abrir automatizacion</Button>
                    </Stack>
                  </Card>
                </Grid>
              );})}

              {filteredWorkflows.length === 0 && (
                <Grid item xs={12}>
                  <Card variant="outlined" sx={{ p: 4, textAlign: 'center', borderRadius: 3 }}>
                    <Iconify icon="mdi:graph-outline" width={42} sx={{ color: 'primary.main', mb: 1 }} />
                    <Typography variant="h6">{scopedWorkflows.length === 0 ? 'No hay automatizaciones creadas' : 'Sin resultados'}</Typography>
                    <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                      {scopedWorkflows.length === 0 ? 'Crea tu primera automatizacion.' : 'Cambia la busqueda.'}
                    </Typography>
                    <Button variant="contained" onClick={handleCreateBlank}>Crear automatizacion</Button>
                  </Card>
                </Grid>
              )}
            </Grid>
          </Stack>
        )}

        {mainTab === 2 && (
          <Stack spacing={2.5}>
            <Stack direction="row" justifyContent="flex-end">
              <TextField
                size="small"
                select
                label="Ventana de metricas"
                value={metricsWindow}
                onChange={(event) => setMetricsWindow(event.target.value as '24h' | '7d' | '30d')}
                sx={{ minWidth: 180 }}
              >
                <MenuItem value="24h">24 horas</MenuItem>
                <MenuItem value="7d">7 dias</MenuItem>
                <MenuItem value="30d">30 dias</MenuItem>
              </TextField>
            </Stack>
            <Grid container spacing={2}>
              {[
                { label: 'Automatizaciones', value: scopedWorkflows.length, helper: `${publishedCount} publicadas`, icon: 'mdi:source-branch' },
                { label: 'Ejecuciones', value: executions.length, helper: `${failedExecutions} fallidas`, icon: 'mdi:play-circle-outline' },
                { label: 'Nodos activos', value: activities.length, helper: hasSelection ? 'en configuracion' : 'sin flujo activo', icon: 'mdi:graph-outline' },
                { label: 'Asistentes en uso', value: availableAgents.filter((a) => !a.isSystemAgent).length, helper: 'asistentes personalizados', icon: 'mdi:robot-outline' },
              ].map((stat) => (
                <Grid item xs={6} md={3} key={stat.label}>
                  <Card variant="outlined" sx={{ p: 2 }}>
                    <Stack direction="row" spacing={1.5} alignItems="center">
                      <Box sx={{ width: 40, height: 40, borderRadius: 1.5, display: 'grid', placeItems: 'center', bgcolor: 'primary.lighter', color: 'primary.main' }}>
                        <Iconify icon={stat.icon} width={22} />
                      </Box>
                      <Box>
                        <Typography variant="h5">{stat.value}</Typography>
                        <Typography variant="caption" color="text.secondary">{stat.label}| {stat.helper}</Typography>
                      </Box>
                    </Stack>
                  </Card>
                </Grid>
              ))}
            </Grid>

            <RuntimeMetricsCard metrics={metrics} wizardMetrics={wizardMetrics} auditEvents={auditEvents} />
            <WorkflowExecutionsCard executions={executions} onOpenSteps={openSteps} onRetryExecution={retryExecution} />
          </Stack>
        )}
      </DashboardContent>

      <AiAgentConfigDialog
        open={aiDialogOpen}
        aiTab={aiTab}
        aiTarget={aiTarget}
        availableModels={availableModels}
        availableTools={availableTools}
        onTabChange={setAiTab}
        onClose={closeAiConfig}
        onUpdate={updateAiAgentConfig}
      />

      <ExecutionStepsDialog open={stepsOpen} steps={steps} onClose={() => setStepsOpen(false)} />
    </>
  );
}












