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
  AgentOption,
  WorkflowStep,
  ChannelOption,
  WorkflowExecution,
  WorkflowAuditEvent,
  WorkflowDefinition,
  ConnectTemplateOption,
  WorkflowRuntimeMetrics,
  AssistantWizardMetrics,
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
  wizardMetrics: null,
  auditEvents: [],
  activityCatalog: [],
  availableModels: [],
  availableTools: [],
  availableAgents: [],
  availableChannels: [],
  integrations: [],
  connectTemplates: [],
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
        state.wizardMetrics = action.payload.wizardMetrics as AssistantWizardMetrics | null;
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
        state.availableAgents = ((action.payload.availableAgents as any[]) ?? []).map(
          (agent) =>
            ({
              id: agent.id,
              name: agent.name,
              description: agent.description,
              status: agent.status,
              version: agent.version,
              tags: agent.tags ?? [],
              updatedAt: agent.updatedAt,
              stepsCount: agent.stepsCount ?? agent.StepsCount ?? 0,
              toolsCount: agent.toolsCount ?? agent.ToolsCount ?? 0,
              primaryModel: agent.primaryModel ?? agent.PrimaryModel ?? '',
              provider: agent.provider ?? agent.Provider ?? '',
              isSystemAgent: agent.isSystemAgent ?? agent.IsSystemAgent ?? false,
              systemRole: agent.systemRole ?? agent.SystemRole ?? '',
            }) as AgentOption
        );
        state.availableChannels = ((action.payload.availableChannels as any[]) ?? []).map(
          (channel) => {
            const config = channel.config ?? channel.Config ?? {};
            const intentAgents = String(config.IntentAgents ?? config.intentAgents ?? config.RoutingAgents ?? config.routingAgents ?? '')
              .split(',')
              .map((value) => value.trim())
              .filter(Boolean);
            return {
              id: channel.id,
              name: channel.name,
              type: channel.type,
              status: channel.status,
              config,
              routerAgentId: config.RouterAgentId ?? config.routerAgentId ?? '',
              defaultAgentId: config.DefaultAgentId ?? config.defaultAgentId ?? '',
              intentAgents,
              routingAgents: intentAgents,
            } as ChannelOption;
          }
        );
        state.integrations = (action.payload.integrations as any[]) ?? [];
        state.connectTemplates = ((action.payload.connectTemplates as any[]) ?? []).map(
          (template) =>
            ({
              id: template.id,
              name: template.name,
              channel: template.channel,
              body: template.body,
            }) as ConnectTemplateOption
        );
      })
      .addCase(fetchWorkflowRuntimeData.rejected, (state, action) => {
        state.loading = false;
        state.error =
          (typeof action.payload === 'string' ? action.payload : action.error.message) ??
          'Failed to load workflows';
      })
      .addCase(saveWorkflowDraft.pending, (state) => {
        state.saving = true;
      })
      .addCase(saveWorkflowDraft.fulfilled, (state) => {
        state.saving = false;
      })
      .addCase(saveWorkflowDraft.rejected, (state, action) => {
        state.saving = false;
        state.error =
          (typeof action.payload === 'string' ? action.payload : action.error.message) ??
          'Failed to save workflow';
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
