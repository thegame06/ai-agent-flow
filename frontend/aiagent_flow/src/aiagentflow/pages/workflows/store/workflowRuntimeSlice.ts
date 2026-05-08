import type { PayloadAction } from '@reduxjs/toolkit';

import { createSlice } from '@reduxjs/toolkit';

import {
  runWorkflowEvent,
  saveWorkflowDraft,
  retryWorkflowExecution,
  fetchWorkflowRuntimeData,
  publishWorkflowDefinition,
  fetchWorkflowExecutionSteps,
} from './workflowThunks';

import type { WorkflowRuntimeState } from './types';
import type {
  WorkflowStep,
  WorkflowExecution,
  WorkflowAuditEvent,
  WorkflowDefinition,
  WorkflowRuntimeMetrics,
  WorkflowActivityCatalogEntry,
} from '../types';

const initialState: WorkflowRuntimeState = {
  loading: false,
  saving: false,
  running: false,
  error: null,
  workflows: [],
  executions: [],
  steps: [],
  stepsOpen: false,
  metrics: null,
  auditEvents: [],
  activityCatalog: [],
  availableModels: [],
  availableTools: [],
};

const workflowRuntimeSlice = createSlice({
  name: 'workflowRuntime',
  initialState,
  reducers: {
    setWorkflowRuntimeError(state, action: PayloadAction<string | null>) {
      state.error = action.payload;
    },
    setWorkflowStepsOpen(state, action: PayloadAction<boolean>) {
      state.stepsOpen = action.payload;
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchWorkflowRuntimeData.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchWorkflowRuntimeData.fulfilled, (state, action) => {
        state.loading = false;
        state.workflows = action.payload.workflows as WorkflowDefinition[];
        state.executions = action.payload.executions as WorkflowExecution[];
        state.metrics = action.payload.metrics as WorkflowRuntimeMetrics | null;
        state.auditEvents = action.payload.auditEvents as WorkflowAuditEvent[];
        state.activityCatalog = action.payload.activityCatalog as WorkflowActivityCatalogEntry[];
        state.availableModels = (action.payload.availableModels as any[]).map((m) => ({
          modelId: m.modelId,
          displayName: m.displayName,
        }));
        state.availableTools = (action.payload.availableTools as any[]).map((t) => ({
          key: t.key,
          displayName: t.displayName,
        }));
      })
      .addCase(fetchWorkflowRuntimeData.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message ?? 'Failed to load workflows';
      })
      .addCase(saveWorkflowDraft.pending, (state) => {
        state.saving = true;
      })
      .addCase(saveWorkflowDraft.fulfilled, (state) => {
        state.saving = false;
      })
      .addCase(saveWorkflowDraft.rejected, (state, action) => {
        state.saving = false;
        state.error = action.error.message ?? 'Failed to save workflow';
      })
      .addCase(runWorkflowEvent.pending, (state) => {
        state.running = true;
      })
      .addCase(runWorkflowEvent.fulfilled, (state) => {
        state.running = false;
      })
      .addCase(runWorkflowEvent.rejected, (state, action) => {
        state.running = false;
        state.error = action.error.message ?? 'Failed to run workflow event';
      })
      .addCase(publishWorkflowDefinition.rejected, (state, action) => {
        state.error = action.error.message ?? 'Failed to publish workflow';
      })
      .addCase(retryWorkflowExecution.rejected, (state, action) => {
        state.error = action.error.message ?? 'Failed to retry execution';
      })
      .addCase(fetchWorkflowExecutionSteps.fulfilled, (state, action) => {
        state.steps = action.payload as WorkflowStep[];
        state.stepsOpen = true;
      })
      .addCase(fetchWorkflowExecutionSteps.rejected, (state, action) => {
        state.error = action.error.message ?? 'Failed to load execution steps';
      });
  },
});

export const { setWorkflowRuntimeError, setWorkflowStepsOpen } =
  workflowRuntimeSlice.actions;

export default workflowRuntimeSlice.reducer;
