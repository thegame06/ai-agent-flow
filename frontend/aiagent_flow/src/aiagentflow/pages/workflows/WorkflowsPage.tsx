import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { RuntimeMetricsCard } from './components/RuntimeMetricsCard';
import { AiAgentConfigDialog } from './components/AiAgentConfigDialog';
import { useWorkflowEditorState } from './hooks/useWorkflowEditorState';
import { ExecutionStepsDialog } from './components/ExecutionStepsDialog';
import { useWorkflowStudioRuntime } from './hooks/useWorkflowStudioRuntime';
import { WorkflowExecutionsCard } from './components/WorkflowExecutionsCard';
import { WorkflowVisualDesigner } from './components/WorkflowVisualDesigner';
import { WorkflowDefinitionsCard } from './components/WorkflowDefinitionsCard';
import { TOOL_ACTIVITY_TYPES, WORKFLOW_QUICKSTARTS, type WorkflowDesignType } from './constants';

import type { WorkflowDefinition, WorkflowStartIntent } from './types';

type ActivePanel = 'none' | 'flows' | 'templates' | 'metrics' | 'executions';

const defaultStartIntents = (eventName: string): WorkflowStartIntent[] => [
  {
    id: 'intent-main',
    label: 'Intencion principal',
    description: 'Frases o eventos que deben iniciar este flujo.',
    examples: ['Quiero informacion', 'Necesito ayuda'],
    eventName,
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

export default function WorkflowsPage() {
  const [editorMode, setEditorMode] = useState<'builder' | 'advanced'>('builder');
  const [designType, setDesignType] = useState<WorkflowDesignType>('workflow');
  const [activePanel, setActivePanel] = useState<ActivePanel>('none');
  const [syncingIntents, setSyncingIntents] = useState(false);
  const tenantId = useTenantId();
  const {
    loading,
    saving,
    running,
    error,
    workflows,
    executions,
    steps,
    stepsOpen,
    metrics,
    auditEvents,
    activityCatalog,
    availableModels,
    availableTools,
    availableAgents,
    availableChannels,
    integrations,
    connectTemplates,
    setError,
    setStepsOpen,
    loadAll,
    saveWorkflow,
    publishWorkflow,
    runEvent,
    retryExecution,
    openSteps,
  } = useWorkflowStudioRuntime(tenantId);

  const {
    editor,
    activities,
    selectedWorkflowId,
    isDirty,
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

  const uiAllowedTypes =
    designType === 'tool' ? allowedTypes.filter((t) => TOOL_ACTIVITY_TYPES.includes(t as any)) : allowedTypes;
  const quickstarts =
    designType === 'tool'
      ? WORKFLOW_QUICKSTARTS.filter((q) => q.id.includes('kyc') || q.id.includes('payment'))
      : WORKFLOW_QUICKSTARTS;
  const designValidationErrors = [
    ...validationErrors,
    ...(designType === 'tool'
      ? activities
          .filter((a) => !TOOL_ACTIVITY_TYPES.includes(a.type as any))
          .map((a) => `El nodo ${a.id || a.type} no esta permitido en modo Tool tecnica.`)
      : []),
  ];
  const publishedCount = workflows.filter((wf) => wf.status === 'Published').length;
  const failedExecutions = executions.filter((execution) => execution.status === 'Failed').length;
  const readyToPublish = hasSelection && designValidationErrors.length === 0;
  const latestWorkflow = useMemo(
    () =>
      [...workflows].sort(
        (a, b) => new Date(b.updatedAt || 0).getTime() - new Date(a.updatedAt || 0).getTime()
      )[0],
    [workflows]
  );
  const selectedWorkflow = useMemo(
    () => workflows.find((wf) => wf.id === selectedWorkflowId) ?? null,
    [selectedWorkflowId, workflows]
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

  useEffect(() => {
    if (!latestWorkflow || selectedWorkflowId || editor.id || isDirty) return;
    selectWorkflow(latestWorkflow);
  }, [editor.id, isDirty, latestWorkflow, selectWorkflow, selectedWorkflowId]);

  const handleSelectWorkflow = (wf: WorkflowDefinition) => {
    selectWorkflow(wf);
    setActivePanel('none');
  };

  const handleCreateBlank = () => {
    setSelectedWorkflowId(null);
    setEditorField('id', `wf_${Date.now()}`);
    setEditorField('name', 'Nuevo flujo');
    setEditorField('triggerEventName', 'connect.message.received');
    setDefinitionJson(JSON.stringify({ activities: [] }, null, 2));
    setActivePanel('none');
  };

  const handleCreateDefault = () => {
    setSelectedWorkflowId(null);
    createNew();
    setActivePanel('none');
  };

  const handleUseTemplate = (tpl: (typeof WORKFLOW_QUICKSTARTS)[number]) => {
    setSelectedWorkflowId(null);
    setEditorField('id', tpl.id);
    setEditorField('name', tpl.name);
    setEditorField('triggerEventName', tpl.triggerEventName);
    setDefinitionJson(tpl.definitionJson);
    setActivePanel('none');
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
    const sourceAgentId = workflowChannel?.defaultAgentId || workflowChannel?.routingAgents?.[0] || '';
    const targetAgentId = firstAgentNode?.config?.agentId || firstAgentNode?.aiAgent?.agentId || sourceAgentId;
    const channel = workflowChannel?.type?.toLowerCase();

    if (!sourceAgentId || !targetAgentId) {
      setError('No se pudieron sincronizar intenciones: configura un canal con agente asignado y un nodo Agente de IA.');
      return;
    }

    try {
      setSyncingIntents(true);
      await Promise.all(
        startIntents.map((intent, index) =>
          axios.post(endpoints.agentflow.intentRouting.rules(tenantId), {
            id: `brain-${editor.id || 'draft'}-${intent.id}`,
            intentKey: intent.label || intent.id,
            sourceAgentId,
            targetAgentId,
            priority: 100 + index,
            enabled: true,
            channel,
            conditionsJson: JSON.stringify({
              workflowId: editor.id,
              workflowName: editor.name,
              eventName: editor.triggerEventName,
              examples: intent.examples ?? [],
              description: intent.description ?? '',
            }),
            handoffPolicyJson: JSON.stringify({ source: 'brain-studio' }),
          })
        )
      );
      setError(null);
    } catch (err: any) {
      setError(err?.message || 'No se pudieron sincronizar las intenciones con intent-routing.');
    } finally {
      setSyncingIntents(false);
    }
  };

  return (
    <>
      <Helmet>
        <title>Brain Studio | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        {error && (
          <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
            {error}
          </Alert>
        )}

        <Card variant="outlined" sx={{ p: 2, mb: 2, borderRadius: 2 }}>
          <Stack spacing={2}>
            <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" spacing={2}>
              <Box>
                <Typography variant="h4">Brain Studio</Typography>
                <Typography variant="body2" color="text.secondary">
                  {editor.name || selectedWorkflow?.name || latestWorkflow?.name || 'Selecciona o crea un flujo'}
                </Typography>
              </Box>
              <Stack direction="row" spacing={1} flexWrap="wrap">
                <Button
                  variant={activePanel === 'templates' ? 'contained' : 'outlined'}
                  startIcon={<Iconify icon="mingcute:add-line" />}
                  onClick={() => setActivePanel(activePanel === 'templates' ? 'none' : 'templates')}
                >
                  Nuevo
                </Button>
                <Button
                  variant={activePanel === 'flows' ? 'contained' : 'outlined'}
                  startIcon={<Iconify icon="mdi:folder-multiple-outline" />}
                  onClick={() => setActivePanel(activePanel === 'flows' ? 'none' : 'flows')}
                >
                  Ver todos
                </Button>
                <Button
                  variant={activePanel === 'metrics' ? 'contained' : 'outlined'}
                  startIcon={<Iconify icon="mdi:chart-box-outline" />}
                  onClick={() => setActivePanel(activePanel === 'metrics' ? 'none' : 'metrics')}
                >
                  Metricas
                </Button>
                <Button
                  variant={activePanel === 'executions' ? 'contained' : 'outlined'}
                  startIcon={<Iconify icon="mdi:play-circle-outline" />}
                  onClick={() => setActivePanel(activePanel === 'executions' ? 'none' : 'executions')}
                >
                  Ejecuciones
                </Button>
                <Button variant="outlined" startIcon={<Iconify icon="mdi:refresh" />} onClick={loadAll}>
                  Actualizar
                </Button>
              </Stack>
            </Stack>

            <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.2} alignItems={{ md: 'center' }}>
              <TextField
                label="Flujo actual"
                value={editor.name}
                onChange={(e) => setEditorField('name', e.target.value)}
                size="small"
                sx={{ flex: 1 }}
              />
              <TextField
                label="Evento"
                value={editor.triggerEventName}
                onChange={(e) => setEditorField('triggerEventName', e.target.value)}
                size="small"
                sx={{ flex: 1 }}
              />
              <Chip size="small" label={`${activities.length} nodos`} />
              <Chip
                size="small"
                color={readyToPublish ? 'success' : 'warning'}
                label={readyToPublish ? 'Listo' : `${designValidationErrors.length} validaciones`}
              />
              <Button
                size="small"
                variant="outlined"
                color="info"
                onClick={syncIntentsToRouting}
                disabled={syncingIntents || startIntents.length === 0}
                startIcon={<Iconify icon="mdi:source-branch-sync" />}
              >
                {syncingIntents ? 'Sincronizando...' : 'Sincronizar intenciones'}
              </Button>
              <ToggleButtonGroup
                value={editorMode}
                exclusive
                size="small"
                onChange={(_, v) => {
                  if (v) setEditorMode(v);
                }}
              >
                <ToggleButton value="builder">Builder</ToggleButton>
                <ToggleButton value="advanced">Avanzado</ToggleButton>
              </ToggleButtonGroup>
              <ToggleButtonGroup
                value={designType}
                exclusive
                size="small"
                onChange={(_, v) => {
                  if (v) setDesignType(v);
                }}
              >
                <ToggleButton value="workflow">Workflow</ToggleButton>
                <ToggleButton value="tool">Tool</ToggleButton>
              </ToggleButtonGroup>
            </Stack>

            {activePanel === 'templates' && (
              <Card variant="outlined" sx={{ p: 1.5, bgcolor: 'background.neutral' }}>
                <Stack direction={{ xs: 'column', md: 'row' }} spacing={1} flexWrap="wrap">
                  <Button variant="contained" onClick={handleCreateBlank}>
                    Desde cero
                  </Button>
                  <Button variant="outlined" onClick={handleCreateDefault}>
                    Base WhatsApp
                  </Button>
                  {quickstarts.map((tpl) => (
                    <Button key={tpl.id} variant="outlined" onClick={() => handleUseTemplate(tpl)}>
                      {tpl.name}
                    </Button>
                  ))}
                </Stack>
              </Card>
            )}

            {activePanel === 'flows' && (
              <WorkflowDefinitionsCard
                loading={loading}
                workflows={workflows}
                selectedId={selectedWorkflowId}
                onSelect={handleSelectWorkflow}
              />
            )}

            {activePanel === 'metrics' && (
              <Grid container spacing={2}>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 2 }}>
                    <Typography variant="h5">{workflows.length}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      Flujos · {publishedCount} publicados
                    </Typography>
                  </Card>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 2 }}>
                    <Typography variant="h5">{activities.length}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      Nodos · {readyToPublish ? 'listo para publicar' : `${designValidationErrors.length} pendientes`}
                    </Typography>
                  </Card>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 2 }}>
                    <Typography variant="h5">{executions.length}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      Ejecuciones · {failedExecutions ? `${failedExecutions} fallidas` : 'sin fallas recientes'}
                    </Typography>
                  </Card>
                </Grid>
                <Grid item xs={12}>
                  <RuntimeMetricsCard metrics={metrics} auditEvents={auditEvents} />
                </Grid>
              </Grid>
            )}

            {activePanel === 'executions' && (
              <WorkflowExecutionsCard executions={executions} onOpenSteps={openSteps} onRetryExecution={retryExecution} />
            )}

            {editorMode === 'advanced' && (
              <Stack spacing={1.2}>
                <TextField
                  label="ID interno del workflow"
                  value={editor.id}
                  onChange={(e) => setEditorField('id', e.target.value)}
                  size="small"
                  fullWidth
                />
                <TextField
                  label="JSON de definicion"
                  value={editor.definitionJson}
                  onChange={(e) => setDefinitionJson(e.target.value)}
                  multiline
                  minRows={10}
                  maxRows={18}
                  fullWidth
                />
              </Stack>
            )}
          </Stack>
        </Card>

        <WorkflowVisualDesigner
          activities={activities}
          allowedTypes={uiAllowedTypes}
          requiredConfigByType={requiredConfigByType}
          validationErrors={designValidationErrors}
          triggerEventName={editor.triggerEventName}
          startIntents={startIntents}
          availableModels={availableModels}
          availableTools={availableTools}
          availableAgents={availableAgents}
          availableChannels={availableChannels}
          integrations={integrations}
          connectTemplates={connectTemplates}
          onAddActivity={addActivity}
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

        <Stack direction="row" spacing={1} sx={{ mt: 2 }} flexWrap="wrap">
          <Button
            variant="contained"
            onClick={() => saveWorkflow({ ...editor, designType }, designValidationErrors)}
            disabled={saving || !editor.id}
          >
            {saving ? 'Guardando...' : 'Guardar borrador'}
          </Button>
          <Button
            variant="outlined"
            onClick={() => publishWorkflow(editor.id, hasSelection, designValidationErrors)}
            disabled={!hasSelection || designValidationErrors.length > 0}
          >
            Publicar
          </Button>
          <Button variant="outlined" color="success" onClick={() => runEvent(editor.triggerEventName)} disabled={running}>
            {running ? 'Ejecutando...' : 'Ejecutar evento'}
          </Button>
        </Stack>
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


