import { useState } from 'react';
import { Helmet } from 'react-helmet-async';

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

import type { WorkflowDefinition } from './types';

export default function WorkflowsPage() {
  const [editorMode, setEditorMode] = useState<'builder' | 'advanced'>('builder');
  const [designType, setDesignType] = useState<WorkflowDesignType>('workflow');
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

  const handleSelectWorkflow = (wf: WorkflowDefinition) => {
    selectWorkflow(wf);
  };

  const handleCreateNuevo = () => {
    setSelectedWorkflowId(null);
    createNew();
  };

  return (
    <>
      <Helmet>
        <title>Studio de Workflows | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 3, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Box>
            <Typography variant="h4">Brain Studio</Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary', mt: 0.5 }}>
              Construye flujos conversacionales con agentes, integraciones y acciones operativas.
            </Typography>
          </Box>
          <Stack direction="row" spacing={1}>
            <Button variant="outlined" startIcon={<Iconify icon="mdi:refresh" />} onClick={loadAll}>
              Actualizar
            </Button>
            <Button variant="contained" startIcon={<Iconify icon="mingcute:add-line" />} onClick={handleCreateNuevo}>
              Nuevo
            </Button>
          </Stack>
        </Box>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
            {error}
          </Alert>
        )}

        <Grid container spacing={3}>
          <Grid item xs={12}>
            <Grid container spacing={2}>
              {[
                ['Workflows', workflows.length, `${publishedCount} publicados`, 'mdi:source-branch'],
                ['Nodos del flujo', activities.length, readyToPublish ? 'Listo para publicar' : `${designValidationErrors.length} pendientes`, 'mdi:vector-polyline'],
                ['Ejecuciones', executions.length, failedExecutions ? `${failedExecutions} fallidas` : 'Sin fallas recientes', 'mdi:play-circle-outline'],
              ].map(([label, value, helper, icon]) => (
                <Grid item xs={12} md={4} key={label}>
                  <Card variant="outlined" sx={{ p: 2, height: '100%' }}>
                    <Stack direction="row" spacing={1.5} alignItems="center">
                      <Box
                        sx={{
                          width: 42,
                          height: 42,
                          borderRadius: 1.5,
                          display: 'grid',
                          placeItems: 'center',
                          bgcolor: 'primary.lighter',
                          color: 'primary.main',
                        }}
                      >
                        <Iconify icon={String(icon)} width={24} />
                      </Box>
                      <Box>
                        <Typography variant="h5">{String(value)}</Typography>
                        <Typography variant="caption" color="text.secondary">
                          {label} · {helper}
                        </Typography>
                      </Box>
                    </Stack>
                  </Card>
                </Grid>
              ))}
            </Grid>
          </Grid>

          <Grid item xs={12}>
            <Card
              variant="outlined"
              sx={{
                p: 2,
                borderRadius: 2,
                background:
                  'linear-gradient(135deg, rgba(38,103,255,0.06), rgba(14,165,163,0.06))',
              }}
            >
              <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2}>
                <Box>
                  <Typography variant="h6" sx={{ mb: 0.5 }}>Punto de partida</Typography>
                  <Typography variant="body2" color="text.secondary">
                    Selecciona una plantilla y ajusta el flujo en el canvas.
                  </Typography>
                </Box>
                <Chip
                  size="small"
                  color={readyToPublish ? 'success' : 'warning'}
                  label={readyToPublish ? 'Sin pendientes' : `${designValidationErrors.length} validaciones`}
                  sx={{ alignSelf: { xs: 'flex-start', md: 'center' } }}
                />
              </Stack>
              <Stack direction="row" spacing={1} flexWrap="wrap" sx={{ mt: 2 }}>
                {quickstarts.map((tpl) => (
                  <Button
                    key={tpl.id}
                    variant="outlined"
                    onClick={() => {
                      setSelectedWorkflowId(null);
                      setEditorField('id', tpl.id);
                      setEditorField('name', tpl.name);
                      setEditorField('triggerEventName', tpl.triggerEventName);
                      setDefinitionJson(tpl.definitionJson);
                    }}
                  >
                    {tpl.name}
                  </Button>
                ))}
              </Stack>
            </Card>
          </Grid>

          <Grid item xs={12} md={4}>
            <WorkflowDefinitionsCard
              loading={loading}
              workflows={workflows}
              selectedId={selectedWorkflowId}
              onSelect={handleSelectWorkflow}
            />
          </Grid>

          <Grid item xs={12} md={8}>
            <Card sx={{ p: 2 }}>
              <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={1.5} sx={{ mb: 2 }}>
                <Box>
                  <Typography variant="h6">Constructor</Typography>
                  <Typography variant="caption" color="text.secondary">
                    Builder para usuarios; avanzado solo para JSON y soporte tecnico.
                  </Typography>
                </Box>
                <Stack direction="row" spacing={1} flexWrap="wrap">
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
              </Stack>
              <Stack spacing={2}>
                <TextField
                  label="ID del workflow"
                  value={editor.id}
                  onChange={(e) => setEditorField('id', e.target.value)}
                  fullWidth
                />
                <TextField
                  label="Nombre"
                  value={editor.name}
                  onChange={(e) => setEditorField('name', e.target.value)}
                  fullWidth
                />
                <TextField
                  label="Evento disparador"
                  value={editor.triggerEventName}
                  onChange={(e) => setEditorField('triggerEventName', e.target.value)}
                  fullWidth
                />
                {editorMode === 'advanced' && (
                  <TextField
                    label="JSON de definicion"
                    value={editor.definitionJson}
                    onChange={(e) => setDefinitionJson(e.target.value)}
                    multiline
                    minRows={14}
                    maxRows={24}
                    fullWidth
                  />
                )}

                <WorkflowVisualDesigner
                  activities={activities}
                  allowedTypes={uiAllowedTypes}
                  requiredConfigByType={requiredConfigByType}
                  validationErrors={designValidationErrors}
                  availableModels={availableModels}
                  availableTools={availableTools}
                  integrations={integrations}
                  connectTemplates={connectTemplates}
                  onAddActivity={addActivity}
                  onUpdateActivity={updateActivity}
                  onRemoveActivity={removeActivity}
                  onOpenAiConfig={openAiConfig}
                  onUpdateAiAgentConfig={updateAiAgentConfigAt}
                  onAddActivityConfig={addActivityConfig}
                  onUpdateActivityConfig={updateActivityConfig}
                  onRemoveActivityConfig={removeActivityConfig}
                />

                <Stack direction="row" spacing={1}>
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
                  <Button
                    variant="outlined"
                    color="success"
                    onClick={() => runEvent(editor.triggerEventName)}
                    disabled={running}
                  >
                    {running ? 'Ejecutando...' : 'Ejecutar evento'}
                  </Button>
                </Stack>
              </Stack>
            </Card>
          </Grid>
        </Grid>

        <Grid container spacing={3} sx={{ mt: 1 }}>
          <Grid item xs={12} md={8}>
            <WorkflowExecutionsCard executions={executions} onOpenSteps={openSteps} onRetryExecution={retryExecution} />
          </Grid>

          <Grid item xs={12} md={4}>
            <RuntimeMetricsCard metrics={metrics} auditEvents={auditEvents} />
          </Grid>
        </Grid>
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


