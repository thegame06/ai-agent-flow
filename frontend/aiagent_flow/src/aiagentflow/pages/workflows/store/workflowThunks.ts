import { createAsyncThunk } from '@reduxjs/toolkit';

import { workflowStudioApi } from '../services/workflowStudioApi';


type RuntimePayload = {
  workflows: any[];
  executions: any[];
  metrics: any;
  wizardMetrics: any;
  auditEvents: any[];
  activityCatalog: any[];
  availableModels: any[];
  availableTools: any[];
  availableAgents: any[];
  availableChannels: any[];
  integrations: any[];
  connectTemplates: any[];
};

const asArray = (value: any): any[] => {
  if (Array.isArray(value)) return value;
  if (Array.isArray(value?.items)) return value.items;
  if (Array.isArray(value?.data)) return value.data;
  if (Array.isArray(value?.workflows)) return value.workflows;
  if (Array.isArray(value?.results)) return value.results;
  return [];
};

const apiErrorMessage = (error: any, fallback: string) => {
  if (typeof error === 'string') return error;
  if (typeof error?.message === 'string') return error.message;
  if (typeof error?.error === 'string') return error.error;
  if (typeof error?.title === 'string') return error.title;
  if (typeof error?.response?.data?.message === 'string') return error.response.data.message;
  if (typeof error?.response?.data?.error === 'string') return error.response.data.error;
  return fallback;
};

export const fetchWorkflowRuntimeData = createAsyncThunk(
  'workflowRuntime/fetchRuntimeData',
  async (
    payload: { tenantId: string; metricsWindow?: '24h' | '7d' | '30d' },
    { rejectWithValue }
  ): Promise<RuntimePayload> => {
    const { tenantId, metricsWindow = '24h' } = payload;
    const safe = async <T>(request: Promise<{ data: T }>, fallback: T) => {
      try {
        return await request;
      } catch {
        return { data: fallback };
      }
    };

    let wfRes: { data: any };
    try {
      wfRes = await workflowStudioApi.getDefinitions(tenantId);
    } catch (error: any) {
      return rejectWithValue(apiErrorMessage(error, 'No se pudieron cargar los workflows.')) as never;
    }
    const [exRes, metRes, wizRes, auditRes, catalogRes, modelsRes, toolsRes, agentsRes, channelsRes, integrationsRes, templatesRes] =
      await Promise.all([
        safe(workflowStudioApi.getExecutions(tenantId), []),
        safe(workflowStudioApi.getMetrics(tenantId, metricsWindow), null),
        safe(workflowStudioApi.getWizardMetrics(tenantId), null),
        safe(workflowStudioApi.getAuditEvents(tenantId), []),
        safe(workflowStudioApi.getCatalogActivities(tenantId), []),
        safe(workflowStudioApi.getModels(), []),
        safe(workflowStudioApi.getTools(), []),
        safe(workflowStudioApi.getAgents(tenantId), []),
        safe(workflowStudioApi.getChannels(tenantId), []),
        safe(workflowStudioApi.getIntegrationStatus(tenantId), []),
        safe(workflowStudioApi.getConnectTemplates(tenantId), []),
      ]);
    const integrations = asArray(integrationsRes.data);

    return {
      workflows: asArray(wfRes.data),
      executions: asArray(exRes.data),
      metrics: metRes.data ?? null,
      wizardMetrics: wizRes.data ?? null,
      auditEvents: asArray(auditRes.data),
      activityCatalog: asArray(catalogRes.data),
      availableModels: asArray(modelsRes.data),
      availableTools: asArray(toolsRes.data),
      availableAgents: asArray(agentsRes.data),
      availableChannels: asArray(channelsRes.data),
      integrations,
      connectTemplates: asArray(templatesRes.data),
    };
  }
);

export const saveWorkflowDraft = createAsyncThunk(
  'workflowRuntime/saveWorkflowDraft',
  async ({
    tenantId,
    workflow,
  }: {
    tenantId: string;
    workflow: {
      id: string;
      name: string;
      triggerEventName: string;
      definitionJson: string;
      runtimeKind?: string;
    };
  }, { rejectWithValue }) => {
    try {
      JSON.parse(workflow.definitionJson);
    } catch {
      return rejectWithValue('El JSON del workflow no es valido.');
    }

    try {
      const response = await workflowStudioApi.upsertDefinition(tenantId, workflow.id, {
        name: workflow.name.trim(),
        triggerEventName: workflow.triggerEventName.trim(),
        runtimeKind: workflow.runtimeKind ?? 'Text',
        definitionJson: workflow.definitionJson,
        metadata: {
          designType: (workflow as any).designType ?? 'workflow',
        },
      });
      return response.data ?? workflow;
    } catch (error: any) {
      return rejectWithValue(apiErrorMessage(error, 'No se pudo guardar el workflow.'));
    }
  }
);

export const publishWorkflowDefinition = createAsyncThunk(
  'workflowRuntime/publishWorkflowDefinition',
  async ({
    tenantId,
    workflowId,
  }: {
    tenantId: string;
    workflowId: string;
  }) => {
    await workflowStudioApi.publishDefinition(tenantId, workflowId);
    return workflowId;
  }
);

export const runWorkflowEvent = createAsyncThunk(
  'workflowRuntime/runWorkflowEvent',
  async ({
    tenantId,
    eventName,
  }: {
    tenantId: string;
    eventName: string;
  }) => {
    await workflowStudioApi.runEvent(tenantId, {
      eventName: eventName || 'connect.message.received',
      payload: {
        channel: 'whatsapp',
        recipient: '5215555555555',
        customerName: 'Demo User',
        content: 'Hola desde Studio Workflows',
      },
    });
    return true;
  }
);

export const retryWorkflowExecution = createAsyncThunk(
  'workflowRuntime/retryWorkflowExecution',
  async ({
    tenantId,
    executionId,
  }: {
    tenantId: string;
    executionId: string;
  }) => {
    await workflowStudioApi.retryExecution(tenantId, executionId);
    return executionId;
  }
);

export const fetchWorkflowExecutionSteps = createAsyncThunk(
  'workflowRuntime/fetchWorkflowExecutionSteps',
  async ({
    tenantId,
    executionId,
  }: {
    tenantId: string;
    executionId: string;
  }) => {
    const res = await workflowStudioApi.getExecutionSteps(tenantId, executionId);
    return res.data ?? [];
  }
);
