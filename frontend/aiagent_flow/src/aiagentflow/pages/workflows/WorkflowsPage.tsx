import { Helmet } from 'react-helmet-async';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

import { WORKFLOW_QUICKSTARTS } from './constants';
import { RuntimeMetricsCard } from './components/RuntimeMetricsCard';
import { AiAgentConfigDialog } from './components/AiAgentConfigDialog';
import { useWorkflowEditorState } from './hooks/useWorkflowEditorState';
import { ExecutionStepsDialog } from './components/ExecutionStepsDialog';
import { useWorkflowStudioRuntime } from './hooks/useWorkflowStudioRuntime';
import { WorkflowExecutionsCard } from './components/WorkflowExecutionsCard';
import { WorkflowVisualDesigner } from './components/WorkflowVisualDesigner';
import { WorkflowDefinitionsCard } from './components/WorkflowDefinitionsCard';

import type { WorkflowDefinition } from './types';

export default function WorkflowsPage() {
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
    applyTypePreset,
    addActivityConfig,
    updateActivityConfig,
    removeActivityConfig,
    openAiConfig,
    closeAiConfig,
    updateAiAgentConfig,
    updateAiAgentConfigAt,
    setSelectedWorkflowId,
  } = useWorkflowEditorState(activityCatalog);

  const handleSelectWorkflow = (wf: WorkflowDefinition) => {
    selectWorkflow(wf);
  };

  const handleCreateNew = () => {
    setSelectedWorkflowId(null);
    createNew();
  };

  return (
    <>
      <Helmet>
        <title>Studio Workflows | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 3, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Box>
            <Typography variant="h4">Studio Workflows</Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary', mt: 0.5 }}>
              Build, publish and run workflow automations connected to Connect channels.
            </Typography>
          </Box>
          <Stack direction="row" spacing={1}>
            <Button variant="outlined" startIcon={<Iconify icon="mdi:refresh" />} onClick={loadAll}>
              Refresh
            </Button>
            <Button variant="contained" startIcon={<Iconify icon="mingcute:add-line" />} onClick={handleCreateNew}>
              New
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
            <Card sx={{ p: 2 }}>
              <Typography variant="h6" sx={{ mb: 1 }}>Workflow Quickstarts</Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                Create ready-to-edit flows for Inbox, KYC and Payments.
              </Typography>
              <Stack direction="row" spacing={1} flexWrap="wrap">
                {WORKFLOW_QUICKSTARTS.map((tpl) => (
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
              <Typography variant="h6" sx={{ mb: 2 }}>
                Editor
              </Typography>
              <Stack spacing={2}>
                <TextField
                  label="Workflow ID"
                  value={editor.id}
                  onChange={(e) => setEditorField('id', e.target.value)}
                  fullWidth
                />
                <TextField
                  label="Name"
                  value={editor.name}
                  onChange={(e) => setEditorField('name', e.target.value)}
                  fullWidth
                />
                <TextField
                  label="Trigger Event"
                  value={editor.triggerEventName}
                  onChange={(e) => setEditorField('triggerEventName', e.target.value)}
                  fullWidth
                />
                <TextField
                  label="Definition JSON"
                  value={editor.definitionJson}
                  onChange={(e) => setDefinitionJson(e.target.value)}
                  multiline
                  minRows={14}
                  maxRows={24}
                  fullWidth
                />

                <WorkflowVisualDesigner
                  activities={activities}
                  allowedTypes={allowedTypes}
                  requiredConfigByType={requiredConfigByType}
                  validationErrors={validationErrors}
                  availableModels={availableModels}
                  availableTools={availableTools}
                  onAddActivity={addActivity}
                  onUpdateActivity={updateActivity}
                  onRemoveActivity={removeActivity}
                  onApplyTypePreset={applyTypePreset}
                  onOpenAiConfig={openAiConfig}
                  onUpdateAiAgentConfig={updateAiAgentConfigAt}
                  onAddActivityConfig={addActivityConfig}
                  onUpdateActivityConfig={updateActivityConfig}
                  onRemoveActivityConfig={removeActivityConfig}
                />

                <Stack direction="row" spacing={1}>
                  <Button
                    variant="contained"
                    onClick={() => saveWorkflow(editor, validationErrors)}
                    disabled={saving || !editor.id}
                  >
                    {saving ? 'Saving...' : 'Save'}
                  </Button>
                  <Button
                    variant="outlined"
                    onClick={() => publishWorkflow(editor.id, hasSelection, validationErrors)}
                    disabled={!hasSelection || validationErrors.length > 0}
                  >
                    Publish
                  </Button>
                  <Button
                    variant="outlined"
                    color="success"
                    onClick={() => runEvent(editor.triggerEventName)}
                    disabled={running}
                  >
                    {running ? 'Running...' : 'Run Event'}
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
