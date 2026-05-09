import { useEffect, useCallback } from 'react';

import { useAppDispatch, useAppSelector } from 'src/aiagentflow/store/hooks';

import { setWorkflowStepsOpen, setWorkflowRuntimeError } from '../store/workflowRuntimeSlice';
import {
  runWorkflowEvent,
  saveWorkflowDraft,
  retryWorkflowExecution,
  fetchWorkflowRuntimeData,
  publishWorkflowDefinition,
  fetchWorkflowExecutionSteps,
} from '../store/workflowThunks';

export function useWorkflowStudioRuntime(tenantId: string) {
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
  const auditEvents = useAppSelector((state) => state.workflowRuntime.auditEvents);
  const activityCatalog = useAppSelector((state) => state.workflowRuntime.activityCatalog);
  const availableModels = useAppSelector((state) => state.workflowRuntime.availableModels);
  const availableTools = useAppSelector((state) => state.workflowRuntime.availableTools);
  const integrations = useAppSelector((state) => state.workflowRuntime.integrations);

  const loadAll = useCallback(async () => {
    await dispatch(fetchWorkflowRuntimeData(tenantId));
  }, [dispatch, tenantId]);

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
    },
    _validationErrors: string[]
  ) => {
    if (!workflow.id || !workflow.name.trim()) return;
    const result = await dispatch(saveWorkflowDraft({ tenantId, workflow }));
    if (saveWorkflowDraft.fulfilled.match(result)) {
      await dispatch(fetchWorkflowRuntimeData(tenantId));
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
      await dispatch(fetchWorkflowRuntimeData(tenantId));
    }
  };

  const runEvent = async (eventName: string) => {
    const result = await dispatch(runWorkflowEvent({ tenantId, eventName }));
    if (runWorkflowEvent.fulfilled.match(result)) {
      await dispatch(fetchWorkflowRuntimeData(tenantId));
    }
  };

  const retryExecution = async (executionId: string) => {
    const result = await dispatch(retryWorkflowExecution({ tenantId, executionId }));
    if (retryWorkflowExecution.fulfilled.match(result)) {
      await dispatch(fetchWorkflowRuntimeData(tenantId));
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
    auditEvents,
    activityCatalog,
    availableModels,
    availableTools,
    integrations,
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
