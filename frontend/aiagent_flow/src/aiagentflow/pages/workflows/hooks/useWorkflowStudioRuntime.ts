import { useEffect, useCallback } from 'react';

import { useAppDispatch, useAppSelector } from 'src/aiagentflow/store/hooks';

import { markSaved } from '../store/workflowEditorSlice';
import { setWorkflowStepsOpen, setWorkflowRuntimeError } from '../store/workflowRuntimeSlice';
import {
  runWorkflowEvent,
  saveWorkflowDraft,
  retryWorkflowExecution,
  fetchWorkflowRuntimeData,
  publishWorkflowDefinition,
  fetchWorkflowExecutionSteps,
} from '../store/workflowThunks';

export function useWorkflowStudioRuntime(tenantId: string, metricsWindow: '24h' | '7d' | '30d' = '24h') {
  const dispatch = useAppDispatch();

  const loading = useAppSelector((state) => state.workflowRuntime.loading);
  const saving = useAppSelector((state) => state.workflowRuntime.saving);
  const running = useAppSelector((state) => state.workflowRuntime.running);
  const error = useAppSelector((state) => state.workflowRuntime.error);
  const workflows = useAppSelector((state) => state.workflowRuntime.workflows);
  const executions = useAppSelector((state) => state.workflowRuntime.executions);
  const steps = useAppSelector((state) => state.workflowRuntime.steps);
  const stepsOpen = useAppSelector((state) => state.workflowRuntime.stepsOpen);
  const metrics = useAppSelector((state) => state.workflowRuntime.metrics);
  const wizardMetrics = useAppSelector((state) => state.workflowRuntime.wizardMetrics);
  const auditEvents = useAppSelector((state) => state.workflowRuntime.auditEvents);
  const activityCatalog = useAppSelector((state) => state.workflowRuntime.activityCatalog);
  const availableModels = useAppSelector((state) => state.workflowRuntime.availableModels);
  const availableTools = useAppSelector((state) => state.workflowRuntime.availableTools);
  const availableAgents = useAppSelector((state) => state.workflowRuntime.availableAgents);
  const availableChannels = useAppSelector((state) => state.workflowRuntime.availableChannels);
  const integrations = useAppSelector((state) => state.workflowRuntime.integrations);
  const connectTemplates = useAppSelector((state) => state.workflowRuntime.connectTemplates);

  const loadAll = useCallback(async () => {
    await dispatch(fetchWorkflowRuntimeData({ tenantId, metricsWindow }));
  }, [dispatch, tenantId, metricsWindow]);

  useEffect(() => {
    loadAll();
  }, [loadAll]);

  const saveWorkflow = async (
    workflow: {
      id: string;
      name: string;
      triggerEventName: string;
      definitionJson: string;
      designType?: 'workflow' | 'tool';
      runtimeKind?: string;
    },
    _validationErrors: string[]
  ) => {
    if (!workflow.id || !workflow.name.trim()) return;
    const result = await dispatch(saveWorkflowDraft({ tenantId, workflow }));
    if (saveWorkflowDraft.fulfilled.match(result)) {
      dispatch(markSaved());
      await dispatch(fetchWorkflowRuntimeData({ tenantId, metricsWindow }));
    }
  };

  const publishWorkflow = async (
    workflowId: string,
    hasSelection: boolean,
    validationErrors: string[]
  ) => {
    if (!hasSelection) return;
    if (validationErrors.length > 0) {
      dispatch(setWorkflowRuntimeError(`Validation failed: ${validationErrors[0]}`));
      return;
    }
    const result = await dispatch(publishWorkflowDefinition({ tenantId, workflowId }));
    if (publishWorkflowDefinition.fulfilled.match(result)) {
      await dispatch(fetchWorkflowRuntimeData({ tenantId, metricsWindow }));
    }
  };

  const runEvent = async (eventName: string) => {
    const result = await dispatch(runWorkflowEvent({ tenantId, eventName }));
    if (runWorkflowEvent.fulfilled.match(result)) {
      await dispatch(fetchWorkflowRuntimeData({ tenantId, metricsWindow }));
    }
  };

  const retryExecution = async (executionId: string) => {
    const result = await dispatch(retryWorkflowExecution({ tenantId, executionId }));
    if (retryWorkflowExecution.fulfilled.match(result)) {
      await dispatch(fetchWorkflowRuntimeData({ tenantId, metricsWindow }));
    }
  };

  const openSteps = async (executionId: string) => {
    await dispatch(fetchWorkflowExecutionSteps({ tenantId, executionId }));
  };

  return {
    loading,
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
    setError: (value: string | null) => dispatch(setWorkflowRuntimeError(value)),
    setStepsOpen: (value: boolean) => dispatch(setWorkflowStepsOpen(value)),
    loadAll,
    saveWorkflow,
    publishWorkflow,
    runEvent,
    retryExecution,
    openSteps,
  };
}
